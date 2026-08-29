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

/// <summary>
/// Materializes one already-rolled NPC loot item into authoritative world-item drop state. CanMaterialize must be
/// side-effect free so the transaction can fail before reserving capacity or consuming loot RNG. Once it returns
/// true for an item type, TryMaterialize is expected to succeed for valid source-backed drops of that type.
/// </summary>
public interface INpcLootWorldItemMaterializer
{
    bool CanMaterialize(ItemTypeId itemType);

    bool TryMaterialize(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random,
        out WorldItemDropStateUpdate worldItem);
}

/// <summary>Result of one atomic-by-single-writer NPC death -> world-item transaction.</summary>
public readonly record struct NpcLootWorldItemTransactionResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    NpcTypeId Type,
    NpcLootWorldItemOrigin Origin,
    int SpawnedItemCount)
{
    public bool IsValid =>
        Target.IsAssigned &&
        FinalRevision.IsAssigned &&
        Type.IsAssigned &&
        Origin.IsValid &&
        SpawnedItemCount >= 0;
}

/// <summary>
/// Coordinates source-backed NPC-specific loot with the server-owned world-item store. Capacity and materializer
/// support are proven before loot RNG is consumed. Successful rules are materialized immediately, before the next
/// rule is rolled, because TerrariaServer 1.4.5.8 proves DropAttemptInfo.rng and Item.NewItem both consume Main.rand.
/// </summary>
public sealed class RuntimeNpcLootWorldItemTransaction
{
    private readonly RuntimeNpcStore _npcStore;
    private readonly RuntimeWorldItemStore _worldItemStore;

    public RuntimeNpcLootWorldItemTransaction(
        RuntimeNpcStore npcStore,
        RuntimeWorldItemStore worldItemStore)
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

        ReadOnlySpan<VanillaNpcLootRule> rules = VanillaNpcLootRuleCatalog.GetNpcSpecificRules(npcType);
        if (rules.IsEmpty || spawnedItems.Length < rules.Length)
            return false;

        for (int index = 0; index < rules.Length; index++)
        {
            VanillaNpcLootRule rule = rules[index];
            if (!rule.IsValid || !materializer.CanMaterialize(rule.ItemType))
                return false;
        }

        Span<WorldItemDropReservation> capacityReservations = stackalloc WorldItemDropReservation[rules.Length];
        int capacityReservationCount = 0;
        for (; capacityReservationCount < rules.Length; capacityReservationCount++)
        {
            if (_worldItemStore.TryReserveDropSlot(out capacityReservations[capacityReservationCount]))
                continue;

            ReleaseReservations(capacityReservations[..capacityReservationCount]);
            return false;
        }

        var origin = new NpcLootWorldItemOrigin(
            CenterX: (int)npc.PositionX + npcDefinition.Width / 2,
            CenterY: (int)npc.PositionY + npcDefinition.Height / 2);

        Span<WorldItemDropReservation> stagedReservations = stackalloc WorldItemDropReservation[rules.Length];
        int stagedCount = 0;

        for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            if (!VanillaNpcLootEvaluator.TryEvaluateRule(
                    in rules[ruleIndex],
                    in lootContext,
                    rolls,
                    out bool dropped,
                    out NpcLootDrop drop))
            {
                ReleaseReservations(capacityReservations);
                ReleaseReservations(stagedReservations[..stagedCount]);
                return false;
            }

            if (!dropped)
                continue;

            if (!materializer.TryMaterialize(in origin, in drop, rolls, out WorldItemDropStateUpdate materialized))
            {
                ReleaseReservations(capacityReservations);
                ReleaseReservations(stagedReservations[..stagedCount]);
                throw new InvalidOperationException(
                    $"NPC loot materializer advertised support for item {drop.ItemType} but failed to materialize it.");
            }

            int capacityIndex = stagedCount;
            if (!_worldItemStore.TryReleaseDropReservation(in capacityReservations[capacityIndex]))
            {
                ReleaseReservations(capacityReservations[(capacityIndex + 1)..]);
                ReleaseReservations(stagedReservations[..stagedCount]);
                throw new InvalidOperationException("Failed to release an exact NPC-loot capacity reservation.");
            }

            capacityReservations[capacityIndex] = default;
            if (!_worldItemStore.TryReserveDrop(in materialized, out stagedReservations[stagedCount]))
            {
                ReleaseReservations(capacityReservations[(capacityIndex + 1)..]);
                ReleaseReservations(stagedReservations[..stagedCount]);
                throw new InvalidOperationException(
                    "A preflighted NPC-loot world-item drop became invalid or lost reserved capacity on the single writer.");
            }

            stagedCount++;
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
            {
                throw new InvalidOperationException(
                    "An exact staged NPC-loot world-item reservation failed after the NPC generation was finalized.");
            }

            spawnedItems[index] = spawned;
        }

        result = new NpcLootWorldItemTransactionResult(
            npc.Handle,
            npc.Revision,
            npcType,
            origin,
            stagedCount);
        return true;
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
