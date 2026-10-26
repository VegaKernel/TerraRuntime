using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerToolAttemptPreparation
{
    internal bool TryStageWiringResult(in ToolClockState1458 clocks, VanillaUnifiedRandom1458 localRandom,
        short resourceSlot = -1, RuntimePlayerInventoryItem? inventoryAfter = null)
    {
        if (Before.SourceWiring is not { } definition || definition.Item != capture.Selected.ItemType ||
            resourceSlot is < 0 or >= 58 || inventoryAfter is null || !inventoryAfter.Value.IsCanonical ||
            !TryStageSourceResult(in clocks, Before.Hits, true, localRandom) || next is null) return false;
        next = next with { PlacementReceipt = null, WiringReceipt =
            new(capture.Connection, resourceSlot, capture.InventoryBefore[resourceSlot], inventoryAfter.Value, next.ActionSerial) };
        return true;
    }

    internal bool TryFinalizeWiringInventory(short resourceSlot, in RuntimePlayerInventoryItem inventoryAfter)
    {
        if (!adopted || accepted is null || next?.WiringReceipt is not { } receipt ||
            receipt.Slot != resourceSlot || receipt.After != inventoryAfter ||
            !owner.TryCaptureAcceptedWiringInventory(capture, accepted, resourceSlot, inventoryAfter, out var refreshed) ||
            refreshed is null) return false;
        accepted = refreshed;
        return true;
    }
}
