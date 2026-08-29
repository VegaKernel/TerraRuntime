using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileDetachedTileEncoderTests
{
    [Fact]
    public void Detached_save_image_encodes_identical_bytes_to_live_store_across_section_boundaries()
    {
        var dimensions = new WorldDimensions(201, 151);
        var live = new WorldTileStore(dimensions);
        byte[] frameImportance = CreateFrameImportance(5);

        var framed = new WorldTile
        {
            Type = 5,
            Wall = 300,
            FrameX = 36,
            FrameY = 54,
            Flags =
                WorldTileFlags.Active |
                WorldTileFlags.WireRed |
                WorldTileFlags.WireYellow |
                WorldTileFlags.Actuator |
                WorldTileFlags.InvisibleBlock,
            LiquidAmount = 120,
            LiquidKind = WorldLiquidKind.Shimmer,
            TileColor = 7,
            WallColor = 8,
            Shape = 3
        };
        var ordinary = new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue,
            LiquidAmount = 42,
            LiquidKind = WorldLiquidKind.Honey
        };

        live.Set(0, 0, in framed);
        live.Set(199, 149, in ordinary);
        live.Set(200, 0, in framed);
        live.Set(200, 150, in ordinary);

        WorldTileSaveImage image = CaptureImage(live);

        using var livePayload = new MemoryStream();
        using var detachedPayload = new MemoryStream();
        WorldFileTileEncodeResult liveResult = WorldFileTileEncoder.TryEncode(
            live,
            VanillaWorldFormat326.TileTypeCount,
            frameImportance,
            livePayload,
            out long liveBytes);
        WorldFileTileEncodeResult detachedResult = WorldFileTileEncoder.TryEncode(
            image,
            VanillaWorldFormat326.TileTypeCount,
            frameImportance,
            detachedPayload,
            out long detachedBytes);

        Assert.Equal(WorldFileTileEncodeResult.Encoded, liveResult);
        Assert.Equal(WorldFileTileEncodeResult.Encoded, detachedResult);
        Assert.Equal(liveBytes, detachedBytes);
        Assert.Equal(livePayload.ToArray(), detachedPayload.ToArray());
    }

    [Fact]
    public void Detached_save_image_keeps_old_encoding_after_live_world_mutates()
    {
        var dimensions = new WorldDimensions(200, 150);
        var live = new WorldTileStore(dimensions);
        byte[] frameImportance = CreateFrameImportance();
        var original = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        live.Set(5, 6, in original);
        WorldTileSaveImage before = CaptureImage(live);

        var updated = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        live.Set(5, 6, in updated);

        using var detachedPayload = new MemoryStream();
        using var mutatedLivePayload = new MemoryStream();
        Assert.Equal(
            WorldFileTileEncodeResult.Encoded,
            WorldFileTileEncoder.TryEncode(
                before,
                VanillaWorldFormat326.TileTypeCount,
                frameImportance,
                detachedPayload,
                out long detachedBytes));
        Assert.Equal(
            WorldFileTileEncodeResult.Encoded,
            WorldFileTileEncoder.TryEncode(
                live,
                VanillaWorldFormat326.TileTypeCount,
                frameImportance,
                mutatedLivePayload,
                out long liveBytes));

        Assert.Equal(detachedPayload.Length, detachedBytes);
        Assert.Equal(mutatedLivePayload.Length, liveBytes);
        Assert.NotEqual(detachedPayload.ToArray(), mutatedLivePayload.ToArray());
        Assert.Equal((ushort)1, before.Get(5, 6).Type);
        Assert.Equal((ushort)2, live.Get(5, 6).Type);
    }

    private static WorldTileSaveImage CaptureImage(WorldTileStore live)
    {
        var shadow = new IncrementalWorldTileSaveShadow(live.Dimensions);
        for (int index = 0; index < live.Dimensions.SectionCount; index++)
        {
            WorldSectionId section = TerrariaSectionGeometry.FromLinearIndex(live.Dimensions, index);
            Assert.True(live.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot));
            Assert.NotNull(snapshot);
            Assert.True(shadow.TryApply(snapshot!));
        }

        Assert.True(shadow.TryCaptureImage(out WorldTileSaveImage? image));
        return Assert.IsType<WorldTileSaveImage>(image);
    }

    private static byte[] CreateFrameImportance(params int[] framedTileTypes)
    {
        var bits = new byte[(VanillaWorldFormat326.TileTypeCount + 7) / 8];
        foreach (int tileType in framedTileTypes)
            bits[tileType >> 3] |= (byte)(1 << (tileType & 7));
        return bits;
    }
}
