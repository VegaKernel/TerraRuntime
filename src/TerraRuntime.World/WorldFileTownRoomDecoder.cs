using System.Buffers.Binary;

namespace TerraRuntime.World;

public readonly record struct WorldTownRoom(int NpcType, int X, int Y);

public enum WorldFileTownRoomDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    Truncated = 3,
    InvalidRoomCount = 4,
    RoomBudgetExceeded = 5,
    InvalidNpcType = 6,
    InvalidCoordinates = 7,
    DuplicateNpcType = 8,
    SectionLengthMismatch = 9
}

public static class WorldFileTownRoomDecoder
{
    public static WorldFileTownRoomDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        int maxRooms,
        out WorldTownRoom[] rooms,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRooms);

        rooms = [];
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileTownRoomDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 9)
            return WorldFileTownRoomDecodeResult.InvalidSectionBounds;

        int start = envelope.SectionOffsets[7];
        int end = envelope.SectionOffsets[8];
        if (start < 0 || end <= start || end > file.Length)
            return WorldFileTownRoomDecodeResult.InvalidSectionBounds;

        var reader = new TownReader(file.Slice(start, end - start));
        if (!reader.TryReadInt32(out int count))
            return WorldFileTownRoomDecodeResult.Truncated;
        if (count < 0)
            return WorldFileTownRoomDecodeResult.InvalidRoomCount;
        if (count > maxRooms)
            return WorldFileTownRoomDecodeResult.RoomBudgetExceeded;

        var loaded = new WorldTownRoom[count];
        var npcTypes = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadInt32(out int npcType) ||
                !reader.TryReadInt32(out int x) ||
                !reader.TryReadInt32(out int y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileTownRoomDecodeResult.Truncated;
            }

            if ((uint)npcType >= VanillaWorldFormat326.NpcTypeCount)
            {
                bytesConsumed = reader.Offset;
                return WorldFileTownRoomDecodeResult.InvalidNpcType;
            }

            if ((uint)x >= (uint)header.Dimensions.WidthTiles || (uint)y >= (uint)header.Dimensions.HeightTiles)
            {
                bytesConsumed = reader.Offset;
                return WorldFileTownRoomDecodeResult.InvalidCoordinates;
            }

            if (!npcTypes.Add(npcType))
            {
                bytesConsumed = reader.Offset;
                return WorldFileTownRoomDecodeResult.DuplicateNpcType;
            }

            loaded[i] = new WorldTownRoom(npcType, x, y);
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileTownRoomDecodeResult.SectionLengthMismatch;

        rooms = loaded;
        return WorldFileTownRoomDecodeResult.Decoded;
    }

    private ref struct TownReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public TownReader(ReadOnlySpan<byte> data)
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
    }
}
