using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

/// <summary>
/// Decodes the Terraria 1.4.5.8 sign section. Tile-sign semantic validation is intentionally deferred until
/// the verified tile-sign catalog is part of runtime state; coordinates and text allocations are still bounded here.
/// Duplicate coordinates follow vanilla load behavior: the first sign wins while surviving entries retain
/// their original file-order slot IDs for packet-10 synchronization.
/// </summary>
public static class WorldFileSignDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileSignDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        int maxTextBytesPerSign,
        long maxTotalTextBytes,
        out WorldSign[] signs,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTextBytesPerSign);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalTextBytes);

        signs = [];
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileSignDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 5)
            return WorldFileSignDecodeResult.InvalidSectionBounds;

        int sectionStart = envelope.SectionOffsets[3];
        int sectionEnd = envelope.SectionOffsets[4];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
            return WorldFileSignDecodeResult.InvalidSectionBounds;

        var reader = new SignReader(file.Slice(sectionStart, sectionEnd - sectionStart));
        if (!reader.TryReadInt16(out short signCountValue))
            return WorldFileSignDecodeResult.Truncated;
        if (signCountValue < 0 || signCountValue > VanillaWorldFormat326.MaximumSignSlots)
            return WorldFileSignDecodeResult.InvalidSignCount;

        int signCount = signCountValue;
        var loaded = new List<WorldSign>(signCount);
        var positions = new HashSet<long>();
        long totalTextBytes = 0;

        for (int i = 0; i < signCount; i++)
        {
            WorldFileSignDecodeResult textResult = reader.TryReadString(
                maxTextBytesPerSign,
                maxTotalTextBytes - totalTextBytes,
                out string text,
                out int textBytes);
            if (textResult != WorldFileSignDecodeResult.Decoded)
            {
                bytesConsumed = reader.Offset;
                return textResult;
            }
            totalTextBytes += textBytes;

            if (!reader.TryReadInt32(out int x) || !reader.TryReadInt32(out int y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileSignDecodeResult.Truncated;
            }

            if ((uint)x >= (uint)header.Dimensions.WidthTiles || (uint)y >= (uint)header.Dimensions.HeightTiles)
            {
                bytesConsumed = reader.Offset;
                return WorldFileSignDecodeResult.InvalidSignCoordinates;
            }

            long positionKey = ((long)(uint)x << 32) | (uint)y;
            if (positions.Add(positionKey))
                loaded.Add(new WorldSign(checked((short)i), text, x, y));
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileSignDecodeResult.SectionLengthMismatch;

        signs = loaded.ToArray();
        return WorldFileSignDecodeResult.Decoded;
    }

    private ref struct SignReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public SignReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadInt16(out short value)
        {
            if (_data.Length - _offset < sizeof(short)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt16LittleEndian(_data[_offset..]);
            _offset += sizeof(short);
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        public WorldFileSignDecodeResult TryReadString(
            int perSignBudget,
            long remainingTotalBudget,
            out string value,
            out int byteLength)
        {
            value = string.Empty;
            byteLength = 0;

            WorldFileSignDecodeResult lengthResult = TryRead7BitEncodedInt(out int length);
            if (lengthResult != WorldFileSignDecodeResult.Decoded)
                return lengthResult;
            if (length > perSignBudget || length > remainingTotalBudget)
                return WorldFileSignDecodeResult.TextBudgetExceeded;
            if (_data.Length - _offset < length)
                return WorldFileSignDecodeResult.Truncated;

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, length));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileSignDecodeResult.InvalidUtf8;
            }

            _offset += length;
            byteLength = length;
            return WorldFileSignDecodeResult.Decoded;
        }

        private WorldFileSignDecodeResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length) { value = default; return WorldFileSignDecodeResult.Truncated; }
                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0) { value = default; return WorldFileSignDecodeResult.InvalidStringLength; }
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue) { value = default; return WorldFileSignDecodeResult.InvalidStringLength; }
                    value = (int)result;
                    return WorldFileSignDecodeResult.Decoded;
                }
            }

            value = 0;
            return WorldFileSignDecodeResult.InvalidStringLength;
        }
    }
}
