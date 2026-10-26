using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.Protocol;

/// <summary>Wire observation only. Player identity is rewritten from the authenticated connection.</summary>
public readonly record struct TerrariaTilePickingState1458(byte PlayerSlot, short TileX, short TileY, byte Damage);

public enum TerrariaTilePickingDecodeResult1458 : byte
{
    Decoded,
    WrongMessageId,
    InvalidPayloadLength
}

/// <summary>TerrariaServer 1.4.5.8 NetMessage/MessageBuffer case125 fixed six-byte payload.</summary>
public static class TerrariaTilePickingCodec1458
{
    public const int PayloadLength = 6;
    public const ushort FrameLength = 9;

    public static TerrariaTilePickingDecodeResult1458 TryDecode(in TerrariaFrame frame,
        out TerrariaTilePickingState1458 state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.TilePicking)
            return TerrariaTilePickingDecodeResult1458.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaTilePickingDecodeResult1458.InvalidPayloadLength;
        Span<byte> payload = stackalloc byte[PayloadLength];
        frame.Payload.CopyTo(payload);
        state = new(payload[0], BinaryPrimitives.ReadInt16LittleEndian(payload[1..]),
            BinaryPrimitives.ReadInt16LittleEndian(payload[3..]), payload[5]);
        return TerrariaTilePickingDecodeResult1458.Decoded;
    }

    public static byte[] Encode(in TerrariaTilePickingState1458 state)
    {
        var bytes = new byte[FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, FrameLength);
        bytes[2] = (byte)TerrariaMessageId.TilePicking;
        bytes[3] = state.PlayerSlot;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), state.TileY);
        bytes[8] = state.Damage;
        return bytes;
    }
}
