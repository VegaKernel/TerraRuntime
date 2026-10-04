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
/// TerrariaServer 1.4.5.8; packet 151 removes an ordinary item or releases an expired instanced item slot.
/// </summary>
public static class TerrariaWorldItemFrameEncoder
{
    private const byte ItemDropMessageId = 21;
    private const byte InstancedItemMessageId = 90;
    private const byte ItemRemoveMessageId = 151;

    public static TerrariaWorldItemFrameEncodeResult TryEncodeDrop(
        in TerrariaWorldItemDropState state,
        out ReadOnlyMemory<byte> frame)
        => EncodeDrop(in state, allowSentinel: false, out frame);

    public static TerrariaWorldItemFrameEncodeResult TryEncodeSentinelDrop(in TerrariaWorldItemDropState state, out ReadOnlyMemory<byte> frame)
        => state.ItemIndex == 400 ? EncodeDrop(in state, allowSentinel: true, out frame) : Reject(out frame);

    private static TerrariaWorldItemFrameEncodeResult EncodeDrop(in TerrariaWorldItemDropState state, bool allowSentinel, out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (!state.IsValid || state.IsRemoval || (!allowSentinel && state.IsNewItemRequest))
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

    /// <summary>NetMessage.SendData(21) changes empty items to message 151; lease expiry uses the same frame.</summary>
    public static TerrariaWorldItemFrameEncodeResult TryEncodeRemoval(
        short itemIndex,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (itemIndex < 0 || itemIndex >= 400)
            return TerrariaWorldItemFrameEncodeResult.InvalidState;

        byte[] encoded = new byte[5];
        BinaryPrimitives.WriteUInt16LittleEndian(encoded, checked((ushort)encoded.Length));
        encoded[2] = ItemRemoveMessageId;
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

    public static TerrariaWorldItemFrameEncodeResult TryEncodeSentinelOwner(in TerrariaWorldItemOwnerState state, out ReadOnlyMemory<byte> frame)
    {
        if (state.ItemIndex != 400 || !(state with { ItemIndex = 0 }).IsValid) return Reject(out frame);
        var packet = new ItemOwner { ItemId = 400, PlayerId = state.OwnerPlayerId, TimeToKeepReservation = state.TimeToKeepReservation,
            GrabDelayPlayer = state.GrabDelayPlayer, GrabDelayTime = state.GrabDelayTime, PositionX = state.PositionX, PositionY = state.PositionY };
        return TrySerialize(packet, out frame);
    }

    private static TerrariaWorldItemFrameEncodeResult Reject(out ReadOnlyMemory<byte> frame)
    { frame = default; return TerrariaWorldItemFrameEncodeResult.InvalidState; }

    /// <summary>Source WorldItem.FindOwner requests an authenticated owner's release with packet39.</summary>
    public static TerrariaWorldItemFrameEncodeResult TryEncodeOwnershipReleaseRequest(short slot, out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if ((uint)slot >= 400) return TerrariaWorldItemFrameEncodeResult.InvalidState;
        byte[] bytes = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 6);
        bytes[2] = 39;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), slot);
        // The server sends false; the owner client responds after forcing its local item to server ownership.
        frame = bytes; return TerrariaWorldItemFrameEncodeResult.Encoded;
    }

    private static TerrariaWorldItemFrameEncodeResult TrySerialize(
        TerrariaPacket packet,
        out ReadOnlyMemory<byte> frame)
    {
        if (!packet.TrySerialize(out byte[] encoded))
        {
            frame = default;
            return TerrariaWorldItemFrameEncodeResult.FrameTooLarge;
        }

        frame = encoded;
        return TerrariaWorldItemFrameEncodeResult.Encoded;
    }
}
