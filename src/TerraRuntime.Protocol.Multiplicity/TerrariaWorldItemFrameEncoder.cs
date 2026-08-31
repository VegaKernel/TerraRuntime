using System.Buffers.Binary;
using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaWorldItemFrameEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2
}

/// <summary>
/// Encodes authoritative live world-item mutations. Packet 90 intentionally reuses the packet-21 payload shape in
/// TerrariaServer 1.4.5.8; packet 151 carries only the leased item slot that becomes reusable again.
/// </summary>
public static class TerrariaWorldItemFrameEncoder
{
    private const byte ItemDropMessageId = 21;
    private const byte InstancedItemMessageId = 90;
    private const byte InstancedItemSlotReleaseMessageId = 151;

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

    /// <summary>
    /// Encodes Terraria message 90. Its payload is byte-for-byte packet 21 in the pinned server; only the message id
    /// differs. Encoding through the proven packet-21 serializer keeps optional shimmer/enemy-grab bits identical.
    /// </summary>
    public static TerrariaWorldItemFrameEncodeResult TryEncodeInstancedDrop(
        in TerrariaWorldItemDropState state,
        out ReadOnlyMemory<byte> frame)
    {
        TerrariaWorldItemFrameEncodeResult result = TryEncodeDrop(in state, out ReadOnlyMemory<byte> packet21);
        if (result != TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            frame = default;
            return result;
        }

        byte[] encoded = packet21.ToArray();
        if (encoded.Length < 3 || encoded[2] != ItemDropMessageId)
        {
            frame = default;
            return TerrariaWorldItemFrameEncodeResult.InvalidState;
        }

        encoded[2] = InstancedItemMessageId;
        frame = encoded;
        return TerrariaWorldItemFrameEncodeResult.Encoded;
    }

    /// <summary>Encodes server message 151 emitted exactly when an instanced item's slot lease reaches zero.</summary>
    public static TerrariaWorldItemFrameEncodeResult TryEncodeInstancedSlotRelease(
        short itemIndex,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (itemIndex < 0 || itemIndex >= 400)
            return TerrariaWorldItemFrameEncodeResult.InvalidState;

        byte[] encoded = new byte[5];
        BinaryPrimitives.WriteUInt16LittleEndian(encoded, checked((ushort)encoded.Length));
        encoded[2] = InstancedItemSlotReleaseMessageId;
        BinaryPrimitives.WriteInt16LittleEndian(encoded.AsSpan(3), itemIndex);
        frame = encoded;
        return TerrariaWorldItemFrameEncodeResult.Encoded;
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
        if (!MultiplicityPacketSerializer.TrySerialize(packet, out byte[] encoded))
        {
            frame = default;
            return TerrariaWorldItemFrameEncodeResult.FrameTooLarge;
        }

        frame = encoded;
        return TerrariaWorldItemFrameEncodeResult.Encoded;
    }
}
