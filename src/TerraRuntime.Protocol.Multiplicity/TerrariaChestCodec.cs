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

public readonly record struct TerrariaChestNameLookupRequest(
    short ChestId,
    short ChestX,
    short ChestY);

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
    private const int ChestNameLookupPayloadLength = 6;

    public static TerrariaChestDecodeResult TryDecodeOpenRequest(
        in TerrariaFrame frame,
        out TerrariaChestOpenRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.RequestChestOpen)
            return TerrariaChestDecodeResult.WrongMessageId;
        if (frame.Payload.Length != RequestOpenPayloadLength)
            return TerrariaChestDecodeResult.InvalidPayloadLength;

        if (!TerrariaPacket.TryDeserializePayload(frame.MessageId, frame.Payload, out TerrariaPacket packet) ||
            packet is not ChestGetContents chest)
        {
            return TerrariaChestDecodeResult.Malformed;
        }

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

        if (!TerrariaPacket.TryDeserializePayload(frame.MessageId, frame.Payload, out TerrariaPacket packet) ||
            packet is not ChestItem item)
        {
            return TerrariaChestDecodeResult.Malformed;
        }

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

        if (!TerrariaPacket.TryDeserializePayload(frame.MessageId, frame.Payload, out TerrariaPacket packet) ||
            packet is not ChestOpen chest)
        {
            return TerrariaChestDecodeResult.Malformed;
        }

        state = new TerrariaActiveChestState(
            chest.ChestId,
            chest.ChestX,
            chest.ChestY,
            chest.NameLength,
            chest.ChestName ?? string.Empty);
        return TerrariaChestDecodeResult.Decoded;
    }

    public static TerrariaChestDecodeResult TryDecodeNameLookup(
        in TerrariaFrame frame,
        out TerrariaChestNameLookupRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.ChestName)
            return TerrariaChestDecodeResult.WrongMessageId;
        if (frame.Payload.Length != ChestNameLookupPayloadLength)
            return TerrariaChestDecodeResult.InvalidPayloadLength;

        if (!TerrariaPacket.TryDeserializePayload(frame.MessageId, frame.Payload, out TerrariaPacket packet) ||
            packet is not ChestName chest ||
            chest.HasName)
        {
            return TerrariaChestDecodeResult.Malformed;
        }

        request = new TerrariaChestNameLookupRequest(
            chest.ChestId,
            chest.ChestX,
            chest.ChestY);
        return TerrariaChestDecodeResult.Decoded;
    }

    public static byte[] EncodeActiveChest(
        short chestId,
        short chestX,
        short chestY,
        string? chestName = null) =>
        (new ChestOpen
        {
            ChestId = chestId,
            ChestX = chestX,
            ChestY = chestY,
            ChestName = chestName ?? string.Empty
        }).ToArray();

    public static byte[] EncodeChestItem(in TerrariaChestItemState state) =>
        (new ChestItem
        {
            ChestId = state.ChestId,
            ItemSlot = state.ItemSlot,
            Stack = state.Stack,
            Prefix = state.Prefix,
            ItemNetId = state.ItemNetId
        }).ToArray();

    public static byte[] EncodePlayerChestIndex(byte playerSlot, short chestId) =>
        (new SyncPlayerChestIndex
        {
            Player = playerSlot,
            Chest = chestId
        }).ToArray();

    public static byte[] EncodeChestName(short chestId, short chestX, short chestY, string name) =>
        (new ChestName
        {
            ChestId = chestId,
            ChestX = chestX,
            ChestY = chestY,
            HasName = true,
            Name = name ?? string.Empty
        }).ToArray();
}
