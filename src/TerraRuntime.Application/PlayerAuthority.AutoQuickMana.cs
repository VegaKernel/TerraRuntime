using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // Source QuickMana selects the first healMana item before TryStartUse. The complete
    // original SetDefaults audit has exactly the catalog's four mana-potion identities;
    // this transaction admits those four PrefixNone identities in source slot order.
    private static bool TryStageAutoQuickMana(ConnectionHandle connection, ulong attempt,
        int mana, int maximum, int manaPotionDelay, ReadOnlySpan<RuntimePlayerInventoryItem> items,
        PlayerBuffState buffs, out int after, out RuntimePlayerMagicRefill1458? refill)
    {
        after = mana; refill = null;
        if (mana < 0 || maximum is < 0 or > 400 || manaPotionDelay < 0 || items.Length != 59) return false;
        if (manaPotionDelay > 0) return true;
        bool voidBag = false;
        for (short slot = 0; slot < 58; slot++)
        {
            var item = items[slot];
            if (!item.IsCanonical) return false;
            if (item.IsEmpty) continue;
            voidBag |= item.ItemType.Value == 4131;
            if (!VanillaSelectedConsumableCatalog1458.TryGet(item.ItemType, out var potion) || potion.HealMana <= 0)
                continue;
            if (item.ItemType.Value is not (110 or 189 or 500 or 2209) || item.Prefix.Value != 0 || attempt == 0) return false;
            if (buffs.GetLastActiveDuration(new(94)) > int.MaxValue - 300) return false;
            // A full debuff table may drop the buff without dropping the source restoration.
            _ = buffs.TryApplySelectedConsumable(new(94), 300, false);
            after = (int)Math.Min((long)mana + potion.HealMana, maximum);
            var next = item.Stack == 1 ? default : item with { Stack = (short)(item.Stack - 1) };
            refill = new(connection, attempt, slot, item, next);
            return true;
        }
        // Unrepresented Bank4 cannot be assumed empty if the ordinary source can open it.
        return !voidBag;
    }
}
