using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFilePreservedTileChestPatchWriterTests
{
    [Fact]
    public void Detached_template_patch_roundtrips_through_complete_world_loader()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var changedTile = new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active,
            LiquidKind = WorldLiquidKind.Water
        };
        source.Tiles.Set(0, 0, in changedTile);
        WorldTileSaveImage tileImage = CaptureImage(source.Tiles);
        WorldChest[] chests =
        [
            new WorldChest(
                0,
                0,
                0,
                "persisted",
                [new WorldChestItem(7, 1, 2), default])
        ];

        using var destination = new MemoryStream();
        Assert.Equal(
            WorldFileTileChestPatchWriteResult.Written,
            WorldFileTileChestPatchWriter.TryWrite(
                source.Envelope,
                source.Header,
                preserved!,
                tileImage,
                chests,
                destination,
                out long bytesWritten));
        Assert.Equal(destination.Length, bytesWritten);

        byte[] rewrittenFile = destination.ToArray();
        WorldFileLoadDiagnostic loadDiagnostic = WorldFileLoader.TryLoad(
            rewrittenFile,
            limits,
            out WorldFileData? loadedWorld);
        Assert.True(loadDiagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);

        WorldTile persistedTile = loaded.Tiles.Get(0, 0);
        Assert.True(persistedTile.IsActive);
        Assert.Equal((ushort)1, persistedTile.Type);
        WorldChest persistedChest = Assert.Single(loaded.Chests);
        Assert.Equal("persisted", persistedChest.Name);
        Assert.Equal(new WorldChestItem(7, 1, 2), persistedChest.Items[0]);

        Assert.Equal(source.Envelope.Revision, loaded.Envelope.Revision);
        Assert.Equal(source.Envelope.FavoriteFlags, loaded.Envelope.FavoriteFlags);
        Assert.Equal(source.Header.Name, loaded.Header.Name);
        Assert.Equal(source.RuntimeMetadata.Time, loaded.RuntimeMetadata.Time);
        Assert.Equal(source.CreativePowers.FreezeTime, loaded.CreativePowers.FreezeTime);
        AssertPreservedSectionsEqual(sourceFile, source.Envelope, rewrittenFile, loaded.Envelope);
    }

    [Fact]
    public void Detached_template_does_not_retain_source_tile_or_chest_payloads()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        long sourceTileBytes = source.Envelope.SectionOffsets[2] - source.Envelope.SectionOffsets[1];
        long sourceChestBytes = source.Envelope.SectionOffsets[3] - source.Envelope.SectionOffsets[2];
        long retainedWithoutTileAndChest =
            (long)sourceFile.Length -
            WorldFileEnvelopeEncoder.CurrentEncodedLength -
            sourceTileBytes -
            sourceChestBytes -
            (sourceFile.Length - source.Envelope.SectionOffsets[10]);

        Assert.Equal(retainedWithoutTileAndChest, preserved!.TotalBytes);
    }

    private static void AssertPreservedSectionsEqual(
        byte[] source,
        WorldFileEnvelope sourceEnvelope,
        byte[] rewritten,
        WorldFileEnvelope rewrittenEnvelope)
    {
        int[] preservedIndices = [0, 3, 4, 5, 6, 7, 8, 9];
        foreach (int index in preservedIndices)
        {
            ReadOnlySpan<byte> expected = source.AsSpan(
                sourceEnvelope.SectionOffsets[index],
                sourceEnvelope.SectionOffsets[index + 1] - sourceEnvelope.SectionOffsets[index]);
            ReadOnlySpan<byte> actual = rewritten.AsSpan(
                rewrittenEnvelope.SectionOffsets[index],
                rewrittenEnvelope.SectionOffsets[index + 1] - rewrittenEnvelope.SectionOffsets[index]);
            Assert.True(expected.SequenceEqual(actual), $"Preserved section {index} changed during patch write.");
        }
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

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
