using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldItemBootstrapPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidItemState = 1,
    FrameTooLarge = 2
}

/// <summary>
/// Encodes the vanilla active-world-item bootstrap pair: packet 21 followed by packet 22 for each item.
/// The runtime owns item identity/state; Multiplicity remains the only owner of the wire layout.
/// </summary>
public static class WorldItemBootstrapPacketEncoder
{
    public const int FramesPerItem = 2;

    public static WorldItemBootstrapPacketEncodeResult TryEncode(
        ReadOnlySpan<WorldItemSnapshot> items,
        out ReadOnlyMemory<byte>[] frames)
    {
        var encoded = new ReadOnlyMemory<byte>[checked(items.Length * FramesPerItem)];
        int frameIndex = 0;

        for (int i = 0; i < items.Length; i++)
        {
            WorldItemSnapshot item = items[i];
            if (!IsValid(in item))
            {
                frames = [];
                return WorldItemBootstrapPacketEncodeResult.InvalidItemState;
            }

            var drop = new ItemDrop
            {
                ItemIndex = item.Handle.Slot,
                PositionX = item.PositionX,
                PositionY = item.PositionY,
                VelocityX = item.VelocityX,
                VelocityY = item.VelocityY,
                Stack = item.Stack,
                Prefix = item.Prefix,
                ItemNetId = item.ItemNetId,
                Ownership = (NewItemOwnership)item.Ownership,
                Shimmered = item.Shimmered,
                ShimmerTime = item.ShimmerTime,
                EnemyGrabDelayTime = item.EnemyGrabDelayTime
            };

            var owner = new ItemOwner
            {
                ItemId = item.Handle.Slot,
                PlayerId = item.OwnerPlayerId,
                TimeToKeepReservation = item.TimeToKeepReservation,
                GrabDelayPlayer = item.GrabDelayPlayer,
                GrabDelayTime = item.GrabDelayTime,
                PositionX = item.PositionX,
                PositionY = item.PositionY
            };

            if (!TrySerialize(drop, out ReadOnlyMemory<byte> dropFrame) ||
                !TrySerialize(owner, out ReadOnlyMemory<byte> ownerFrame))
            {
                frames = [];
                return WorldItemBootstrapPacketEncodeResult.FrameTooLarge;
            }

            encoded[frameIndex++] = dropFrame;
            encoded[frameIndex++] = ownerFrame;
        }

        frames = encoded;
        return WorldItemBootstrapPacketEncodeResult.Encoded;
    }

    private static bool IsValid(in WorldItemSnapshot item) =>
        item.IsActive &&
        item.Handle.Slot >= 0 &&
        float.IsFinite(item.PositionX) &&
        float.IsFinite(item.PositionY) &&
        float.IsFinite(item.VelocityX) &&
        float.IsFinite(item.VelocityY) &&
        float.IsFinite(item.ShimmerTime) &&
        (byte)item.Ownership <= (byte)WorldItemOwnershipMode.GrabDelayForAllPlayers;

    private static bool TrySerialize(TerrariaPacket packet, out ReadOnlyMemory<byte> frame)
    {
        if (!MultiplicityPacketSerializer.TrySerialize(packet, out byte[] encoded))
        {
            frame = default;
            return false;
        }

        frame = encoded;
        return true;
    }
}
