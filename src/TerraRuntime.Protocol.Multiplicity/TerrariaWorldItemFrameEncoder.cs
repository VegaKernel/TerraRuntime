using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaWorldItemFrameEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2
}

/// <summary>
/// Encodes authoritative live world-item mutations. Unlike bootstrap encoding, this API models packet 21
/// and packet 22 independently and supports the canonical packet-21 removal shape.
/// </summary>
public static class TerrariaWorldItemFrameEncoder
{
    public static TerrariaWorldItemFrameEncodeResult TryEncodeDrop(
        in TerrariaWorldItemDropState state,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (!state.IsValid || state.IsNewItemRequest || state.IsRemoval || state.ItemIndex >= 400)
            return TerrariaWorldItemFrameEncodeResult.InvalidState;

        var packet = new ItemDrop
        {
            ItemIndex = state.ItemIndex,
            PositionX = state.PositionX,
            PositionY = state.PositionY,
            VelocityX = state.VelocityX,
            VelocityY = state.VelocityY,
            Stack = state.Stack,
            Prefix = state.Prefix,
            ItemNetId = state.ItemNetId,
            Ownership = (NewItemOwnership)(byte)state.Ownership,
            Shimmered = state.Shimmered,
            ShimmerTime = state.ShimmerTime,
            EnemyGrabDelayTime = state.EnemyGrabDelayTime
        };

        return TrySerialize(packet, out frame);
    }

    public static TerrariaWorldItemFrameEncodeResult TryEncodeOwner(
        in TerrariaWorldItemOwnerState state,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (!state.IsValid)
            return TerrariaWorldItemFrameEncodeResult.InvalidState;

        var packet = new ItemOwner
        {
            ItemId = state.ItemIndex,
            PlayerId = state.OwnerPlayerId,
            TimeToKeepReservation = state.TimeToKeepReservation,
            GrabDelayPlayer = state.GrabDelayPlayer,
            GrabDelayTime = state.GrabDelayTime,
            PositionX = state.PositionX,
            PositionY = state.PositionY
        };

        return TrySerialize(packet, out frame);
    }

    public static TerrariaWorldItemFrameEncodeResult TryEncodeRemoval(
        short itemIndex,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (itemIndex < 0 || itemIndex >= 400)
            return TerrariaWorldItemFrameEncodeResult.InvalidState;

        var packet = new ItemDrop
        {
            ItemIndex = itemIndex,
            PositionX = 0f,
            PositionY = 0f,
            VelocityX = 0f,
            VelocityY = 0f,
            Stack = 0,
            Prefix = 0,
            ItemNetId = 0,
            Ownership = NewItemOwnership.None,
            Shimmered = false,
            ShimmerTime = 0f,
            EnemyGrabDelayTime = 0
        };

        return TrySerialize(packet, out frame);
    }

    private static TerrariaWorldItemFrameEncodeResult TrySerialize(
        TerrariaPacket packet,
        out ReadOnlyMemory<byte> frame)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        if (stream.Length < TerrariaFrameDecoderOptions.MinimumFrameLength || stream.Length > ushort.MaxValue)
        {
            frame = default;
            return TerrariaWorldItemFrameEncodeResult.FrameTooLarge;
        }

        frame = stream.ToArray();
        return TerrariaWorldItemFrameEncodeResult.Encoded;
    }
}
