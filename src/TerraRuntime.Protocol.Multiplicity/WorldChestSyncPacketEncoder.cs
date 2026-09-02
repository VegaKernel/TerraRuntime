using global::Multiplicity.Packets;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldChestSyncPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidChest = 1,
    InvalidItem = 2,
    FrameTooLarge = 3
}

/// <summary>
/// Encodes the packet sequence emitted by TerrariaServer 1.4.5.8 SendChestContentsTo:
/// packet 155 with the chest size followed by packet 32 for every chest slot, including empty slots.
/// </summary>
public static class WorldChestSyncPacketEncoder
{
    private const int MaximumAddressableSlots = byte.MaxValue + 1;

    public static WorldChestSyncPacketEncodeResult TryEncode(
        WorldChest chest,
        out ReadOnlyMemory<byte>[] frames)
    {
        ArgumentNullException.ThrowIfNull(chest);
        frames = [];

        if (chest.SlotId < 0 ||
            chest.SlotId >= VanillaWorldFormat326.MaximumChestSlots ||
            chest.Items is null ||
            chest.Items.Length > MaximumAddressableSlots)
        {
            return WorldChestSyncPacketEncodeResult.InvalidChest;
        }

        var result = new ReadOnlyMemory<byte>[checked(chest.Items.Length + 1)];
        var sizePacket = new SyncChestSize
        {
            ChestId = chest.SlotId,
            NewSize = checked((short)chest.Items.Length)
        };

        if (!TrySerialize(sizePacket, out result[0]))
            return WorldChestSyncPacketEncodeResult.FrameTooLarge;

        for (int i = 0; i < chest.Items.Length; i++)
        {
            WorldChestItem item = chest.Items[i];
            short stack;
            short itemNetId;
            byte prefix;

            if (item.IsEmpty)
            {
                stack = 0;
                itemNetId = 0;
                prefix = 0;
            }
            else
            {
                if (item.Stack > short.MaxValue ||
                    !item.TryGetItemType(out var itemType) ||
                    itemType.IsNone ||
                    itemType.Value > short.MaxValue)
                {
                    return WorldChestSyncPacketEncodeResult.InvalidItem;
                }

                stack = checked((short)item.Stack);
                itemNetId = checked((short)itemType.Value);
                prefix = item.Prefix;
            }

            var itemPacket = new ChestItem
            {
                ChestId = chest.SlotId,
                ItemSlot = checked((byte)i),
                Stack = stack,
                Prefix = prefix,
                ItemNetId = itemNetId
            };

            if (!TrySerialize(itemPacket, out result[i + 1]))
                return WorldChestSyncPacketEncodeResult.FrameTooLarge;
        }

        frames = result;
        return WorldChestSyncPacketEncodeResult.Encoded;
    }

    private static bool TrySerialize(TerrariaPacket packet, out ReadOnlyMemory<byte> frame)
    {
        if (!packet.TrySerialize(out byte[] encoded))
        {
            frame = default;
            return false;
        }

        frame = encoded;
        return true;
    }
}
