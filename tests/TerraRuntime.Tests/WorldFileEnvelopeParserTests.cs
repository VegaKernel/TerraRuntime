using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileEnvelopeParserTests
{
    private const int CurrentEnvelopeLength = 167;

    [Fact]
    public void Parses_exact_current_world_envelope_and_importance_bits()
    {
        int[] pointers = CurrentPointers();
        byte[] file = CreateWorldEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            pointers,
            VanillaWorldFormat326.TileTypeCount);
        file[72] = 0b_0000_0101;

        WorldFileEnvelopeParseResult result = WorldFileEnvelopeParser.TryParse(
            file,
            out WorldFileEnvelope? envelope,
            out int envelopeLength);

        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, result);
        Assert.NotNull(envelope);
        Assert.Equal(CurrentEnvelopeLength, envelopeLength);
        Assert.Equal(WorldFileFormatPolicy.CurrentVersion, envelope.FormatVersion);
        Assert.Equal(WorldFormatCompatibility.Verified, envelope.Compatibility);
        Assert.Equal(pointers, envelope.SectionOffsets);
        Assert.True(envelope.IsFrameImportant(0));
        Assert.False(envelope.IsFrameImportant(1));
        Assert.True(envelope.IsFrameImportant(2));
        Assert.False(envelope.IsFrameImportant(753));
    }

    [Fact]
    public void Future_version_can_be_structurally_read_without_becoming_save_compatible()
    {
        byte[] file = CreateWorldEnvelope(327, [48, 64, 80, 96], frameImportanceCount: 10);

        WorldFileEnvelopeParseResult result = WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out _);

        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, result);
        Assert.NotNull(envelope);
        Assert.Equal(WorldFormatCompatibility.NewerUnverified, envelope.Compatibility);
    }

    [Fact]
    public void Current_version_requires_vanilla_section_and_tile_counts()
    {
        byte[] wrongSections = CreateWorldEnvelope(326, [48, 64, 80, 96], frameImportanceCount: 10);
        Assert.Equal(
            WorldFileEnvelopeParseResult.CurrentSectionCountMismatch,
            WorldFileEnvelopeParser.TryParse(wrongSections, out _, out _));

        int[] pointers = CurrentPointers();
        byte[] wrongTileCount = CreateWorldEnvelope(326, pointers, frameImportanceCount: 10);
        Assert.Equal(
            WorldFileEnvelopeParseResult.CurrentFrameImportanceCountMismatch,
            WorldFileEnvelopeParser.TryParse(wrongTileCount, out _, out _));
    }

    [Fact]
    public void Rejects_non_monotonic_or_out_of_range_section_pointers()
    {
        int[] nonMonotonicPointers = CurrentPointers();
        nonMonotonicPointers[2] = nonMonotonicPointers[1] - 1;
        byte[] nonMonotonic = CreateWorldEnvelope(326, nonMonotonicPointers, VanillaWorldFormat326.TileTypeCount);
        Assert.Equal(
            WorldFileEnvelopeParseResult.NonMonotonicSectionPointers,
            WorldFileEnvelopeParser.TryParse(nonMonotonic, out _, out _));

        int[] outOfRangePointers = CurrentPointers();
        outOfRangePointers[^1] = 600;
        byte[] outOfRange = CreateWorldEnvelope(326, outOfRangePointers, VanillaWorldFormat326.TileTypeCount);
        Assert.Equal(
            WorldFileEnvelopeParseResult.SectionPointerOutOfRange,
            WorldFileEnvelopeParser.TryParse(outOfRange, out _, out _));
    }

    [Fact]
    public void Rejects_current_first_section_offset_that_does_not_match_envelope_end()
    {
        int[] pointers = CurrentPointers();
        pointers[0]++;
        byte[] file = CreateWorldEnvelope(326, pointers, VanillaWorldFormat326.TileTypeCount);

        Assert.Equal(
            WorldFileEnvelopeParseResult.FirstSectionOffsetMismatch,
            WorldFileEnvelopeParser.TryParse(file, out _, out _));
    }

    [Fact]
    public void Rejects_bad_magic_before_allocating_untrusted_payloads()
    {
        byte[] file = CreateWorldEnvelope(326, CurrentPointers(), VanillaWorldFormat326.TileTypeCount);
        file[4] = (byte)'x';

        Assert.Equal(
            WorldFileEnvelopeParseResult.BadMagic,
            WorldFileEnvelopeParser.TryParse(file, out _, out _));
    }

    private static int[] CurrentPointers() =>
        [CurrentEnvelopeLength, 200, 220, 240, 260, 280, 300, 320, 340, 360, 380];

    private static byte[] CreateWorldEnvelope(int formatVersion, int[] sectionOffsets, int frameImportanceCount)
    {
        var file = new byte[512];
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

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), checked((ushort)frameImportanceCount));
        return file;
    }
}
