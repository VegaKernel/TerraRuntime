using System.Buffers.Binary;

namespace TerraRuntime.World;

public static class WorldFilePressurePlateDecoder
{
    public static WorldFilePressurePlateDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        int maxPressurePlates,
        out WorldPressurePlate[] pressurePlates,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPressurePlates);

        pressurePlates = [];
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFilePressurePlateDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 8)
            return WorldFilePressurePlateDecodeResult.InvalidSectionBounds;

        int sectionStart = envelope.SectionOffsets[6];
        int sectionEnd = envelope.SectionOffsets[7];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
            return WorldFilePressurePlateDecodeResult.InvalidSectionBounds;

        ReadOnlySpan<byte> section = file.Slice(sectionStart, sectionEnd - sectionStart);
        if (section.Length < sizeof(int))
            return WorldFilePressurePlateDecodeResult.Truncated;

        int count = BinaryPrimitives.ReadInt32LittleEndian(section);
        int offset = sizeof(int);
        if (count < 0)
            return WorldFilePressurePlateDecodeResult.InvalidCount;
        if (count > maxPressurePlates)
            return WorldFilePressurePlateDecodeResult.CountBudgetExceeded;

        long requiredBytes = (long)count * (sizeof(int) * 2);
        if (section.Length - offset < requiredBytes)
        {
            bytesConsumed = offset;
            return WorldFilePressurePlateDecodeResult.Truncated;
        }

        var result = new WorldPressurePlate[count];
        var seen = new HashSet<long>();
        for (int i = 0; i < count; i++)
        {
            int x = BinaryPrimitives.ReadInt32LittleEndian(section[offset..]);
            offset += sizeof(int);
            int y = BinaryPrimitives.ReadInt32LittleEndian(section[offset..]);
            offset += sizeof(int);

            if ((uint)x >= (uint)header.Dimensions.WidthTiles || (uint)y >= (uint)header.Dimensions.HeightTiles)
            {
                bytesConsumed = offset;
                return WorldFilePressurePlateDecodeResult.InvalidCoordinates;
            }

            long key = ((long)(uint)x << 32) | (uint)y;
            if (!seen.Add(key))
            {
                bytesConsumed = offset;
                return WorldFilePressurePlateDecodeResult.DuplicateCoordinates;
            }

            result[i] = new WorldPressurePlate(x, y);
        }

        bytesConsumed = offset;
        if (offset != section.Length)
            return WorldFilePressurePlateDecodeResult.SectionLengthMismatch;

        pressurePlates = result;
        return WorldFilePressurePlateDecodeResult.Decoded;
    }
}
