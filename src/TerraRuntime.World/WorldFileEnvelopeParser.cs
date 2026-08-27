using System.Buffers.Binary;

namespace TerraRuntime.World;

/// <summary>
/// Parses only the small modern Terraria `.wld` file envelope. It does not interpret the evolving world
/// header or tile payload and therefore cannot by itself declare a future format safe to save.
/// </summary>
public static class WorldFileEnvelopeParser
{
    private const int FixedPrefixLength = 4 + 7 + 1 + 4 + 8 + 2;
    private const int MaximumSectionCount = 64;
    private const int MaximumFrameImportanceCount = 16_384;
    private const byte WorldFileType = 2;
    private static ReadOnlySpan<byte> Magic => "relogic"u8;

    public static WorldFileEnvelopeParseResult TryParse(
        ReadOnlySpan<byte> file,
        out WorldFileEnvelope? envelope,
        out int envelopeLength)
    {
        envelope = null;
        envelopeLength = 0;

        if (file.Length < FixedPrefixLength)
        {
            return WorldFileEnvelopeParseResult.Truncated;
        }

        int offset = 0;
        int formatVersion = BinaryPrimitives.ReadInt32LittleEndian(file[offset..]);
        offset += sizeof(int);
        if (formatVersion <= 0)
        {
            return WorldFileEnvelopeParseResult.InvalidVersion;
        }

        if (!file.Slice(offset, Magic.Length).SequenceEqual(Magic))
        {
            return WorldFileEnvelopeParseResult.BadMagic;
        }

        offset += Magic.Length;
        if (file[offset++] != WorldFileType)
        {
            return WorldFileEnvelopeParseResult.NotWorldFile;
        }

        uint revision = BinaryPrimitives.ReadUInt32LittleEndian(file[offset..]);
        offset += sizeof(uint);
        ulong favoriteFlags = BinaryPrimitives.ReadUInt64LittleEndian(file[offset..]);
        offset += sizeof(ulong);

        short sectionCountValue = BinaryPrimitives.ReadInt16LittleEndian(file[offset..]);
        offset += sizeof(short);
        if (sectionCountValue < 4 || sectionCountValue > MaximumSectionCount)
        {
            return WorldFileEnvelopeParseResult.InvalidSectionCount;
        }

        if (formatVersion == WorldFileFormatPolicy.CurrentVersion && sectionCountValue != VanillaWorldFormat326.SectionCount)
        {
            return WorldFileEnvelopeParseResult.CurrentSectionCountMismatch;
        }

        int sectionCount = sectionCountValue;
        int pointerBytes = checked(sectionCount * sizeof(int));
        if (file.Length - offset < pointerBytes + sizeof(ushort))
        {
            return WorldFileEnvelopeParseResult.Truncated;
        }

        var sectionOffsets = new int[sectionCount];
        int previousPointer = -1;
        for (int i = 0; i < sectionCount; i++)
        {
            int pointer = BinaryPrimitives.ReadInt32LittleEndian(file[offset..]);
            offset += sizeof(int);
            if (pointer < 0 || pointer > file.Length)
            {
                return WorldFileEnvelopeParseResult.SectionPointerOutOfRange;
            }

            if (i != 0 && pointer <= previousPointer)
            {
                return WorldFileEnvelopeParseResult.NonMonotonicSectionPointers;
            }

            sectionOffsets[i] = pointer;
            previousPointer = pointer;
        }

        int frameImportanceCount = BinaryPrimitives.ReadUInt16LittleEndian(file[offset..]);
        offset += sizeof(ushort);
        if (frameImportanceCount > MaximumFrameImportanceCount)
        {
            return WorldFileEnvelopeParseResult.FrameImportanceTooLarge;
        }

        if (formatVersion == WorldFileFormatPolicy.CurrentVersion && frameImportanceCount != VanillaWorldFormat326.TileTypeCount)
        {
            return WorldFileEnvelopeParseResult.CurrentFrameImportanceCountMismatch;
        }

        int importanceBytes = (frameImportanceCount + 7) >> 3;
        if (file.Length - offset < importanceBytes)
        {
            return WorldFileEnvelopeParseResult.Truncated;
        }

        byte[] frameImportanceBits = file.Slice(offset, importanceBytes).ToArray();
        offset += importanceBytes;
        if (sectionOffsets[0] < offset)
        {
            return WorldFileEnvelopeParseResult.FirstSectionOverlapsEnvelope;
        }

        if (formatVersion == WorldFileFormatPolicy.CurrentVersion && sectionOffsets[0] != offset)
        {
            return WorldFileEnvelopeParseResult.FirstSectionOffsetMismatch;
        }

        envelopeLength = offset;
        envelope = new WorldFileEnvelope(
            formatVersion,
            revision,
            favoriteFlags,
            sectionOffsets,
            frameImportanceCount,
            frameImportanceBits);
        return WorldFileEnvelopeParseResult.Parsed;
    }
}
