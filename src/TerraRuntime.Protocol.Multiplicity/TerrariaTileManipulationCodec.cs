using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Source-verified TerrariaServer 1.4.5.8 packet-17 action identities represented by TerraRuntime.
/// This enum describes wire identity only. Runtime authority is owned by the gameplay/runtime layer.
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
    /// Resolves source-known packet-17 wire action identities. A successful result says only that the action byte
    /// is part of the pinned TerrariaServer 1.4.5.8 protocol contract; it does not grant authority to mutate state.
    /// </summary>
    public bool TryGetWireAction(out TerrariaTileManipulationAction action)
    {
        action = Action switch
        {
            (byte)TerrariaTileManipulationAction.KillTile => TerrariaTileManipulationAction.KillTile,
            (byte)TerrariaTileManipulationAction.PlaceTile => TerrariaTileManipulationAction.PlaceTile,
            (byte)TerrariaTileManipulationAction.KillWall => TerrariaTileManipulationAction.KillWall,
            (byte)TerrariaTileManipulationAction.PlaceWall => TerrariaTileManipulationAction.PlaceWall,
            (byte)TerrariaTileManipulationAction.KillTileNoItem => TerrariaTileManipulationAction.KillTileNoItem,
            _ => default
        };

        return Action <= (byte)TerrariaTileManipulationAction.KillTileNoItem;
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
/// Wire-only adapter for Terraria 1.4.5.8 packet 17. Multiplicity owns the exact eight-byte payload layout;
/// action semantics and permission checks intentionally live above this codec.
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
        var packet = new Tile
        {
            Action = (TileAction)state.Action,
            TileX = state.TileX,
            TileY = state.TileY,
            Value = state.Data,
            Style = state.Style
        };

        frame = packet.ToArray();
        return TerrariaTileManipulationEncodeResult.Encoded;
    }

    private static TerrariaTileManipulationState DecodePayload(ReadOnlySpan<byte> payload)
    {
        TileView packet = TileView.FromPayload(payload);
        return new TerrariaTileManipulationState(
            Action: (byte)packet.Action,
            TileX: packet.TileX,
            TileY: packet.TileY,
            Data: packet.Value,
            Style: packet.Style);
    }
}
