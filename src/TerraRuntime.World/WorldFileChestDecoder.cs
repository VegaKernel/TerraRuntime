using System.Buffers.Binary;
using System.Text;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Decodes the Terraria 1.4.5.8 chest section. The current file format stores a per-chest item count,
/// so caller-provided limits bound both individual and aggregate allocations before arrays are created.
/// Duplicate coordinates follow vanilla load behavior: the first chest wins while surviving entries retain
/// their original file-order slot IDs for packet-10 synchronization.
/// </summary>
public static class WorldFileChestDecoder
{
    private const int MaximumNameBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileChestDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        int maxItemsPerChest,
        long maxTotalItems,
        out WorldChest[] chests,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(maxItemsPerChest);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalItems);

        chests = [];
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileChestDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 4)
            return WorldFileChestDecodeResult.InvalidSectionBounds;

        int sectionStart = envelope.SectionOffsets[2];
        int sectionEnd = envelope.SectionOffsets[3];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
            return WorldFileChestDecodeResult.InvalidSectionBounds;

        var reader = new ChestReader(file.Slice(sectionStart, sectionEnd - sectionStart));
        if (!reader.TryReadInt16(out short chestCountValue))
            return WorldFileChestDecodeResult.Truncated;
        if (chestCountValue < 0 || chestCountValue > VanillaWorldFormat326.MaximumChestSlots)
            return WorldFileChestDecodeResult.InvalidChestCount;

        int chestCount = chestCountValue;
        var loaded = new List<WorldChest>(chestCount);
        var positions = new HashSet<long>();
        long totalItems = 0;

        int width = header.Dimensions.WidthTiles;
        int height = header.Dimensions.HeightTiles;
        for (int i = 0; i < chestCount; i++)
        {
            if (!reader.TryReadInt32(out int x) || !reader.TryReadInt32(out int y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileChestDecodeResult.Truncated;
            }

            if (width < 2 || height < 2 || (uint)x >= (uint)(width - 1) || (uint)y >= (uint)(height - 1))
            {
                bytesConsumed = reader.Offset;
                return WorldFileChestDecodeResult.InvalidChestCoordinates;
            }

            WorldFileChestDecodeResult nameResult = reader.TryReadString(out string name);
            if (nameResult != WorldFileChestDecodeResult.Decoded)
            {
                bytesConsumed = reader.Offset;
                return nameResult;
            }

            if (!reader.TryReadInt32(out int itemCount))
            {
                bytesConsumed = reader.Offset;
                return WorldFileChestDecodeResult.Truncated;
            }

            if (itemCount < 0 || itemCount > maxItemsPerChest || totalItems + itemCount > maxTotalItems)
            {
                bytesConsumed = reader.Offset;
                return WorldFileChestDecodeResult.ItemBudgetExceeded;
            }

            totalItems += itemCount;
            var items = new WorldChestItem[itemCount];
            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                if (!reader.TryReadInt16(out short stack))
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileChestDecodeResult.Truncated;
                }

                if (stack == 0)
                    continue;

                if (!reader.TryReadInt32(out int itemType) || !reader.TryReadByte(out byte prefix))
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileChestDecodeResult.Truncated;
                }

                if (!VanillaItemIds.TryCreate(itemType, out _))
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileChestDecodeResult.InvalidItemType;
                }

                items[itemIndex] = new WorldChestItem(stack < 0 ? 1 : stack, itemType, prefix);
            }

            long positionKey = ((long)(uint)x << 32) | (uint)y;
            if (positions.Add(positionKey))
                loaded.Add(new WorldChest(checked((short)i), x, y, name, items));
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileChestDecodeResult.SectionLengthMismatch;

        chests = loaded.ToArray();
        return WorldFileChestDecodeResult.Decoded;
    }

    private ref struct ChestReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public ChestReader(ReadOnlySpan<byte> data)
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

        public WorldFileChestDecodeResult TryReadString(out string value)
        {
            value = string.Empty;
            WorldFileChestDecodeResult lengthResult = TryRead7BitEncodedInt(out int length);
            if (lengthResult != WorldFileChestDecodeResult.Decoded)
                return lengthResult;
            if (length > MaximumNameBytes)
                return WorldFileChestDecodeResult.StringTooLarge;
            if (_data.Length - _offset < length)
                return WorldFileChestDecodeResult.Truncated;

            try
            {
                value = StrictUtf8.GetString(_data.Slice(_offset, length));
            }
            catch (DecoderFallbackException)
            {
                return WorldFileChestDecodeResult.InvalidUtf8;
            }

            _offset += length;
            return WorldFileChestDecodeResult.Decoded;
        }

        private WorldFileChestDecodeResult TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (_offset >= _data.Length) { value = default; return WorldFileChestDecodeResult.Truncated; }
                byte current = _data[_offset++];
                if (shift == 28 && (current & 0xF0) != 0) { value = default; return WorldFileChestDecodeResult.InvalidStringLength; }
                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue) { value = default; return WorldFileChestDecodeResult.InvalidStringLength; }
                    value = (int)result;
                    return WorldFileChestDecodeResult.Decoded;
                }
            }

            value = default;
            return WorldFileChestDecodeResult.InvalidStringLength;
        }
    }
}
