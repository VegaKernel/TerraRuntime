using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaChestOpenRequest(short TileX, short TileY);

public readonly record struct TerrariaChestItemState(
    short ChestId,
    byte ItemSlot,
    short Stack,
    byte Prefix,
    short ItemNetId)
{
    public bool IsEmpty => Stack <= 0;
}

public readonly record struct TerrariaActiveChestState(
    short ChestId,
    short ChestX,
    short ChestY,
    byte NameLength,
    string ChestName);

public enum TerrariaChestDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}

/// <summary>
/// Terraria 1.4.5.8 / protocol-326 chest adapter. Multiplicity owns the exact packet layouts;
/// TerraRuntime only projects immutable values across the network/authority boundary.
/// </summary>
public static class TerrariaChestCodec
{
    private const int RequestOpenPayloadLength = 4;
    private const int ChestItemPayloadLength = 8;
    private const int MinimumActiveChestPayloadLength = 7;
    private const int MaximumActiveChestPayloadLength = 96;

    public static TerrariaChestDecodeResult TryDecodeOpenRequest(
        in TerrariaFrame frame,
        out TerrariaChestOpenRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.RequestChestOpen)
            return TerrariaChestDecodeResult.WrongMessageId;
        if (frame.Payload.Length != RequestOpenPayloadLength)
            return TerrariaChestDecodeResult.InvalidPayloadLength;

        if (!TryDeserialize(in frame, out TerrariaPacket packet) || packet is not ChestGetContents chest)
            return TerrariaChestDecodeResult.Malformed;

        request = new TerrariaChestOpenRequest(chest.TileX, chest.TileY);
        return TerrariaChestDecodeResult.Decoded;
    }

    public static TerrariaChestDecodeResult TryDecodeItem(
        in TerrariaFrame frame,
        out TerrariaChestItemState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SyncChestItem)
            return TerrariaChestDecodeResult.WrongMessageId;
        if (frame.Payload.Length != ChestItemPayloadLength)
            return TerrariaChestDecodeResult.InvalidPayloadLength;

        if (!TryDeserialize(in frame, out TerrariaPacket packet) || packet is not ChestItem item)
            return TerrariaChestDecodeResult.Malformed;

        state = new TerrariaChestItemState(
            item.ChestId,
            item.ItemSlot,
            item.Stack,
            item.Prefix,
            item.ItemNetId);
        return TerrariaChestDecodeResult.Decoded;
    }

    public static TerrariaChestDecodeResult TryDecodeActiveChest(
        in TerrariaFrame frame,
        out TerrariaActiveChestState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SyncPlayerChest)
            return TerrariaChestDecodeResult.WrongMessageId;
        if (frame.Payload.Length < MinimumActiveChestPayloadLength ||
            frame.Payload.Length > MaximumActiveChestPayloadLength)
        {
            return TerrariaChestDecodeResult.InvalidPayloadLength;
        }

        if (!TryDeserialize(in frame, out TerrariaPacket packet) || packet is not ChestOpen chest)
            return TerrariaChestDecodeResult.Malformed;

        state = new TerrariaActiveChestState(
            chest.ChestId,
            chest.ChestX,
            chest.ChestY,
            chest.NameLength,
            chest.ChestName ?? string.Empty);
        return TerrariaChestDecodeResult.Decoded;
    }

    public static byte[] EncodeActiveChest(
        short chestId,
        short chestX,
        short chestY,
        string? chestName = null)
    {
        var packet = new ChestOpen
        {
            ChestId = chestId,
            ChestX = chestX,
            ChestY = chestY,
            ChestName = chestName ?? string.Empty
        };
        return Serialize(packet);
    }

    public static byte[] EncodeChestItem(in TerrariaChestItemState state)
    {
        var packet = new ChestItem
        {
            ChestId = state.ChestId,
            ItemSlot = state.ItemSlot,
            Stack = state.Stack,
            Prefix = state.Prefix,
            ItemNetId = state.ItemNetId
        };
        return Serialize(packet);
    }

    public static byte[] EncodePlayerChestIndex(byte playerSlot, short chestId) =>
        Serialize(new SyncPlayerChestIndex
        {
            Player = playerSlot,
            Chest = chestId
        });

    private static bool TryDeserialize(in TerrariaFrame frame, out TerrariaPacket packet)
    {
        int length = checked((int)frame.Payload.Length);
        ReadOnlyMemory<byte> payload;
        if (frame.Payload.IsSingleSegment)
        {
            payload = frame.Payload.First;
        }
        else
        {
            byte[] buffer = GC.AllocateUninitializedArray<byte>(length);
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(buffer.AsSpan(offset));
                offset += segment.Length;
            }
            payload = buffer;
        }

        return TerrariaPacket.TryDeserializePayload(frame.MessageId, payload, out packet);
    }

    private static byte[] Serialize(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        if (stream.Length < TerrariaFrameDecoderOptions.MinimumFrameLength || stream.Length > ushort.MaxValue)
            throw new InvalidOperationException("Encoded chest frame length is outside the Terraria frame envelope.");
        return stream.ToArray();
    }
}
