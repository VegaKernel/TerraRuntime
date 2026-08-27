using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

public readonly record struct WorldBestiaryKill(string PersistentId, int KillCount);

public sealed record WorldBestiaryData(
    WorldBestiaryKill[] Kills,
    string[] Sightings,
    string[] Chats);

public readonly record struct WorldFileBestiaryLimits(
    int MaxKillEntries,
    int MaxSightEntries,
    int MaxChatEntries,
    int MaxPersistentIdBytes,
    long MaxTotalPersistentIdBytes)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxKillEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxSightEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxChatEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxPersistentIdBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalPersistentIdBytes);
    }
}

public enum WorldFileBestiaryDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    Truncated = 3,
    InvalidEntryCount = 4,
    EntryBudgetExceeded = 5,
    InvalidStringLength = 6,
    StringTooLarge = 7,
    TotalStringBudgetExceeded = 8,
    InvalidUtf8 = 9,
    InvalidKillCount = 10,
    SectionLengthMismatch = 11
}

public static class WorldFileBestiaryDecoder
{
    private const int MaximumKillCount = 999_999_999;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileBestiaryDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileBestiaryLimits limits,
        out WorldBestiaryData? bestiary,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        limits.Validate();
        bestiary = null;
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileBestiaryDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 10)
            return WorldFileBestiaryDecodeResult.InvalidSectionBounds;

        int start = envelope.SectionOffsets[8];
        int end = envelope.SectionOffsets[9];
        if (start < 0 || end <= start || end > file.Length)
            return WorldFileBestiaryDecodeResult.InvalidSectionBounds;

        var reader = new BestiaryReader(file.Slice(start, end - start));
        long totalStringBytes = 0;

        WorldFileBestiaryDecodeResult countResult = reader.TryReadCount(limits.MaxKillEntries, out int killCount);
        if (countResult != WorldFileBestiaryDecodeResult.Decoded)
            return countResult;

        var killsById = new Dictionary<string, int>(killCount, StringComparer.Ordinal);
        for (int i = 0; i < killCount; i++)
        {
            WorldFileBestiaryDecodeResult stringResult = reader.TryReadString(
                limits.MaxPersistentIdBytes,
                limits.MaxTotalPersistentIdBytes,
                ref totalStringBytes,
                out string persistentId);
            if (stringResult != WorldFileBestiaryDecodeResult.Decoded)
            {
                bytesConsumed = reader.Offset;
                return stringResult;
            }

            if (!reader.TryReadInt32(out int value))
            {
                bytesConsumed = reader.Offset;
                return WorldFileBestiaryDecodeResult.Truncated;
            }
            if (value < 0 || value > MaximumKillCount)
            {
                bytesConsumed = reader.Offset;
                return WorldFileBestiaryDecodeResult.InvalidKillCount;
            }

            killsById[persistentId] = value;
        }

        countResult = reader.TryReadCount(limits.MaxSightEntries, out int sightCount);
        if (countResult != WorldFileBestiaryDecodeResult.Decoded)
        {
            bytesConsumed = reader.Offset;
            return countResult;
        }

        var sightings = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < sightCount; i++)
        {
            WorldFileBestiaryDecodeResult stringResult = reader.TryReadString(
                limits.MaxPersistentIdBytes,
                limits.MaxTotalPersistentIdBytes,
                ref totalStringBytes,
                out string persistentId);
            if (stringResult != WorldFileBestiaryDecodeResult.Decoded)
            {
                bytesConsumed = reader.Offset;
                return stringResult;
            }
            sightings.Add(persistentId);
        }

        countResult = reader.TryReadCount(limits.MaxChatEntries, out int chatCount);
        if (countResult != WorldFileBestiaryDecodeResult.Decoded)
        {
            bytesConsumed = reader.Offset;
            return countResult;
        }

        var chats = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < chatCount; i++)
        {
            WorldFileBestiaryDecodeResult stringResult = reader.TryReadString(
                limits.MaxPersistentIdBytes,
                limits.MaxTotalPersistentIdBytes,
                ref totalStringBytes,
                out string persistentId);
            if (stringResult != WorldFileBestiaryDecodeResult.Decoded)
            {
                bytesConsumed = reader.Offset;
                return stringResult;
            }
            chats.Add(persistentId);
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileBestiaryDecodeResult.SectionLengthMismatch;

        var kills = new WorldBestiaryKill[killsById.Count];
        int index = 0;
        foreach ((string persistentId, int value) in killsById)
            kills[index++] = new WorldBestiaryKill(persistentId, value);

        bestiary = new WorldBestiaryData(kills, [.. sightings], [.. chats]);
        return WorldFileBestiaryDecodeResult.Decoded;
    }

    private ref struct BestiaryReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public BestiaryReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        public WorldFileBestiaryDecodeResult TryReadCount(int maximum, out int count)
        {
            if (!TryReadInt32(out count))
                return WorldFileBestiaryDecodeResult.Truncated;
            if (count < 0)
                return WorldFileBestiaryDecodeResult.InvalidEntryCount;
            if (count > maximum)
                return WorldFileBestiaryDecodeResult.EntryBudgetExceeded;
            return WorldFileBestiaryDecodeResult.Decoded;
        }

        public WorldFileBestiaryDecodeResult TryReadString(
            int maximumBytes,
            long maximumTotalBytes,
            ref long totalBytes,
            out string value)
        {
            value = string.Empty;
            WorldFileBestiaryDecodeResult lengthResult = TryRead7BitEncodedInt(out int length);
            if (lengthResult != WorldFileBestiaryDecodeResult.Decoded)
                return lengthResult;
            if (length > maximumBytes)
                return WorldFileBestiaryDecodeResult.StringTooLarge;
            if (totalBytes + length > maximumTotalBytes)
                return WorldFileBestiaryDecodeResult.TotalStringBudgetExceeded;
            if (_data.Length - _offset < length)
                return WorldFileBestiaryDecodeResult.Truncated;

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, length));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileBestiaryDecodeResult.InvalidUtf8;
            }

            _offset += length;
            totalBytes += length;
            return WorldFileBestiaryDecodeResult.Decoded;
        }

        private WorldFileBestiaryDecodeResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length) { value = default; return WorldFileBestiaryDecodeResult.Truncated; }
                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0) { value = default; return WorldFileBestiaryDecodeResult.InvalidStringLength; }
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue) { value = default; return WorldFileBestiaryDecodeResult.InvalidStringLength; }
                    value = (int)result;
                    return WorldFileBestiaryDecodeResult.Decoded;
                }
            }

            value = default;
            return WorldFileBestiaryDecodeResult.InvalidStringLength;
        }
    }
}
