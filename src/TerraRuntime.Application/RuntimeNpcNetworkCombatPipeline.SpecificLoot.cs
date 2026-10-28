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
    private Func<bool?>? specificDontStarveWorldSource;
    private SpecificWorldFacts plannedSpecificWorld;

    private readonly record struct SpecificWorldFacts(bool? DontStarve, bool? BloodMoonNight, bool? Windy);

    internal void BindSpecificLootWorldSource(Func<bool?> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (specificDontStarveWorldSource is not null)
            throw new InvalidOperationException("NPC-specific world source is already bound.");
        specificDontStarveWorldSource = source;
    }

    private SpecificWorldFacts CaptureSpecificWorldFacts() => new(
        specificDontStarveWorldSource?.Invoke(),
        worldClock is { } clock ? !clock.DayTime && clock.BloodMoonActive : null,
        worldClock is { } weather ? MathF.Abs(weather.WindSpeedTarget) >= .4f : null);

    private bool? CaptureSpecificLowTiles() => npcSpecificLowTiles is not null
        ? npcSpecificLowTiles() : skyblockLowTiles ? null : false;

    private bool TryCaptureSpecificLootContext(in NpcSnapshot npc, out VanillaNpcLootContext context)
    {
        bool? lowTiles = IsPreviewingDeath ? plannedSpecificLowTiles : CaptureSpecificLowTiles();
        var world = IsPreviewingDeath ? plannedSpecificWorld : CaptureSpecificWorldFacts();
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
            LowTiles: lowTiles, HasSickle: hasSickle)
        {
            DontStarveWorld = world.DontStarve,
            BloodMoonNight = world.BloodMoonNight,
            WindyEnoughForKites = world.Windy
        };
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
        CaptureSpecificWorldFacts() == plannedSpecificWorld &&
        (plannedSpecificPlayers is null ||
            (plannedSpecificInventorySerial < ulong.MaxValue &&
             playerAuthority.NpcLootInventorySerial == plannedSpecificInventorySerial &&
             playerAuthority.IsCurrentItemOwnerPlayers(plannedSpecificPlayers)));
}
