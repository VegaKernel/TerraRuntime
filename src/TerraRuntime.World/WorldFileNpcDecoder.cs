using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

/// <summary>
/// Decodes the Terraria 1.4.5.8 NPC persistence section into inert records. No gameplay NPC is created here.
/// Sequence counts absent from the file format are bounded explicitly by caller-provided budgets.
/// </summary>
public static class WorldFileNpcDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileNpcDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileNpcDecodeOptions options,
        out WorldNpcPersistence? persistence,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        options.Validate();
        persistence = null;
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileNpcDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 6)
            return WorldFileNpcDecodeResult.InvalidSectionBounds;

        int sectionStart = envelope.SectionOffsets[4];
        int sectionEnd = envelope.SectionOffsets[5];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
            return WorldFileNpcDecodeResult.InvalidSectionBounds;

        var reader = new NpcReader(file.Slice(sectionStart, sectionEnd - sectionStart));
        if (!reader.TryReadInt32(out int shimmerCount))
            return WorldFileNpcDecodeResult.Truncated;
        if (shimmerCount < 0 || shimmerCount > options.MaxShimmeredTownNpcIndices)
            return WorldFileNpcDecodeResult.InvalidShimmerCount;

        var shimmered = new int[shimmerCount];
        for (int i = 0; i < shimmerCount; i++)
        {
            if (!reader.TryReadInt32(out int index))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }

            if ((uint)index >= (uint)options.MaxShimmerIndexExclusive)
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.InvalidShimmerIndex;
            }

            shimmered[i] = index;
        }

        var townNpcs = new List<WorldTownNpc>(Math.Min(options.MaxTownNpcs, 32));
        long totalNameBytes = 0;
        while (true)
        {
            if (!reader.TryReadBoolean(out bool active))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }
            if (!active)
                break;
            if (townNpcs.Count >= options.MaxTownNpcs)
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.TownNpcBudgetExceeded;
            }

            if (!reader.TryReadInt32(out int netId))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }

            WorldFileNpcDecodeResult nameResult = reader.TryReadString(
                options.MaxNameBytesPerTownNpc,
                options.MaxTotalNameBytes - totalNameBytes,
                out string givenName,
                out int nameBytes);
            if (nameResult != WorldFileNpcDecodeResult.Decoded)
            {
                bytesConsumed = reader.Offset;
                return nameResult;
            }
            totalNameBytes += nameBytes;

            if (!reader.TryReadSingle(out float x) ||
                !reader.TryReadSingle(out float y) ||
                !reader.TryReadBoolean(out bool homeless) ||
                !reader.TryReadInt32(out int homeTileX) ||
                !reader.TryReadInt32(out int homeTileY) ||
                !reader.TryReadByte(out byte bits))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }

            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.NonFinitePosition;
            }

            int? variation = null;
            if ((bits & 0x01) != 0)
            {
                if (!reader.TryReadInt32(out int variationValue))
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileNpcDecodeResult.Truncated;
                }
                variation = variationValue;
            }

            if (!reader.TryReadBoolean(out bool homelessDespawn))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }

            townNpcs.Add(new WorldTownNpc(
                netId,
                givenName,
                x,
                y,
                homeless,
                homeTileX,
                homeTileY,
                variation,
                homelessDespawn));
        }

        var persistentNpcs = new List<WorldPersistentNpc>(Math.Min(options.MaxPersistentNpcs, 32));
        while (true)
        {
            if (!reader.TryReadBoolean(out bool active))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }
            if (!active)
                break;
            if (persistentNpcs.Count >= options.MaxPersistentNpcs)
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.PersistentNpcBudgetExceeded;
            }

            if (!reader.TryReadInt32(out int netId) ||
                !reader.TryReadSingle(out float x) ||
                !reader.TryReadSingle(out float y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.Truncated;
            }

            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileNpcDecodeResult.NonFinitePosition;
            }

            persistentNpcs.Add(new WorldPersistentNpc(netId, x, y));
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileNpcDecodeResult.SectionLengthMismatch;

        persistence = new WorldNpcPersistence(shimmered, townNpcs.ToArray(), persistentNpcs.ToArray());
        return WorldFileNpcDecodeResult.Decoded;
    }

    private ref struct NpcReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public NpcReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadByte(out byte value)
        {
            if (_offset >= _data.Length) { value = default; return false; }
            value = _data[_offset++];
            return true;
        }

        public bool TryReadBoolean(out bool value)
        {
            if (!TryReadByte(out byte raw)) { value = default; return false; }
            value = raw != 0;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        public bool TryReadSingle(out float value)
        {
            if (!TryReadInt32(out int bits)) { value = default; return false; }
            value = BitConverter.Int32BitsToSingle(bits);
            return true;
        }

        public WorldFileNpcDecodeResult TryReadString(
            int perNameBudget,
            long remainingTotalBudget,
            out string value,
            out int byteLength)
        {
            value = string.Empty;
            byteLength = 0;
            WorldFileNpcDecodeResult lengthResult = TryRead7BitEncodedInt(out int length);
            if (lengthResult != WorldFileNpcDecodeResult.Decoded)
                return lengthResult;
            if (length > perNameBudget || length > remainingTotalBudget)
                return WorldFileNpcDecodeResult.NameBudgetExceeded;
            if (_data.Length - _offset < length)
                return WorldFileNpcDecodeResult.Truncated;

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, length));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileNpcDecodeResult.InvalidUtf8;
            }

            _offset += length;
            byteLength = length;
            return WorldFileNpcDecodeResult.Decoded;
        }

        private WorldFileNpcDecodeResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length) { value = default; return WorldFileNpcDecodeResult.Truncated; }
                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0) { value = default; return WorldFileNpcDecodeResult.InvalidStringLength; }
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue) { value = default; return WorldFileNpcDecodeResult.InvalidStringLength; }
                    value = (int)result;
                    return WorldFileNpcDecodeResult.Decoded;
                }
            }

            value = default;
            return WorldFileNpcDecodeResult.InvalidStringLength;
        }
    }
}
