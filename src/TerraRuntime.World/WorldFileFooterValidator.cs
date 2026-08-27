using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

public enum WorldFileFooterValidationResult : byte
{
    Valid = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    Truncated = 3,
    InvalidMarker = 4,
    InvalidStringLength = 5,
    StringTooLarge = 6,
    InvalidUtf8 = 7,
    WorldNameMismatch = 8,
    WorldIdMismatch = 9,
    TrailingBytes = 10
}

/// <summary>
/// Validates the Terraria 1.4.5.8 footer written after the final section pointer:
/// true marker, world name and world id, with no trailing bytes.
/// </summary>
public static class WorldFileFooterValidator
{
    private const int MaximumNameBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileFooterValidationResult Validate(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileFooterValidationResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < VanillaWorldFormat326.SectionCount)
            return WorldFileFooterValidationResult.InvalidSectionBounds;

        int start = envelope.SectionOffsets[10];
        if (start < 0 || start >= file.Length)
            return WorldFileFooterValidationResult.InvalidSectionBounds;

        var reader = new FooterReader(file[start..]);
        if (!reader.TryReadByte(out byte marker))
            return WorldFileFooterValidationResult.Truncated;
        if (marker == 0)
            return WorldFileFooterValidationResult.InvalidMarker;

        WorldFileFooterValidationResult stringResult = reader.TryReadString(out string worldName);
        if (stringResult != WorldFileFooterValidationResult.Valid)
        {
            bytesConsumed = reader.Offset;
            return stringResult;
        }

        if (!reader.TryReadInt32(out int worldId))
        {
            bytesConsumed = reader.Offset;
            return WorldFileFooterValidationResult.Truncated;
        }

        bytesConsumed = reader.Offset;
        if (!string.Equals(worldName, header.Name, StringComparison.Ordinal))
            return WorldFileFooterValidationResult.WorldNameMismatch;
        if (worldId != header.WorldId)
            return WorldFileFooterValidationResult.WorldIdMismatch;
        if (reader.Remaining != 0)
            return WorldFileFooterValidationResult.TrailingBytes;

        return WorldFileFooterValidationResult.Valid;
    }

    private ref struct FooterReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public FooterReader(ReadOnlySpan<byte> data)
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

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        public WorldFileFooterValidationResult TryReadString(out string value)
        {
            value = string.Empty;
            WorldFileFooterValidationResult lengthResult = TryRead7BitEncodedInt(out int length);
            if (lengthResult != WorldFileFooterValidationResult.Valid)
                return lengthResult;
            if (length > MaximumNameBytes)
                return WorldFileFooterValidationResult.StringTooLarge;
            if (_data.Length - _offset < length)
                return WorldFileFooterValidationResult.Truncated;

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, length));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileFooterValidationResult.InvalidUtf8;
            }

            _offset += length;
            return WorldFileFooterValidationResult.Valid;
        }

        private WorldFileFooterValidationResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length) { value = default; return WorldFileFooterValidationResult.Truncated; }
                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0) { value = default; return WorldFileFooterValidationResult.InvalidStringLength; }
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue) { value = default; return WorldFileFooterValidationResult.InvalidStringLength; }
                    value = (int)result;
                    return WorldFileFooterValidationResult.Valid;
                }
            }

            value = default;
            return WorldFileFooterValidationResult.InvalidStringLength;
        }
    }
}
