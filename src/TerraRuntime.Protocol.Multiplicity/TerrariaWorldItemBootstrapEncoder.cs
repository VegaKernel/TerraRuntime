using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaWorldItemBootstrapEncodeResult : byte
{
    Encoded = 0,
    InvalidItemIndex = 1,
    InvalidStack = 2,
    InvalidItemNetId = 3,
    InvalidPosition = 4,
    InvalidVelocity = 5,
    InvalidShimmerTime = 6,
    InvalidReservationTime = 7,
    InvalidGrabDelayTime = 8,
    InvalidOwnership = 9,
    FrameTooLarge = 10
}

/// <summary>
/// Encodes the vanilla bootstrap pair for one active dropped item: packet 21 (ItemDrop)
/// followed by packet 22 (ItemOwner). Runtime item storage remains a TerraRuntime concern.
/// </summary>
public static class TerrariaWorldItemBootstrapEncoder
{
    public static TerrariaWorldItemBootstrapEncodeResult TryEncode(
        in TerrariaWorldItemState state,
        out ReadOnlyMemory<byte> itemFrame,
        out ReadOnlyMemory<byte> ownerFrame)
    {
        itemFrame = default;
        ownerFrame = default;

        if (state.ItemIndex < 0)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidItemIndex;
        if (state.Stack <= 0)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidStack;
        if (state.ItemNetId <= 0)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidItemNetId;
        if (!float.IsFinite(state.PositionX) || !float.IsFinite(state.PositionY))
            return TerrariaWorldItemBootstrapEncodeResult.InvalidPosition;
        if (!float.IsFinite(state.VelocityX) || !float.IsFinite(state.VelocityY))
            return TerrariaWorldItemBootstrapEncodeResult.InvalidVelocity;
        if (!float.IsFinite(state.ShimmerTime) || state.ShimmerTime < 0f)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidShimmerTime;
        if (state.TimeToKeepReservation < 0)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidReservationTime;
        if (state.GrabDelayTime < 0)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidGrabDelayTime;
        if ((byte)state.Ownership > (byte)TerrariaWorldItemOwnership.GrabDelayForAllPlayers)
            return TerrariaWorldItemBootstrapEncodeResult.InvalidOwnership;

        var item = new ItemDrop
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

        var owner = new ItemOwner
        {
            ItemId = state.ItemIndex,
            PlayerId = state.OwnerPlayerId,
            TimeToKeepReservation = state.TimeToKeepReservation,
            GrabDelayPlayer = state.GrabDelayPlayer,
            GrabDelayTime = state.GrabDelayTime,
            PositionX = state.PositionX,
            PositionY = state.PositionY
        };

        if (!TrySerialize(item, out itemFrame) || !TrySerialize(owner, out ownerFrame))
        {
            itemFrame = default;
            ownerFrame = default;
            return TerrariaWorldItemBootstrapEncodeResult.FrameTooLarge;
        }

        return TerrariaWorldItemBootstrapEncodeResult.Encoded;
    }

    private static bool TrySerialize(TerrariaPacket packet, out ReadOnlyMemory<byte> frame)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        if (stream.Length < TerrariaFrameDecoderOptions.MinimumFrameLength || stream.Length > ushort.MaxValue)
        {
            frame = default;
            return false;
        }

        frame = stream.ToArray();
        return true;
    }
}
