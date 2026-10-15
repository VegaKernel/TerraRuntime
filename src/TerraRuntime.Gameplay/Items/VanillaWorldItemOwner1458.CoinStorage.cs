using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public static partial class VanillaWorldItemOwner1458
{
    // The caller has validated the full canonical inventory image. This detached
    // branch stages GetItem's coin-slot priority and exact-100 DoCoins conversions.
    // Its journal contains each changed slot once, with the original and final tuple;
    // it is not a claim about intermediate GetItem callbacks or wallet spending.
    private static bool TryPlanCoinStorage(in WorldItemPickupSlot1458 incoming,
        ReadOnlySpan<WorldItemPickupSlot1458> inventory, bool voidEnabled,
        out WorldItemStoragePlan1458? plan, bool noCoinMerge = false)
    {
        plan = null;
        var projected = inventory.ToArray();
        var touched = new bool[VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive];
        var order = new List<short>(VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive);
        var capturedIncoming = incoming;
        int remaining = incoming.Stack;
        int maximum = incoming.Type == VanillaCoinFacts.PlatinumCoin.Value
            ? VanillaCoinFacts.PlatinumMaximumStack : VanillaCoinFacts.LowerDenominationMaximumStack;

        void Set(short slot, WorldItemPickupSlot1458 value)
        {
            if (!touched[slot])
            {
                touched[slot] = true;
                order.Add(slot);
            }
            projected[slot] = value;
        }

        void ConvertExactHundred(short slot)
        {
            var current = projected[slot];
            if (current.Stack != VanillaCoinFacts.LowerDenominationMaximumStack ||
                current.Type is < 71 or > 73) return;

            // Original SetDefaults(next denomination) resets prefix and favorite.
            Set(slot, new(current.Type + 1, 1, 0, false));
            for (short destination = 0;
                destination < VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive; destination++)
            {
                var next = projected[destination];
                int nextMaximum = next.Type == VanillaCoinFacts.PlatinumCoin.Value
                    ? VanillaCoinFacts.PlatinumMaximumStack : VanillaCoinFacts.LowerDenominationMaximumStack;
                if (destination == slot || next.Type != current.Type + 1 || next.Stack >= nextMaximum) continue;
                Set(destination, next with { Stack = next.Stack + 1 });
                Set(slot, default);
                ConvertExactHundred(destination);
                // The original loop continues with the converted slot now Air.
                // Canonical Air has maxStack 0, so no later slot can accept it.
                return;
            }
        }

        void Merge(short slot)
        {
            if (remaining == 0) return;
            var before = projected[slot];
            if (before.Type != capturedIncoming.Type || before.Prefix != capturedIncoming.Prefix || before.Stack >= maximum) return;
            int amount = Math.Min(remaining, maximum - before.Stack);
            Set(slot, before with { Stack = before.Stack + amount });
            remaining -= amount;
            if (!noCoinMerge) ConvertExactHundred(slot);
        }

        for (short slot = VanillaPlayerItemSlotCatalog.CoinSlotStart;
            slot < VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive && remaining > 0; slot++) Merge(slot);
        for (short slot = 0;
            slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive && remaining > 0; slot++) Merge(slot);

        if (remaining > 0)
        {
            int start = incoming.Favorite ? 0 : VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive - 1;
            int step = incoming.Favorite ? 1 : -1;
            for (int slot = start;
                (uint)slot < VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive; slot += step)
            {
                if (projected[slot].Type != 0) continue;
                Set((short)slot, incoming with { Stack = remaining });
                remaining = 0;
                if (!noCoinMerge) ConvertExactHundred((short)slot);
                break;
            }
        }
        if (remaining > 0 && voidEnabled) return false;

        var residual = remaining == 0 ? default : incoming with { Stack = remaining };
        // Canonical stack limits bound all copper-unit products and their sum well
        // below Int64.MaxValue. Identity changes must conserve value, not raw count.
        if (CoinValue(inventory) + CoinValue(in incoming) !=
            CoinValue(projected) + CoinValue(in residual)) return false;
        var mutations = new List<WorldItemStorageMutation1458>(order.Count);
        foreach (short slot in order)
            if (inventory[slot] != projected[slot])
                mutations.Add(new(slot, inventory[slot], projected[slot]));
        plan = new(residual, incoming.Stack - remaining, mutations.ToArray());
        return true;
    }

    private static long CoinValue(ReadOnlySpan<WorldItemPickupSlot1458> items)
    {
        long result = 0;
        foreach (ref readonly var item in items) result += CoinValue(in item);
        return result;
    }

    private static long CoinValue(in WorldItemPickupSlot1458 item) => item.Type switch
    {
        71 => item.Stack * VanillaCoinFacts.CopperValue,
        72 => item.Stack * VanillaCoinFacts.SilverValue,
        73 => item.Stack * VanillaCoinFacts.GoldValue,
        74 => item.Stack * VanillaCoinFacts.PlatinumValue,
        _ => 0
    };
}
