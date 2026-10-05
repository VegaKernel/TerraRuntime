using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private RuntimeItemOwnerPlayerCapture1458[]? plannedSpecificPlayers;
    private bool? plannedSpecificLowTiles;
    private ulong plannedSpecificInventorySerial;

    private bool? CaptureSpecificLowTiles() => npcSpecificLowTiles is not null
        ? npcSpecificLowTiles() : skyblockLowTiles ? null : false;

    private bool TryCaptureSpecificLootContext(in NpcSnapshot npc, out VanillaNpcLootContext context)
    {
        bool? lowTiles = CaptureSpecificLowTiles();
        bool? hasSickle = null;
        if (lowTiles != false && VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npc.TypeIdentity, out var table) &&
            HasSickleCondition(table.Rules))
        {
            var captures = playerAuthority.CaptureItemOwnerPlayers();
            if (IsPreviewingDeath) plannedSpecificPlayers = captures;
            if (TryFindClosestPlayer(in npc, out var closest))
            {
                foreach (var capture in captures)
                    if (capture.State.Player == closest.Player)
                    {
                        hasSickle = CaptureSickle(capture);
                        break;
                    }
            }
        }
        // Main.expertMode reads effective Difficulty, which Good World promotes by one level.
        context = new(expertMode || npcSpecificGoodWorld || worldClock?.GetGoodWorld == true,
            DropExtraGel: npcSpecificDropExtraGel,
            SpawnedFromStatue: npc.Simulation.SpawnedFromStatue,
            LowTiles: lowTiles, HasSickle: hasSickle);
        return true;
    }

    private static bool HasSickleCondition(ReadOnlySpan<VanillaNpcLootRule> rules)
    {
        foreach (var rule in rules)
            if (rule.Kind == VanillaNpcLootRuleKind.SkyblockSickleCommon) return true;
        return false;
    }

    private static bool? CaptureSickle(RuntimeItemOwnerPlayerCapture1458 capture)
    {
        if (!capture.HasInventory) return null;
        bool openVoidBag = false;
        for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount; slot++)
        {
            var item = capture.Inventory[slot];
            if (item.IsEmpty) continue;
            if (item.ItemType.Value == VanillaNpcSpecificDropItemIds.Sickle.Value) return true;
            if (item.ItemType == VanillaNpcLootPredicateItemIds1458.OpenVoidBag) openVoidBag = true;
        }
        if (!openVoidBag) return false;
        foreach (var equipment in capture.Equipment)
            if (equipment.SlotId >= VanillaPlayerItemSlotCatalog.Bank4Start &&
                equipment.SlotId < VanillaPlayerItemSlotCatalog.Bank4EndExclusive && equipment.Stack > 0 &&
                equipment.TryGetCanonicalItemType(out var type) && type == VanillaNpcSpecificDropItemIds.Sickle)
                return true;
        // Missing imported storage slots do not establish an empty open Void Bag.
        return null;
    }

    private bool IsSpecificLootContextCurrent() =>
        CaptureSpecificLowTiles() == plannedSpecificLowTiles &&
        (plannedSpecificPlayers is null ||
            (plannedSpecificInventorySerial < ulong.MaxValue &&
             playerAuthority.NpcLootInventorySerial == plannedSpecificInventorySerial &&
             playerAuthority.IsCurrentItemOwnerPlayers(plannedSpecificPlayers)));
}
