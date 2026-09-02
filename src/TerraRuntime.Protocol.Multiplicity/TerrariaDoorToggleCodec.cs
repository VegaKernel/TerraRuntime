using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// TerrariaServer 1.4.5.8 packet-19 door/tall-gate actions. The wire direction is a single boolean byte:
/// any non-zero value means +1 while zero means -1, matching MessageBuffer.ReadBoolean-style handling.
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
/// Wire-only adapter for Terraria 1.4.5.8 packet 19. Multiplicity owns the packet layout while TerraRuntime
/// projects only protocol-neutral values; mutation authority stays above this codec.
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

        var packet = new DoorUse
        {
            Action = (DoorUseAction)state.Action,
            TileX = state.TileX,
            TileY = state.TileY,
            Direction = state.DirectionX == 1 ? (byte)1 : (byte)0
        };

        frame = packet.ToArray();
        return TerrariaDoorToggleEncodeResult.Encoded;
    }

    private static TerrariaDoorToggleState DecodePayload(ReadOnlySpan<byte> payload)
    {
        DoorUseView packet = DoorUseView.FromPayload(payload);
        return new TerrariaDoorToggleState(
            Action: (byte)packet.Action,
            TileX: packet.TileX,
            TileY: packet.TileY,
            DirectionX: packet.Direction != 0 ? 1 : -1);
    }
}
