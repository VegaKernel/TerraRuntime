using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>TerrariaServer 1.4.5.8 packet-52 lock/unlock action and tile coordinate.</summary>
public readonly record struct TerrariaLockAndUnlockState(byte Action, short TileX, short TileY);

public enum TerrariaLockAndUnlockDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

/// <summary>
/// Wire adapter for MessageBuffer.GetData case 52. Gameplay authority decides which actions it supports.
/// </summary>
public static class TerrariaLockAndUnlockCodec
{
    public const int PayloadLength = 5;

    public static TerrariaLockAndUnlockDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaLockAndUnlockState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.LockAndUnlock)
            return TerrariaLockAndUnlockDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaLockAndUnlockDecodeResult.InvalidPayloadLength;

        Span<byte> payload = stackalloc byte[PayloadLength];
        if (frame.Payload.IsSingleSegment)
            frame.Payload.FirstSpan.CopyTo(payload);
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(payload[offset..]);
                offset += segment.Length;
            }
        }

        state = new TerrariaLockAndUnlockState(
            payload[0],
            BinaryPrimitives.ReadInt16LittleEndian(payload[1..]),
            BinaryPrimitives.ReadInt16LittleEndian(payload[3..]));
        return TerrariaLockAndUnlockDecodeResult.Decoded;
    }

    public static bool TryEncode(in TerrariaLockAndUnlockState state, out byte[] frame)
    {
        frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + PayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = (byte)TerrariaMessageId.LockAndUnlock;
        frame[3] = state.Action;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(6), state.TileY);
        return true;
    }
}
