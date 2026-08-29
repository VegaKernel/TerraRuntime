using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTileChestRewriterTests
{
    [Fact]
    public void Rewritten_tile_chest_slice_loads_and_preserves_opaque_sections_byte_for_byte()
    {
        byte[] sourceFile = CreateCompleteCurrentWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoadDiagnostic sourceDiagnostic = WorldFileLoader.TryLoad(
            sourceFile,
            limits,
            out WorldFileData? sourceWorld);
        Assert.True(sourceDiagnostic.IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preservedBefore));
        Assert.NotNull(preservedBefore);

        var savedTile = new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        };
        source.Tiles.Set(1, 2, in savedTile);
        WorldTileSaveImage tileImage = CaptureImage(source.Tiles);
        WorldChest[] savedChests =
        [
            new WorldChest(
                0,
                0,
                0,
                "saved",
                [new WorldChestItem(1, 1, 0)])
        ];

        using var rewrittenStream = new MemoryStream();
        Assert.Equal(
            WorldFileTileChestRewriteResult.Rewritten,
            WorldFileTileChestRewriter.TryRewrite(
                source.Envelope,
                source.Header,
                preservedBefore!,
                tileImage,
                savedChests,
                rewrittenStream,
                out long bytesWritten));
        Assert.Equal(rewrittenStream.Length, bytesWritten);

        byte[] rewrittenFile = rewrittenStream.ToArray();
        WorldFileLoadDiagnostic rewrittenDiagnostic = WorldFileLoader.TryLoad(
            rewrittenFile,
            limits,
            out WorldFileData? rewrittenWorld);
        Assert.True(rewrittenDiagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(rewrittenWorld);

        Assert.True(loaded.Tiles.Get(1, 2).IsActive);
        Assert.Equal((ushort)1, loaded.Tiles.Get(1, 2).Type);
        WorldChest chest = Assert.Single(loaded.Chests);
        Assert.Equal(0, chest.SlotId);
        Assert.Equal(0, chest.X);
        Assert.Equal(0, chest.Y);
        Assert.Equal("saved", chest.Name);
        Assert.Equal(new WorldChestItem(1, 1, 0), Assert.Single(chest.Items));

        Assert.Equal(source.Envelope.Revision, loaded.Envelope.Revision);
        Assert.Equal(source.Envelope.FavoriteFlags, loaded.Envelope.FavoriteFlags);
        Assert.Equal(source.Envelope.FrameImportanceCount, loaded.Envelope.FrameImportanceCount);
        Assert.Equal(
            source.Envelope.FrameImportanceBits.ToArray(),
            loaded.Envelope.FrameImportanceBits.ToArray());

        Assert.True(WorldFilePreservedSections.TryCapture(
            rewrittenFile,
            loaded.Envelope,
            out WorldFilePreservedSections? preservedAfter));
        Assert.NotNull(preservedAfter);
        AssertPreservedEqual(preservedBefore!, preservedAfter!);
    }

    [Fact]
    public void Rejects_nonempty_destination_without_mutating_it()
    {
        byte[] sourceFile = CreateCompleteCurrentWorld();
        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
            sourceFile,
            CreateLimits(),
            out WorldFileData? world);
        Assert.True(diagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(world);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            loaded.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        using var destination = new MemoryStream([0xCA, 0xFE], writable: true);
        destination.Position = 0;
        Assert.Equal(
            WorldFileTileChestRewriteResult.DestinationNotEmpty,
            WorldFileTileChestRewriter.TryRewrite(
                loaded.Envelope,
                loaded.Header,
                preserved!,
                CaptureImage(loaded.Tiles),
                loaded.Chests,
                destination,
                out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal([0xCA, 0xFE], destination.ToArray());
    }

    private static WorldTileSaveImage CaptureImage(WorldTileStore tiles)
    {
        var shadow = new IncrementalWorldTileSaveShadow(tiles.Dimensions);
        for (int index = 0; index < tiles.Dimensions.SectionCount; index++)
        {
            WorldSectionId section = TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, index);
            Assert.True(tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot));
            Assert.NotNull(snapshot);
            Assert.True(shadow.TryApply(snapshot!));
        }

        Assert.True(shadow.TryCaptureImage(out WorldTileSaveImage? image));
        return Assert.IsType<WorldTileSaveImage>(image);
    }

    private static void AssertPreservedEqual(
        WorldFilePreservedSections expected,
        WorldFilePreservedSections actual)
    {
        Assert.Equal(expected.Header.ToArray(), actual.Header.ToArray());
        Assert.Equal(expected.Signs.ToArray(), actual.Signs.ToArray());
        Assert.Equal(expected.Npcs.ToArray(), actual.Npcs.ToArray());
        Assert.Equal(expected.TileEntities.ToArray(), actual.TileEntities.ToArray());
        Assert.Equal(expected.PressurePlates.ToArray(), actual.PressurePlates.ToArray());
        Assert.Equal(expected.TownRooms.ToArray(), actual.TownRooms.ToArray());
        Assert.Equal(expected.Bestiary.ToArray(), actual.Bestiary.ToArray());
        Assert.Equal(expected.CreativePowers.ToArray(), actual.CreativePowers.ToArray());
    }

    private static byte[] CreateCompleteCurrentWorld() =>
        (byte[])InvokeLoaderFixture("CreateCompleteCurrentWorld")!;

    private static WorldFileLoadLimits CreateLimits() =>
        (WorldFileLoadLimits)InvokeLoaderFixture("CreateLimits")!;

    private static object? InvokeLoaderFixture(string methodName)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"WorldFileLoaderTests.{methodName} fixture helper was not found.");
        return method.Invoke(null, null);
    }
}
