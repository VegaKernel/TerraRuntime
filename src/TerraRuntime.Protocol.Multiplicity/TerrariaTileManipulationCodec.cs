using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Source-verified TerrariaServer 1.4.5.8 packet-17 action identities currently represented by TerraRuntime.
/// Wire identity and runtime authority are deliberately separate: an enum value may be source-known while the
/// authoritative runtime still fails it closed until the required held-item/tool and world-state semantics exist.
/// </summary>
public enum TerrariaTileManipulationAction : byte
{
    KillTile = 0,
    PlaceTile = 1,
    KillWall = 2,
    PlaceWall = 3,
    KillTileNoItem = 4
}

public readonly record struct TerrariaTileManipulationState(
    byte Action,
    short TileX,
    short TileY,
    short Data,
    byte Style)
{
    /// <summary>
    /// Resolves packet-17 actions that are currently admitted to the authoritative runtime path.
    /// Wall actions remain wire-known but fail closed here until TerraRuntime can prove the selected
    /// wall item / hammer capability and inventory transition rather than merely checking for a non-empty slot.
    /// </summary>
    public bool TryGetKnownAction(out TerrariaTileManipulationAction action)
    {
        action = Action switch
        {
            (byte)TerrariaTileManipulationAction.KillTile => TerrariaTileManipulationAction.KillTile,
            (byte)TerrariaTileManipulationAction.PlaceTile => TerrariaTileManipulationAction.PlaceTile,
            (byte)TerrariaTileManipulationAction.KillTileNoItem => TerrariaTileManipulationAction.KillTileNoItem,
            _ => default
        };

        return Action is
            (byte)TerrariaTileManipulationAction.KillTile or
            (byte)TerrariaTileManipulationAction.PlaceTile or
            (byte)TerrariaTileManipulationAction.KillTileNoItem;
    }
}

public enum TerrariaTileManipulationDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

public enum TerrariaTileManipulationEncodeResult : byte
{
    Encoded = 0,
    FrameTooLarge = 1,
    Failed = 2
}

/// <summary>
/// Wire-only adapter for Terraria 1.4.5.8 packet 17. The protocol contract is exactly eight payload bytes:
/// action byte, X/Y/data Int16 values and style byte. Action semantics and permission checks intentionally live
/// above this codec so decoding a client packet never grants authority to mutate the world.
/// </summary>
public static class TerrariaTileManipulationCodec
{
    public const int PayloadLength = 8;

    public static TerrariaTileManipulationDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaTileManipulationState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.TileManipulation)
            return TerrariaTileManipulationDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaTileManipulationDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
            return TerrariaTileManipulationDecodeResult.Decoded;
        }

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        state = DecodePayload(scratch);
        return TerrariaTileManipulationDecodeResult.Decoded;
    }

    public static TerrariaTileManipulationEncodeResult TryEncode(
        in TerrariaTileManipulationState state,
        out byte[] frame)
    {
        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = state.Action;
        BinaryPrimitives.WriteInt16LittleEndian(payload[1..3], state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(payload[3..5], state.TileY);
        BinaryPrimitives.WriteInt16LittleEndian(payload[5..7], state.Data);
        payload[7] = state.Style;

        var writer = new ArrayBufferWriter<byte>(PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.TileManipulation,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
        {
            frame = [];
            return TerrariaTileManipulationEncodeResult.FrameTooLarge;
        }
        if (result != TerrariaFrameWriteResult.Written)
        {
            frame = [];
            return TerrariaTileManipulationEncodeResult.Failed;
        }

        frame = writer.WrittenSpan.ToArray();
        return TerrariaTileManipulationEncodeResult.Encoded;
    }

    private static TerrariaTileManipulationState DecodePayload(ReadOnlySpan<byte> payload) =>
        new(
            Action: payload[0],
            TileX: BinaryPrimitives.ReadInt16LittleEndian(payload[1..3]),
            TileY: BinaryPrimitives.ReadInt16LittleEndian(payload[3..5]),
            Data: BinaryPrimitives.ReadInt16LittleEndian(payload[5..7]),
            Style: payload[7]);
}
