using System.Buffers.Binary;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>TerrariaServer 1.4.5.8 packet-60 household disposition.</summary>
public enum TerrariaNpcHomeStatus : byte
{
    None = 0,
    Homeless = 1,
    HasRoom = 2
}

public readonly record struct TerrariaNpcHomeState(
    short NpcSlot,
    short HomeTileX,
    short HomeTileY,
    byte Status)
{
    public bool TryGetStatus(out TerrariaNpcHomeStatus status)
    {
        status = Status switch
        {
            (byte)TerrariaNpcHomeStatus.None => TerrariaNpcHomeStatus.None,
            (byte)TerrariaNpcHomeStatus.Homeless => TerrariaNpcHomeStatus.Homeless,
            (byte)TerrariaNpcHomeStatus.HasRoom => TerrariaNpcHomeStatus.HasRoom,
            _ => default
        };
        return Status <= (byte)TerrariaNpcHomeStatus.HasRoom;
    }
}

public enum TerrariaNpcHomeDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

public enum TerrariaNpcHomeEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}

/// <summary>
/// Typed wire adapter for Terraria 1.4.5.8 packet 60. Multiplicity owns the seven-byte packet layout;
/// runtime housing authority and semantic status validation remain above this boundary.
/// </summary>
public static class TerrariaNpcHomeCodec
{
    public const int PayloadLength = 7;

    public static TerrariaNpcHomeDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaNpcHomeState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.UpdateNpcHome)
            return TerrariaNpcHomeDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaNpcHomeDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
            return TerrariaNpcHomeDecodeResult.Decoded;
        }

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        state = DecodePayload(scratch);
        return TerrariaNpcHomeDecodeResult.Decoded;
    }

    public static TerrariaNpcHomeEncodeResult TryEncode(in TerrariaNpcHomeState state, out byte[] frame)
    {
        frame = [];
        if (state.NpcSlot < 0 || !state.TryGetStatus(out _))
            return TerrariaNpcHomeEncodeResult.InvalidState;

        var packet = new UpdateNPCHome
        {
            NpcId = state.NpcSlot,
            HomeTileX = state.HomeTileX,
            HomeTileY = state.HomeTileY,
            Homeless = state.Status
        };

        return packet.TrySerialize(out frame)
            ? TerrariaNpcHomeEncodeResult.Encoded
            : TerrariaNpcHomeEncodeResult.Failed;
    }

    private static TerrariaNpcHomeState DecodePayload(ReadOnlySpan<byte> payload) =>
        new(
            NpcSlot: BinaryPrimitives.ReadInt16LittleEndian(payload[0..2]),
            HomeTileX: BinaryPrimitives.ReadInt16LittleEndian(payload[2..4]),
            HomeTileY: BinaryPrimitives.ReadInt16LittleEndian(payload[4..6]),
            Status: payload[6]);
}
