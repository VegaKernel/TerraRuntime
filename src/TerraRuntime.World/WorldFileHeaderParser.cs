using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

/// <summary>
/// Parses the stable leading fields written by Terraria 1.4.5.8 WorldFile.SaveWorldHeader.
/// The reader is intentionally limited to the current format until older layouts are verified independently.
/// </summary>
public static class WorldFileHeaderParser
{
    // Safety ceiling for length-prefixed strings from an untrusted/corrupt world file. This is not a vanilla gameplay limit.
    private const int MaximumStringBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static WorldFileHeaderParseResult TryParse(ReadOnlySpan<byte> file, out WorldFileHeader? header)
    {
        header = null;

        if (WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out _) != WorldFileEnvelopeParseResult.Parsed ||
            envelope is null)
        {
            return WorldFileHeaderParseResult.InvalidEnvelope;
        }

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
        {
            return WorldFileHeaderParseResult.UnsupportedVersion;
        }

        if (envelope.SectionOffsets.Length < 2)
        {
            return WorldFileHeaderParseResult.InvalidSectionBounds;
        }

        int sectionStart = envelope.SectionOffsets[0];
        int sectionEnd = envelope.SectionOffsets[1];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
        {
            return WorldFileHeaderParseResult.InvalidSectionBounds;
        }

        var reader = new HeaderReader(file.Slice(sectionStart, sectionEnd - sectionStart));

        WorldFileHeaderParseResult result = reader.TryReadString(out string name);
        if (result != WorldFileHeaderParseResult.Parsed)
        {
            return result;
        }

        result = reader.TryReadString(out string seedText);
        if (result != WorldFileHeaderParseResult.Parsed)
        {
            return result;
        }

        if (!reader.TryReadUInt64(out ulong worldGeneratorVersion) ||
            !reader.TryReadGuid(out Guid uniqueId) ||
            !reader.TryReadInt32(out int worldId) ||
            !reader.TryReadInt32(out int leftWorld) ||
            !reader.TryReadInt32(out int rightWorld) ||
            !reader.TryReadInt32(out int topWorld) ||
            !reader.TryReadInt32(out int bottomWorld) ||
            !reader.TryReadInt32(out int heightTiles) ||
            !reader.TryReadInt32(out int widthTiles))
        {
            return WorldFileHeaderParseResult.Truncated;
        }

        if (rightWorld <= leftWorld || bottomWorld <= topWorld)
        {
            return WorldFileHeaderParseResult.InvalidWorldBounds;
        }

        WorldDimensions dimensions;
        try
        {
            dimensions = new WorldDimensions(widthTiles, heightTiles);
        }
        catch (ArgumentOutOfRangeException)
        {
            return WorldFileHeaderParseResult.InvalidDimensions;
        }

        header = new WorldFileHeader(
            name,
            seedText,
            worldGeneratorVersion,
            uniqueId,
            worldId,
            leftWorld,
            rightWorld,
            topWorld,
            bottomWorld,
            dimensions);
        return WorldFileHeaderParseResult.Parsed;
    }

    private ref struct HeaderReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public HeaderReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public WorldFileHeaderParseResult TryReadString(out string value)
        {
            value = string.Empty;
            WorldFileHeaderParseResult lengthResult = TryRead7BitEncodedInt(out int byteLength);
            if (lengthResult != WorldFileHeaderParseResult.Parsed)
            {
                return lengthResult;
            }

            if (byteLength > MaximumStringBytes)
            {
                return WorldFileHeaderParseResult.StringTooLarge;
            }

            if (_data.Length - _offset < byteLength)
            {
                return WorldFileHeaderParseResult.Truncated;
            }

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, byteLength));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileHeaderParseResult.InvalidUtf8;
            }

            _offset += byteLength;
            return WorldFileHeaderParseResult.Parsed;
        }

        public bool TryReadUInt64(out ulong value)
        {
            if (_data.Length - _offset < sizeof(ulong))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadUInt64LittleEndian(_data[_offset..]);
            _offset += sizeof(ulong);
            return true;
        }

        public bool TryReadGuid(out Guid value)
        {
            const int GuidBytes = 16;
            if (_data.Length - _offset < GuidBytes)
            {
                value = default;
                return false;
            }

            value = new Guid(_data.Slice(_offset, GuidBytes));
            _offset += GuidBytes;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        private WorldFileHeaderParseResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length)
                {
                    value = default;
                    return WorldFileHeaderParseResult.Truncated;
                }

                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0)
                {
                    value = default;
                    return WorldFileHeaderParseResult.InvalidStringLength;
                }

                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                    {
                        value = default;
                        return WorldFileHeaderParseResult.InvalidStringLength;
                    }

                    value = (int)result;
                    return WorldFileHeaderParseResult.Parsed;
                }
            }

            value = default;
            return WorldFileHeaderParseResult.InvalidStringLength;
        }
    }
}
