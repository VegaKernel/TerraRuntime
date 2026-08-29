using System.Reflection;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldSectionPacketSnapshotTests
{
    [Fact]
    public void Immutable_packet_snapshot_encodes_identically_to_compatibility_path()
    {
        WorldFileData world = LoadCompleteWorld();
        WorldSectionId section = new(0, 0);
        Assert.True(world.Tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? tileSnapshot));
        Assert.NotNull(tileSnapshot);

        WorldSectionEncodingContext context = WorldSectionEncodingContext.Capture(world);
        WorldSectionPacketSnapshotCaptureResult captureResult = WorldSectionPacketSnapshotCapture.TryCapture(
            world,
            tileSnapshot!,
            context,
            out WorldSectionPacketSnapshot? packetSnapshot);

        Assert.Equal(WorldSectionPacketSnapshotCaptureResult.Captured, captureResult);
        Assert.NotNull(packetSnapshot);
        Assert.Equal(
            WorldSectionPacketEncodeResult.Encoded,
            WorldSectionPacketEncoder.TryEncode(world, tileSnapshot!, out byte[] compatibilityFrame));
        Assert.Equal(
            WorldSectionPacketEncodeResult.Encoded,
            WorldSectionPacketEncoder.TryEncode(packetSnapshot!, out byte[] immutableFrame));
        Assert.Equal(compatibilityFrame, immutableFrame);
    }

    [Fact]
    public void Packet_snapshot_rejects_tile_snapshot_after_authoritative_revision_changes()
    {
        WorldFileData world = LoadCompleteWorld();
        WorldSectionId section = new(0, 0);
        Assert.True(world.Tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? tileSnapshot));
        Assert.NotNull(tileSnapshot);

        WorldTile tile = world.Tiles.Get(0, 0);
        tile.Flags ^= WorldTileFlags.WireRed;
        world.Tiles.Set(0, 0, tile);

        WorldSectionPacketSnapshotCaptureResult result = WorldSectionPacketSnapshotCapture.TryCapture(
            world,
            tileSnapshot!,
            WorldSectionEncodingContext.Capture(world),
            out WorldSectionPacketSnapshot? packetSnapshot);

        Assert.Equal(WorldSectionPacketSnapshotCaptureResult.StaleTileSnapshot, result);
        Assert.Null(packetSnapshot);
    }

    private static WorldFileData LoadCompleteWorld()
    {
        byte[] source = (byte[])InvokeWorldLoaderTestHelper("CreateCompleteCurrentWorld")!;
        WorldFileLoadLimits limits = (WorldFileLoadLimits)InvokeWorldLoaderTestHelper("CreateLimits")!;
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        return Assert.IsType<WorldFileData>(loaded);
    }

    private static object? InvokeWorldLoaderTestHelper(string name)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"World loader test helper '{name}' was not found.");
        return method.Invoke(null, null);
    }
}
