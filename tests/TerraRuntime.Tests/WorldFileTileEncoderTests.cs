using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTileEncoderTests
{
    [Fact]
    public void Encode_roundtrips_current_format_tile_state_and_long_rle()
    {
        var dimensions = new WorldDimensions(2, 300);
        var source = new WorldTileStore(dimensions);
        byte[] frameImportance = CreateFrameImportance(5);
        var repeated = new WorldTile
        {
            Type = 5,
            Wall = 300,
            FrameX = 36,
            FrameY = 54,
            Flags =
                WorldTileFlags.Active |
                WorldTileFlags.WireRed |
                WorldTileFlags.WireBlue |
                WorldTileFlags.WireGreen |
                WorldTileFlags.WireYellow |
                WorldTileFlags.Actuator |
                WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock |
                WorldTileFlags.InvisibleWall |
                WorldTileFlags.FullbrightBlock |
                WorldTileFlags.FullbrightWall,
            LiquidAmount = 171,
            LiquidKind = WorldLiquidKind.Shimmer,
            TileColor = 7,
            WallColor = 8,
            Shape = 3
        };

        for (int y = 0; y < dimensions.HeightTiles; y++)
            source.Set(0, y, in repeated);

        using var payload = new MemoryStream();
        WorldFileTileEncodeResult encodeResult = WorldFileTileEncoder.TryEncode(
            source,
            VanillaWorldFormat326.TileTypeCount,
            frameImportance,
            payload,
            out long bytesWritten);

        Assert.Equal(WorldFileTileEncodeResult.Encoded, encodeResult);
        Assert.Equal(payload.Length, bytesWritten);
        Assert.True(bytesWritten < 64, $"Expected both 300-tile columns to use RLE, encoded {bytesWritten} bytes.");

        byte[] file = BuildSyntheticWorld(payload.ToArray(), frameImportance, out WorldFileEnvelope envelope);
        var destination = new WorldTileStore(dimensions);
        var header = new WorldFileHeader(
            "Roundtrip",
            "1",
            0,
            Guid.Empty,
            1,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);

        WorldFileTileDecodeResult decodeResult = WorldFileTileDecoder.TryDecode(
            file,
            envelope,
            header,
            destination,
            out int bytesConsumed);

        Assert.Equal(WorldFileTileDecodeResult.Decoded, decodeResult);
        Assert.Equal(payload.Length, bytesConsumed);
        for (int y = 0; y < dimensions.HeightTiles; y++)
            AssertTileEqual(repeated, destination.Get(0, y));

        for (int y = 0; y < dimensions.HeightTiles; y++)
            AssertTileEqual(default, destination.Get(1, y));
    }

    [Fact]
    public void Encode_does_not_batch_vanilla_save_compression_exceptions()
    {
        var dimensions = new WorldDimensions(1, 2);
        var source = new WorldTileStore(dimensions);
        byte[] frameImportance = CreateFrameImportance();
        var tile = new WorldTile
        {
            Type = 423,
            FrameX = -1,
            FrameY = -1,
            Flags = WorldTileFlags.Active
        };
        source.Set(0, 0, in tile);
        source.Set(0, 1, in tile);

        using var payload = new MemoryStream();
        WorldFileTileEncodeResult result = WorldFileTileEncoder.TryEncode(
            source,
            VanillaWorldFormat326.TileTypeCount,
            frameImportance,
            payload,
            out long bytesWritten);

        Assert.Equal(WorldFileTileEncodeResult.Encoded, result);
        Assert.Equal(6, bytesWritten);
    }

    [Fact]
    public void Encode_rejects_invalid_current_format_content_ids()
    {
        var dimensions = new WorldDimensions(1, 1);
        var source = new WorldTileStore(dimensions);
        byte[] frameImportance = CreateFrameImportance();
        var invalid = new WorldTile
        {
            Type = 0,
            Wall = VanillaWorldFormat326.WallTypeCount,
            Flags = WorldTileFlags.Active
        };
        source.Set(0, 0, in invalid);

        using var payload = new MemoryStream();
        WorldFileTileEncodeResult result = WorldFileTileEncoder.TryEncode(
            source,
            VanillaWorldFormat326.TileTypeCount,
            frameImportance,
            payload,
            out long bytesWritten);

        Assert.Equal(WorldFileTileEncodeResult.InvalidWallType, result);
        Assert.Equal(0, bytesWritten);
    }

    private static byte[] CreateFrameImportance(params int[] framedTileTypes)
    {
        var bits = new byte[(VanillaWorldFormat326.TileTypeCount + 7) / 8];
        foreach (int tileType in framedTileTypes)
            bits[tileType >> 3] |= (byte)(1 << (tileType & 7));
        return bits;
    }

    private static byte[] BuildSyntheticWorld(
        byte[] tilePayload,
        byte[] frameImportance,
        out WorldFileEnvelope envelope)
    {
        const int envelopeLength = 4 + 7 + 1 + 4 + 8 + 2 +
                                   (VanillaWorldFormat326.SectionCount * 4) + 2 +
                                   ((VanillaWorldFormat326.TileTypeCount + 7) / 8);
        int tileStart = envelopeLength + 1;
        int tileEnd = tileStart + tilePayload.Length;
        var sectionOffsets = new int[VanillaWorldFormat326.SectionCount];
        sectionOffsets[0] = envelopeLength;
        sectionOffsets[1] = tileStart;
        sectionOffsets[2] = tileEnd;
        for (int index = 3; index < sectionOffsets.Length; index++)
            sectionOffsets[index] = tileEnd + index - 2;

        byte[] file = new byte[sectionOffsets[^1]];
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), WorldFileFormatPolicy.CurrentVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 1);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.SectionCount);
        offset += sizeof(short);
        foreach (int sectionOffset in sectionOffsets)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), sectionOffset);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.TileTypeCount);
        offset += sizeof(ushort);
        frameImportance.CopyTo(file.AsSpan(offset));
        offset += frameImportance.Length;
        Assert.Equal(envelopeLength, offset);
        tilePayload.CopyTo(file.AsSpan(tileStart));

        WorldFileEnvelopeParseResult parseResult = WorldFileEnvelopeParser.TryParse(
            file,
            out WorldFileEnvelope? parsed,
            out int parsedLength);
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, parseResult);
        Assert.Equal(envelopeLength, parsedLength);
        Assert.NotNull(parsed);
        envelope = parsed;
        return file;
    }

    private static void AssertTileEqual(WorldTile expected, WorldTile actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Wall, actual.Wall);
        Assert.Equal(expected.FrameX, actual.FrameX);
        Assert.Equal(expected.FrameY, actual.FrameY);
        Assert.Equal(expected.Flags, actual.Flags);
        Assert.Equal(expected.LiquidAmount, actual.LiquidAmount);
        Assert.Equal(expected.TileColor, actual.TileColor);
        Assert.Equal(expected.WallColor, actual.WallColor);
        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.LiquidKind, actual.LiquidKind);
    }
}
