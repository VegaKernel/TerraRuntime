using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTileDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int TileStart = 200;

    [Fact]
    public void Decodes_current_tile_headers_payload_and_vertical_rle()
    {
        byte[] tileBytes =
        [
            0x6F, 0x3F, 0xFF, 0x1E,
            0x2C, 0x01,
            0x12, 0x00, 0x24, 0x00,
            0x05,
            0x2C, 0x06,
            0xC8,
            0x01,
            0x01,
            0x02, 0x90, 0x0A, 0x00, 0x63, 0x00,
            0x00
        ];

        byte[] file = CreateCurrentFile(tileBytes, importantTypes: [300, VanillaWorldFormat326.TimersTileType]);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        var dimensions = new WorldDimensions(1, 4);
        var header = CreateHeader(dimensions);
        var store = new WorldTileStore(dimensions);

        WorldFileTileDecodeResult result = WorldFileTileDecoder.TryDecode(file, envelope, header, store, out int consumed);

        Assert.Equal(WorldFileTileDecodeResult.Decoded, result);
        Assert.Equal(tileBytes.Length, consumed);

        WorldTile first = store.Get(0, 0);
        Assert.Equal((ushort)300, first.Type);
        Assert.Equal((ushort)300, first.Wall);
        Assert.Equal((short)18, first.FrameX);
        Assert.Equal((short)36, first.FrameY);
        Assert.Equal((byte)5, first.TileColor);
        Assert.Equal((byte)6, first.WallColor);
        Assert.Equal((byte)200, first.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Shimmer, first.LiquidKind);
        Assert.Equal((byte)3, first.Shape);
        Assert.True(first.IsActive);
        Assert.Equal(
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
            first.Flags);

        Assert.Equal(first.Type, store.Get(0, 1).Type);
        Assert.Equal(first.Flags, store.Get(0, 1).Flags);
        Assert.Equal(first.LiquidAmount, store.Get(0, 1).LiquidAmount);

        WorldTile timer = store.Get(0, 2);
        Assert.Equal(VanillaWorldFormat326.TimersTileType, timer.Type);
        Assert.Equal((short)10, timer.FrameX);
        Assert.Equal((short)0, timer.FrameY);

        Assert.False(store.Get(0, 3).IsActive);
    }

    [Fact]
    public void Rejects_rle_that_crosses_the_current_column()
    {
        byte[] file = CreateCurrentFile([0x42, 0x01, 0x05]);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        var dimensions = new WorldDimensions(1, 2);
        var store = new WorldTileStore(dimensions);

        Assert.Equal(
            WorldFileTileDecodeResult.InvalidRunLength,
            WorldFileTileDecoder.TryDecode(file, envelope, CreateHeader(dimensions), store, out _));
    }

    [Fact]
    public void Rejects_unknown_current_tile_type_before_using_importance_table()
    {
        byte[] file = CreateCurrentFile([0x22, 0x20, 0x03]);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        var dimensions = new WorldDimensions(1, 1);
        var store = new WorldTileStore(dimensions);

        Assert.Equal(
            WorldFileTileDecodeResult.InvalidTileType,
            WorldFileTileDecoder.TryDecode(file, envelope, CreateHeader(dimensions), store, out _));
    }

    [Fact]
    public void Requires_tile_decoder_to_end_exactly_at_next_section_pointer()
    {
        byte[] file = CreateCurrentFile([0x00, 0x00]);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        var dimensions = new WorldDimensions(1, 1);
        var store = new WorldTileStore(dimensions);

        Assert.Equal(
            WorldFileTileDecodeResult.SectionLengthMismatch,
            WorldFileTileDecoder.TryDecode(file, envelope, CreateHeader(dimensions), store, out int consumed));
        Assert.Equal(1, consumed);
    }

    private static WorldFileHeader CreateHeader(WorldDimensions dimensions) =>
        new(
            "test",
            "seed",
            1,
            Guid.Empty,
            1,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out int envelopeLength));
        Assert.Equal(EnvelopeEnd, envelopeLength);
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] tileBytes, ushort[]? importantTypes = null)
    {
        var file = new byte[512];
        int tileEnd = TileStart + tileBytes.Length;
        int next = Math.Max(tileEnd + 8, 240);
        int[] pointers =
        [
            EnvelopeEnd,
            TileStart,
            tileEnd,
            next,
            next + 20,
            next + 40,
            next + 60,
            next + 80,
            next + 100,
            next + 120,
            next + 140
        ];
        Assert.True(pointers[^1] < file.Length);

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
        foreach (int pointer in pointers)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.TileTypeCount);
        offset += sizeof(ushort);
        int importanceStart = offset;
        offset += (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        Assert.Equal(EnvelopeEnd, offset);

        if (importantTypes is not null)
        {
            foreach (ushort tileType in importantTypes)
            {
                file[importanceStart + (tileType >> 3)] |= (byte)(1 << (tileType & 7));
            }
        }

        tileBytes.CopyTo(file, TileStart);
        return file;
    }
}
