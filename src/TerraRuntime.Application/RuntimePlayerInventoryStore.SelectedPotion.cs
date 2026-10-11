using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // Preparation only. The existing complete remote phase adopts this exact paid
    // debit together with its source-owned use effects, clocks, buffs and RNG.
    internal bool TryPrepareSelectedPotionConsumption(ConnectionHandle connection, short slot,
        out RemotePhaseInventoryMutation mutation)
    {
        mutation = default;
        if (slot is < 0 or >= 58 || !TryGet(connection, slot, out var before) ||
            !before.IsCanonical || before.IsEmpty || before.Prefix.Value != 0 ||
            !VanillaSelectedConsumableCatalog1458.TryGet(before.ItemType, out var potion) ||
            potion.HealLife <= 0 && potion.HealMana <= 0 && potion.BuffType <= 0 ||
            fundedQuantities[GetOffset(connection.Player.Slot.Value, slot)] <= 0) return false;
        var after = before.Stack == 1 ? default : before with { Stack = (short)(before.Stack - 1) };
        mutation = new(connection, slot, before, after) { ConsumeFundedQuantity = true };
        return true;
    }
}
