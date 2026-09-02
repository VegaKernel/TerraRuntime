using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core;

public sealed partial class RuntimeServerPlayerStateStore
{
    public bool TrySetItem(
        PlayerHandle player,
        in ServerPlayerItemState item,
        out ServerPlayerItemState normalized)
    {
        normalized = default;
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue ||
            !TryNormalizeItem(player.Slot, in item, out normalized))
        {
            return false;
        }

        state.Revision++;
        if (normalized.IsEmpty)
            state.Items?.Remove(normalized.Slot);
        else
            (state.Items ??= [])[normalized.Slot] = normalized;
        return true;
    }

    public bool TryGetItem(
        PlayerHandle player,
        short slot,
        out ServerPlayerItemState item)
    {
        if (!VanillaPlayerItemSlotCatalog.IsValid(slot) ||
            !TryGetState(player, out ServerPlayerRuntimeState? state))
        {
            item = default;
            return false;
        }

        if (state.Items is not null && state.Items.TryGetValue(slot, out item))
            return true;

        item = new ServerPlayerItemState(slot, VanillaItemIds.None, 0, default, 0);
        return true;
    }

    private static bool TryNormalizeItem(
        PlayerSlotId playerSlot,
        in ServerPlayerItemState item,
        out ServerPlayerItemState normalized)
    {
        if (!VanillaPlayerItemSlotCatalog.IsValid(item.Slot))
        {
            normalized = default;
            return false;
        }

        if (item.IsEmpty)
        {
            if (!item.ItemType.IsNone ||
                item.Stack != 0 ||
                item.Prefix != VanillaPrefixIds.None ||
                item.ItemFlags != 0)
            {
                normalized = default;
                return false;
            }

            normalized = new ServerPlayerItemState(item.Slot, VanillaItemIds.None, 0, default, 0);
            return true;
        }

        if (item.Stack <= 0 ||
            !VanillaPrefixIds.TryCreate(item.Prefix.Value, out PrefixId canonicalPrefix) ||
            canonicalPrefix != item.Prefix ||
            !VanillaItemIds.TryCreate(item.ItemType.Value, out ItemTypeId canonical) ||
            canonical != item.ItemType)
        {
            normalized = default;
            return false;
        }

        var request = new PlayerEquipmentCommitRequest(
            playerSlot,
            item.Slot,
            item.Stack,
            checked((byte)item.Prefix.Value),
            checked((short)item.ItemType.Value),
            item.ItemFlags);
        PlayerEquipmentCommitRequest commit = VanillaPlayerItemNormalizer.Normalize(in request);
        if (!commit.TryGetCanonicalItemType(out ItemTypeId itemType) || itemType.IsNone)
        {
            normalized = default;
            return false;
        }

        normalized = new ServerPlayerItemState(
            commit.SlotId,
            itemType,
            commit.Stack,
            commit.PrefixId,
            commit.ItemFlags);
        return true;
    }
}
