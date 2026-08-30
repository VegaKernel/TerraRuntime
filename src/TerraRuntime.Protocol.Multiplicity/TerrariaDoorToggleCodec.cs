using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// TerrariaServer 1.4.5.8 packet-19 door/tall-gate actions. The wire direction is a single boolean byte:
/// one means +1, zero (and, on decode, every other zero value) means -1.
/// </summary>
public enum TerrariaDoorToggleAction : byte
{
    OpenDoor = 0,
    CloseDoor = 1,
    OpenTrapdoor = 2,
    CloseTrapdoor = 3,
    OpenTallGate = 4,
    CloseTallGate = 5
}

public readonly record struct TerrariaDoorToggleState(
    byte Action,
    short TileX,
    short TileY,
    int DirectionX)
{
    public bool TryGetKnownAction(out TerrariaDoorToggleAction action)
    {
        if (Action <= (byte)TerrariaDoorToggleAction.CloseTallGate)
        {
            action = (TerrariaDoorToggleAction)Action;
            return true;
        }

        action = default;
        return false;
    }

    public bool IsValid => DirectionX is -1 or 1;
}

public enum TerrariaDoorToggleDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

public enum TerrariaDoorToggleEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}

/// <summary>
/// Wire-only adapter for Terraria 1.4.5.8 packet 19: action byte, X/Y Int16 values and one direction byte.
/// Mutation authority stays above this codec.
/// </summary>
public static class TerrariaDoorToggleCodec
{
    public const int PayloadLength = 6;

    public static TerrariaDoorToggleDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaDoorToggleState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.DoorToggle)
            return TerrariaDoorToggleDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaDoorToggleDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
            return TerrariaDoorToggleDecodeResult.Decoded;
        }

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        state = DecodePayload(scratch);
        return TerrariaDoorToggleDecodeResult.Decoded;
    }

    public static TerrariaDoorToggleEncodeResult TryEncode(
        in TerrariaDoorToggleState state,
        out byte[] frame)
    {
        if (!state.IsValid)
        {
            frame = [];
            return TerrariaDoorToggleEncodeResult.InvalidState;
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = state.Action;
        BinaryPrimitives.WriteInt16LittleEndian(payload[1..3], state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(payload[3..5], state.TileY);
        payload[5] = state.DirectionX == 1 ? (byte)1 : (byte)0;

        var writer = new ArrayBufferWriter<byte>(PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.DoorToggle,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
        {
            frame = [];
            return TerrariaDoorToggleEncodeResult.FrameTooLarge;
        }
        if (result != TerrariaFrameWriteResult.Written)
        {
            frame = [];
            return TerrariaDoorToggleEncodeResult.Failed;
        }

        frame = writer.WrittenSpan.ToArray();
        return TerrariaDoorToggleEncodeResult.Encoded;
    }

    private static TerrariaDoorToggleState DecodePayload(ReadOnlySpan<byte> payload) =>
        new(
            Action: payload[0],
            TileX: BinaryPrimitives.ReadInt16LittleEndian(payload[1..3]),
            TileY: BinaryPrimitives.ReadInt16LittleEndian(payload[3..5]),
            DirectionX: payload[5] != 0 ? 1 : -1);
}
