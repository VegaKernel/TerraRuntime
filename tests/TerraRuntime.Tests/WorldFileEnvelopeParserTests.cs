using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileEnvelopeParserTests
{
    [Fact]
    public void Parses_bounded_modern_world_envelope_and_importance_bits()
    {
        byte[] file = CreateWorldEnvelope(formatVersion: 325, sectionOffsets: [48, 64, 80, 96]);
        file[44] = 0b_0000_0101;

        WorldFileEnvelopeParseResult result = WorldFileEnvelopeParser.TryParse(
            file,
            out WorldFileEnvelope? envelope,
            out int envelopeLength);

        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, result);
        Assert.NotNull(envelope);
        Assert.Equal(46, envelopeLength);
        Assert.Equal(325, envelope.FormatVersion);
        Assert.Equal(WorldFormatCompatibility.Verified, envelope.Compatibility);
        Assert.Equal(new[] { 48, 64, 80, 96 }, envelope.SectionOffsets);
        Assert.True(envelope.IsFrameImportant(0));
        Assert.False(envelope.IsFrameImportant(1));
        Assert.True(envelope.IsFrameImportant(2));
        Assert.False(envelope.IsFrameImportant(10));
    }

    [Fact]
    public void Future_version_can_be_structurally_read_without_becoming_save_compatible()
    {
        byte[] file = CreateWorldEnvelope(formatVersion: 326, sectionOffsets: [48, 64, 80, 96]);

        WorldFileEnvelopeParseResult result = WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out _);

        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, result);
        Assert.NotNull(envelope);
        Assert.Equal(WorldFormatCompatibility.NewerUnverified, envelope.Compatibility);
    }

    [Fact]
    public void Rejects_non_monotonic_or_out_of_range_section_pointers()
    {
        byte[] nonMonotonic = CreateWorldEnvelope(formatVersion: 325, sectionOffsets: [48, 80, 64, 96]);
        Assert.Equal(
            WorldFileEnvelopeParseResult.NonMonotonicSectionPointers,
            WorldFileEnvelopeParser.TryParse(nonMonotonic, out _, out _));

        byte[] outOfRange = CreateWorldEnvelope(formatVersion: 325, sectionOffsets: [48, 64, 80, 200]);
        Assert.Equal(
            WorldFileEnvelopeParseResult.SectionPointerOutOfRange,
            WorldFileEnvelopeParser.TryParse(outOfRange, out _, out _));
    }

    [Fact]
    public void Rejects_bad_magic_before_allocating_untrusted_payloads()
    {
        byte[] file = CreateWorldEnvelope(formatVersion: 325, sectionOffsets: [48, 64, 80, 96]);
        file[4] = (byte)'x';

        Assert.Equal(
            WorldFileEnvelopeParseResult.BadMagic,
            WorldFileEnvelopeParser.TryParse(file, out _, out _));
    }

    private static byte[] CreateWorldEnvelope(int formatVersion, int[] sectionOffsets)
    {
        var file = new byte[128];
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), formatVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 7);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), checked((short)sectionOffsets.Length));
        offset += sizeof(short);
        foreach (int pointer in sectionOffsets)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), 10);
        offset += sizeof(ushort);
        file[offset] = 0;
        file[offset + 1] = 0;
        return file;
    }
}
