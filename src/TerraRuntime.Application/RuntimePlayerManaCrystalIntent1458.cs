using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// Exact pre-report selected item and mana epoch, retained in the existing ItemPhase.
// Reports acknowledge the bounded owned use; they cannot grant maximum mana or debit twice.
internal readonly record struct RuntimePlayerManaCrystalIntent1458(
    PlayerHandle Player, byte Slot, RuntimePlayerInventoryItem Item, int BaseMaximum,
    int Mana, int DerivedMaximum, PlayerSelectedConsumableState1458 Clocks,
    bool Completed, bool InventoryAcknowledged, bool ManaAcknowledged)
{
    internal RuntimePlayerInventoryItem After => BaseMaximum >= 200 ? Item :
        Item.Stack == 1 ? default : Item with { Stack = (short)(Item.Stack - 1) };
}
