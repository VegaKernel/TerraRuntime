using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Source-backed NPC loot spawn origin. Terraria's ordinary DropItemFromNPC path converts NPC top-left position
/// to an integer center before Item.NewItem receives the drop.
/// </summary>
public readonly record struct NpcLootWorldItemOrigin(float CenterX, float CenterY)
{
    public bool IsValid => float.IsFinite(CenterX) && float.IsFinite(CenterY);
}

public interface INpcLootWorldItemMaterializer
{
    bool CanMaterialize(ItemTypeId itemType);

    bool TryMaterialize(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random,
        out WorldItemDropStateUpdate worldItem);
}

public readonly record struct NpcLootWorldItemTransactionResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    NpcTypeId Type,
    NpcLootWorldItemOrigin Origin,
    int SpawnedItemCount)
{
    public bool IsValid =>
        Target.IsAssigned && FinalRevision.IsAssigned && Type.IsAssigned && Origin.IsValid && SpawnedItemCount >= 0;
}

/// <summary>
/// Coordinates source-backed NPC-specific loot with the server-owned world-item store. All possible item branches
/// are preflighted before RNG. Successful items are materialized immediately between rule evaluations so loot RNG
/// and Item.NewItem RNG remain interleaved in source order.
/// </summary>
public sealed class RuntimeNpcLootWorldItemTransaction
{
    private readonly RuntimeNpcStore _npcStore;
    private readonly RuntimeWorldItemStore _worldItemStore;

    public RuntimeNpcLootWorldItemTransaction(RuntimeNpcStore npcStore, RuntimeWorldItemStore worldItemStore)
    {
        _npcStore = npcStore ?? throw new ArgumentNullException(nameof(npcStore));
        _worldItemStore = worldItemStore ?? throw new ArgumentNullException(nameof(worldItemStore));
    }

    public bool TryFinalizeAndSpawn(
        NpcHandle target,
        in VanillaNpcLootContext lootContext,
        INpcLootRollSource rolls,
        INpcLootWorldItemMaterializer materializer,
        Span<WorldItemSnapshot> spawnedItems,
        out NpcLootWorldItemTransactionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(materializer);
        result = default;

        if (!_npcStore.TryGet(target, out NpcSnapshot npc) ||
            npc.Simulation.LifeMax <= 0 ||
            npc.Simulation.Life != 0 ||
            !NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition npcDefinition))
        {
            return false;
        }

        bool kingSlimeNormal = npcType == VanillaNpcIds.KingSlime && !lootContext.IsExpertMode;
        VanillaNpcLootTable genericTable = default;
        if (!kingSlimeNormal && !VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npcType, out genericTable))
            return false;

        int maximumDropCount = kingSlimeNormal
            ? VanillaKingSlimeNormalLootCatalog.MaximumDropCount
            : genericTable.MaximumDropCount;
        if (spawnedItems.Length < maximumDropCount)
            return false;

        if (kingSlimeNormal)
        {
            if (!VanillaKingSlimeNormalLootCatalog.IsValid || !CanMaterializeKingSlime(materializer))
                return false;
        }
        else
        {
            ReadOnlySpan<VanillaNpcLootRule> rules = genericTable.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!materializer.CanMaterialize(rules[index].ItemType))
                    return false;
            }
        }

        Span<WorldItemDropReservation> capacityReservations =
            stackalloc WorldItemDropReservation[maximumDropCount];
        int capacityReservationCount = 0;
        for (; capacityReservationCount < maximumDropCount; capacityReservationCount++)
        {
            if (_worldItemStore.TryReserveDropSlot(out capacityReservations[capacityReservationCount]))
                continue;
            ReleaseReservations(capacityReservations[..capacityReservationCount]);
            return false;
        }

        var origin = new NpcLootWorldItemOrigin(
            CenterX: (int)npc.PositionX + npcDefinition.Width / 2,
            CenterY: (int)npc.PositionY + npcDefinition.Height / 2);
        Span<WorldItemDropReservation> stagedReservations =
            stackalloc WorldItemDropReservation[maximumDropCount];
        int stagedCount = 0;

        if (kingSlimeNormal)
        {
            ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
                        in rules[index], rolls, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacityReservations);
                    ReleaseReservations(stagedReservations[..stagedCount]);
                    return false;
                }
                if (dropped)
                    StageDrop(in origin, in drop, rolls, materializer, capacityReservations, stagedReservations, ref stagedCount);
            }
        }
        else
        {
            ReadOnlySpan<VanillaNpcLootRule> rules = genericTable.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaNpcLootEvaluator.TryEvaluateRule(
                        in rules[index], in lootContext, rolls, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacityReservations);
                    ReleaseReservations(stagedReservations[..stagedCount]);
                    return false;
                }
                if (dropped)
                    StageDrop(in origin, in drop, rolls, materializer, capacityReservations, stagedReservations, ref stagedCount);
            }
        }

        ReleaseReservations(capacityReservations[stagedCount..]);
        if (!_npcStore.TryDespawn(npc.Handle))
        {
            ReleaseReservations(stagedReservations[..stagedCount]);
            return false;
        }

        for (int index = 0; index < stagedCount; index++)
        {
            if (!_worldItemStore.TryCommitReservedDrop(in stagedReservations[index], out WorldItemSnapshot spawned))
                throw new InvalidOperationException("An exact staged NPC-loot reservation failed after NPC finalization.");
            spawnedItems[index] = spawned;
        }

        result = new NpcLootWorldItemTransactionResult(
            npc.Handle, npc.Revision, npcType, origin, stagedCount);
        return true;
    }

    private static bool CanMaterializeKingSlime(INpcLootWorldItemMaterializer materializer)
    {
        ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
        for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            for (int itemIndex = 0; itemIndex < rules[ruleIndex].PotentialItemCount; itemIndex++)
            {
                if (!materializer.CanMaterialize(rules[ruleIndex].GetPotentialItem(itemIndex)))
                    return false;
            }
        }
        return true;
    }

    private void StageDrop(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource rolls,
        INpcLootWorldItemMaterializer materializer,
        Span<WorldItemDropReservation> capacityReservations,
        Span<WorldItemDropReservation> stagedReservations,
        ref int stagedCount)
    {
        if (!materializer.TryMaterialize(in origin, in drop, rolls, out WorldItemDropStateUpdate materialized))
        {
            ReleaseReservations(capacityReservations);
            ReleaseReservations(stagedReservations[..stagedCount]);
            throw new InvalidOperationException(
                $"NPC loot materializer advertised support for item {drop.ItemType} but failed to materialize it.");
        }

        int capacityIndex = stagedCount;
        if (!_worldItemStore.TryReleaseDropReservation(in capacityReservations[capacityIndex]))
            throw new InvalidOperationException("Failed to release an exact NPC-loot capacity reservation.");
        capacityReservations[capacityIndex] = default;

        if (!_worldItemStore.TryReserveDrop(in materialized, out stagedReservations[stagedCount]))
            throw new InvalidOperationException("A preflighted NPC-loot world-item drop lost reserved capacity.");
        stagedCount++;
    }

    private void ReleaseReservations(Span<WorldItemDropReservation> reservations)
    {
        for (int index = 0; index < reservations.Length; index++)
        {
            if (reservations[index].IsAssigned)
                _worldItemStore.TryReleaseDropReservation(in reservations[index]);
        }
    }
}
