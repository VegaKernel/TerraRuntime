using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

public enum WorldFileClockHeaderPatchResult : byte
{
    Patched = 0,
    InvalidHeader = 1,
    InvalidClockState = 2
}

/// <summary>
/// Patches only the runtime clock fields inside an already validated Terraria 1.4.5.8 header section.
/// Every other byte is preserved verbatim, including SaveWorldFlags state that TerraRuntime does not yet model.
/// The targeted fields all precede variable-length angler/banner/party/manifest data, so their offsets depend only
/// on the two validated leading strings and the fixed current-version prefix.
/// </summary>
public static class WorldFileClockHeaderPatcher
{
    private const int MaximumStringBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileClockHeaderPatchResult TryPatch(
        ReadOnlySpan<byte> sourceHeader,
        WorldFileHeader header,
        double time,
        bool dayTime,
        byte moonPhase,
        double slimeRainTime,
        out byte[] patchedHeader)
    {
        ArgumentNullException.ThrowIfNull(header);
        patchedHeader = [];

        if (!double.IsFinite(time) ||
            time < 0d ||
            time > int.MaxValue ||
            time != Math.Truncate(time) ||
            moonPhase >= 8 ||
            !double.IsFinite(slimeRainTime))
        {
            return WorldFileClockHeaderPatchResult.InvalidClockState;
        }

        var reader = new HeaderPrefixReader(sourceHeader);
        if (!reader.TryReadString(out string name) ||
            !reader.TryReadString(out string seed) ||
            !reader.TryReadUInt64(out ulong generatorVersion) ||
            !reader.TryReadGuid(out Guid uniqueId) ||
            !reader.TryReadInt32(out int worldId) ||
            !reader.TryReadInt32(out int leftWorld) ||
            !reader.TryReadInt32(out int rightWorld) ||
            !reader.TryReadInt32(out int topWorld) ||
            !reader.TryReadInt32(out int bottomWorld) ||
            !reader.TryReadInt32(out int heightTiles) ||
            !reader.TryReadInt32(out int widthTiles))
        {
            return WorldFileClockHeaderPatchResult.InvalidHeader;
        }

        if (!string.Equals(name, header.Name, StringComparison.Ordinal) ||
            !string.Equals(seed, header.SeedText, StringComparison.Ordinal) ||
            generatorVersion != header.WorldGeneratorVersion ||
            uniqueId != header.UniqueId ||
            worldId != header.WorldId ||
            leftWorld != header.LeftWorld ||
            rightWorld != header.RightWorld ||
            topWorld != header.TopWorld ||
            bottomWorld != header.BottomWorld ||
            heightTiles != header.Dimensions.HeightTiles ||
            widthTiles != header.Dimensions.WidthTiles)
        {
            return WorldFileClockHeaderPatchResult.InvalidHeader;
        }

        // gameMode, 9 seed flags, creation/last-played ticks, moon type,
        // tree/cave background tables, spawn, world-surface and rock-layer.
        const int bytesBeforeTime =
            sizeof(int) +
            9 +
            (sizeof(long) * 2) +
            sizeof(byte) +
            (sizeof(int) * 3) +
            (sizeof(int) * 4) +
            (sizeof(int) * 3) +
            (sizeof(int) * 4) +
            (sizeof(int) * 3) +
            (sizeof(int) * 2) +
            (sizeof(double) * 2);
        if (!reader.TrySkip(bytesBeforeTime))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        int timeOffset = reader.Offset;
        if (!reader.TrySkip(sizeof(double)))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        int dayTimeOffset = reader.Offset;
        if (!reader.TrySkip(sizeof(byte)))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        int moonPhaseOffset = reader.Offset;
        if (!reader.TrySkip(sizeof(int)))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        // bloodMoon, eclipse, dungeon X/Y.
        if (!reader.TrySkip(2 + (sizeof(int) * 2)))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        // 21 progression booleans, shadow-orb count, altar count, hardmode/party-of-doom,
        // invasion delay/size/type and invasion X. SlimeRainTime follows immediately.
        const int bytesBeforeSlimeRain =
            21 +
            sizeof(byte) +
            sizeof(int) +
            2 +
            (sizeof(int) * 3) +
            sizeof(double);
        if (!reader.TrySkip(bytesBeforeSlimeRain))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        int slimeRainOffset = reader.Offset;
        if (!reader.TrySkip(sizeof(double)))
            return WorldFileClockHeaderPatchResult.InvalidHeader;

        patchedHeader = sourceHeader.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(
            patchedHeader.AsSpan(timeOffset, sizeof(long)),
            BitConverter.DoubleToInt64Bits(time));
        patchedHeader[dayTimeOffset] = dayTime ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(
            patchedHeader.AsSpan(moonPhaseOffset, sizeof(int)),
            moonPhase);
        BinaryPrimitives.WriteInt64LittleEndian(
            patchedHeader.AsSpan(slimeRainOffset, sizeof(long)),
            BitConverter.DoubleToInt64Bits(slimeRainTime));
        return WorldFileClockHeaderPatchResult.Patched;
    }

    private ref struct HeaderPrefixReader
    {
        private readonly ReadOnlySpan<byte> data;
        private int offset;

        public HeaderPrefixReader(ReadOnlySpan<byte> data)
        {
            this.data = data;
            offset = 0;
        }

        public int Offset => offset;

        public bool TrySkip(int length)
        {
            if (length < 0 || data.Length - offset < length)
                return false;
            offset += length;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (data.Length - offset < sizeof(int))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            offset += sizeof(int);
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            if (data.Length - offset < sizeof(ulong))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
            offset += sizeof(ulong);
            return true;
        }

        public bool TryReadGuid(out Guid value)
        {
            if (data.Length - offset < 16)
            {
                value = default;
                return false;
            }

            value = new Guid(data.Slice(offset, 16));
            offset += 16;
            return true;
        }

        public bool TryReadString(out string value)
        {
            value = string.Empty;
            if (!TryRead7BitEncodedInt(out int length) ||
                length < 0 ||
                length > MaximumStringBytes ||
                data.Length - offset < length)
            {
                return false;
            }

            try
            {
                value = StrictUtf8.GetString(data.Slice(offset, length));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            offset += length;
            return true;
        }

        private bool TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (offset >= data.Length)
                {
                    value = default;
                    return false;
                }

                byte current = data[offset++];
                if (shift == 28 && (current & 0xF0) != 0)
                {
                    value = default;
                    return false;
                }

                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) != 0)
                    continue;

                if (result > int.MaxValue)
                {
                    value = default;
                    return false;
                }

                value = (int)result;
                return true;
            }

            value = default;
            return false;
        }
    }
}
