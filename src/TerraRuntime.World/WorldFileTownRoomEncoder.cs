using System.Buffers.Binary;

namespace TerraRuntime.World;

public enum WorldFileTownRoomEncodeResult : byte
{
    Encoded = 0,
    RoomBudgetExceeded = 1,
    InvalidNpcType = 2,
    InvalidCoordinates = 3,
    DuplicateNpcType = 4,
    DestinationNotWritable = 5,
    WriteFailed = 6
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 town-room persistence section. Each NPC type may own at most one saved room,
/// matching the decoder and vanilla room-assignment identity model.
/// </summary>
public static class WorldFileTownRoomEncoder
{
    public static WorldFileTownRoomEncodeResult TryEncode(
        ReadOnlySpan<WorldTownRoom> source,
        WorldDimensions dimensions,
        int maxRooms,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRooms);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileTownRoomEncodeResult.DestinationNotWritable;
        if (source.Length > maxRooms)
            return WorldFileTownRoomEncodeResult.RoomBudgetExceeded;

        var npcTypes = new HashSet<int>();
        foreach (WorldTownRoom room in source)
        {
            if ((uint)room.NpcType >= VanillaWorldFormat326.NpcTypeCount)
                return WorldFileTownRoomEncodeResult.InvalidNpcType;
            if ((uint)room.X >= (uint)dimensions.WidthTiles ||
                (uint)room.Y >= (uint)dimensions.HeightTiles)
            {
                return WorldFileTownRoomEncodeResult.InvalidCoordinates;
            }
            if (!npcTypes.Add(room.NpcType))
                return WorldFileTownRoomEncodeResult.DuplicateNpcType;
        }

        long encodedLength = checked(sizeof(int) + ((long)source.Length * sizeof(int) * 3));
        try
        {
            Span<byte> buffer = stackalloc byte[sizeof(int) * 3];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, source.Length);
            destination.Write(buffer[..sizeof(int)]);

            foreach (WorldTownRoom room in source)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer, room.NpcType);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[sizeof(int)..], room.X);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[(sizeof(int) * 2)..], room.Y);
                destination.Write(buffer);
            }
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileTownRoomEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileTownRoomEncodeResult.Encoded;
    }
}
