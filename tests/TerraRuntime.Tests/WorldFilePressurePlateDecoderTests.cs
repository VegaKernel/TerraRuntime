using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFilePressurePlateDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int PlateStart = 240;

    [Fact]
    public void Decodes_current_weighted_pressure_plate_positions()
    {
        byte[] section = new byte[sizeof(int) + 16];
        BinaryPrimitives.WriteInt32LittleEndian(section, 2);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(8), 2);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(12), 8);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(16), 9);
        byte[] file = CreateCurrentFile(section);

        WorldFilePressurePlateDecodeResult result = WorldFilePressurePlateDecoder.TryDecode(
            file,
            ParseEnvelope(file),
            CreateHeader(),
            maxPressurePlates: 4,
            out WorldPressurePlate[] plates,
            out int consumed);

        Assert.Equal(WorldFilePressurePlateDecodeResult.Decoded, result);
        Assert.Equal(section.Length, consumed);
        Assert.Equal(new[] { new WorldPressurePlate(1, 2), new WorldPressurePlate(8, 9) }, plates);
    }

    [Fact]
    public void Rejects_count_before_allocating_unbounded_array()
    {
        byte[] section = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(section, 1000);
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFilePressurePlateDecodeResult.CountBudgetExceeded,
            WorldFilePressurePlateDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxPressurePlates: 32,
                out WorldPressurePlate[] plates,
                out _));
        Assert.Empty(plates);
    }

    [Fact]
    public void Rejects_duplicate_coordinates_instead_of_dictionary_add_failure()
    {
        byte[] section = new byte[sizeof(int) + 16];
        BinaryPrimitives.WriteInt32LittleEndian(section, 2);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(4), 3);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(12), 3);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(16), 4);
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFilePressurePlateDecodeResult.DuplicateCoordinates,
            WorldFilePressurePlateDecoder.TryDecode(file, ParseEnvelope(file), CreateHeader(), 4, out _, out _));
    }

    [Fact]
    public void Rejects_coordinates_outside_world()
    {
        byte[] section = new byte[sizeof(int) + 8];
        BinaryPrimitives.WriteInt32LittleEndian(section, 1);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(4), 10);
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(8), 0);
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFilePressurePlateDecodeResult.InvalidCoordinates,
            WorldFilePressurePlateDecoder.TryDecode(file, ParseEnvelope(file), CreateHeader(), 4, out _, out _));
    }

    [Fact]
    public void Requires_exact_end_of_pressure_plate_section()
    {
        byte[] section = new byte[sizeof(int) + 1];
        BinaryPrimitives.WriteInt32LittleEndian(section, 0);
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFilePressurePlateDecodeResult.SectionLengthMismatch,
            WorldFilePressurePlateDecoder.TryDecode(file, ParseEnvelope(file), CreateHeader(), 4, out _, out int consumed));
        Assert.Equal(sizeof(int), consumed);
    }

    private static WorldFileHeader CreateHeader()
    {
        var dimensions = new WorldDimensions(10, 10);
        return new WorldFileHeader("test", "seed", 1, Guid.Empty, 1, 0, 160, 0, 160, dimensions);
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out int envelopeLength));
        Assert.Equal(EnvelopeEnd, envelopeLength);
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] section)
    {
        int plateEnd = PlateStart + section.Length;
        int[] pointers =
        [
            EnvelopeEnd,
            180,
            190,
            200,
            210,
            220,
            PlateStart,
            plateEnd,
            plateEnd + 8,
            plateEnd + 16,
            plateEnd + 24
        ];
        var file = new byte[pointers[^1] + 1];

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
        offset += (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        Assert.Equal(EnvelopeEnd, offset);

        section.CopyTo(file, PlateStart);
        return file;
    }
}
