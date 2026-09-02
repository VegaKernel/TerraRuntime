using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaBrainOfCthulhuLootContext(
    bool IsExpertMode,
    bool IsMasterMode,
    NpcTypeId SourceType)
{
    public bool IsValid =>
        (!IsMasterMode || IsExpertMode) &&
        (SourceType == VanillaNpcIds.BrainOfCthulhu || SourceType == VanillaNpcIds.BrainCreeper);
}

public readonly record struct VanillaBrainOfCthulhuLootPlayer(
    PlayerSlotId Slot,
    float CenterX,
    float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) &&
        float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IBrainOfCthulhuLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);

    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaBrainOfCthulhuLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random);

    bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random);
}

public readonly record struct BrainOfCthulhuLootExecutionResult(
    int WorldItemCount,
    int InstancedItemCount,
    int InstancedRecipientCount,
    int MasterPetDropCount)
{
    public bool IsValid =>
        WorldItemCount >= 0 &&
        InstancedItemCount >= 0 &&
        InstancedRecipientCount >= 0 &&
        MasterPetDropCount >= 0 &&
        MasterPetDropCount <= WorldItemCount;
}

/// <summary>
/// Source-order implementation of TerrariaServer 1.4.5.8 RegisterBoss_BOC plus the later boss-trophy registration.
/// Creeper material rules preserve the Master/Expert/Classic numerator and stack bands; Brain owns bag/relic/pet,
/// non-Expert finishing drops and trophy.
/// </summary>
public static class VanillaBrainOfCthulhuLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool TryExecute(
        in VanillaBrainOfCthulhuLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaBrainOfCthulhuLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IBrainOfCthulhuLootDeliverySink sink,
        out BrainOfCthulhuLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid || !ArePlayersSourceOrdered(activeInteractingPlayers))
            return false;

        if (context.SourceType == VanillaNpcIds.BrainCreeper)
            return TryExecuteCreeper(in context, in npcOrigin, rolls, sink, out result);

        if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuBossBag))
            return false;
        if (context.IsMasterMode &&
            (!sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuMasterTrophy) ||
             !sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuPetItem)))
        {
            return false;
        }
        if (!context.IsExpertMode &&
            (!sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.CrimtaneOre) ||
             !sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.BrainMask) ||
             !sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.BoneRattle)))
        {
            return false;
        }
        if (!sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuTrophy))
            return false;

        int worldItems = 0;
        int instancedItems = 0;
        int recipients = 0;
        int petDrops = 0;

        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            short stack = checked((short)rolls.NextInt32(1, 2));
            var bag = new NpcLootDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuBossBag, stack);
            if (!sink.TryDeliverInstanced(
                    in npcOrigin,
                    in bag,
                    activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks,
                    rolls))
            {
                throw new InvalidOperationException("Brain loot sink failed an advertised instanced Boss Bag delivery.");
            }
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            if (!TryRollWorldItem(
                    VanillaBrainOfCthulhuItemIds.BrainOfCthulhuMasterTrophy,
                    1, 1, 1, 1,
                    in npcOrigin, rolls, sink, ref worldItems))
            {
                return false;
            }

            short petStack = checked((short)rolls.NextInt32(1, 2));
            for (int index = 0; index < activeInteractingPlayers.Length; index++)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                VanillaBrainOfCthulhuLootPlayer player = activeInteractingPlayers[index];
                NpcLootWorldItemOrigin playerOrigin = player.Origin;
                var pet = new NpcLootDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuPetItem, petStack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in pet, rolls))
                    throw new InvalidOperationException("Brain loot sink failed an advertised Master pet delivery.");
                worldItems++;
                petDrops++;
            }
        }
        else if (!context.IsExpertMode)
        {
            if (!TryRollWorldItem(VanillaBrainOfCthulhuItemIds.CrimtaneOre, 1, 1, 40, 90, in npcOrigin, rolls, sink, ref worldItems) ||
                !TryRollWorldItem(VanillaBrainOfCthulhuItemIds.BrainMask, 7, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems) ||
                !TryRollWorldItem(VanillaBrainOfCthulhuItemIds.BoneRattle, 20, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems))
            {
                return false;
            }
        }

        if (!TryRollWorldItem(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuTrophy, 10, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems))
            return false;

        result = new BrainOfCthulhuLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return result.IsValid;
    }

    private static bool TryExecuteCreeper(
        in VanillaBrainOfCthulhuLootContext context,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        IBrainOfCthulhuLootDeliverySink sink,
        out BrainOfCthulhuLootExecutionResult result)
    {
        result = default;
        if (!sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.TissueSample) ||
            !sink.CanDeliverWorldItem(VanillaBrainOfCthulhuItemIds.CrimtaneOre))
        {
            return false;
        }

        int tissueDenominator;
        int tissueMinimum;
        int tissueMaximum;
        int oreDenominator;
        int oreMinimum;
        int oreMaximum;
        if (context.IsMasterMode)
        {
            tissueDenominator = 4;
            tissueMinimum = 1;
            tissueMaximum = 2;
            oreDenominator = 4;
            oreMinimum = 2;
            oreMaximum = 4;
        }
        else if (context.IsExpertMode)
        {
            tissueDenominator = 3;
            tissueMinimum = 1;
            tissueMaximum = 3;
            oreDenominator = 3;
            oreMinimum = 5;
            oreMaximum = 7;
        }
        else
        {
            tissueDenominator = 3;
            tissueMinimum = 2;
            tissueMaximum = 5;
            oreDenominator = 3;
            oreMinimum = 5;
            oreMaximum = 12;
        }

        int delivered = 0;
        if (!TryRollWorldItem(VanillaBrainOfCthulhuItemIds.TissueSample, tissueDenominator, 2, tissueMinimum, tissueMaximum, in origin, rolls, sink, ref delivered) ||
            !TryRollWorldItem(VanillaBrainOfCthulhuItemIds.CrimtaneOre, oreDenominator, 2, oreMinimum, oreMaximum, in origin, rolls, sink, ref delivered))
        {
            return false;
        }

        result = new BrainOfCthulhuLootExecutionResult(delivered, 0, 0, 0);
        return true;
    }

    private static bool TryRollWorldItem(
        ItemTypeId itemType,
        int chanceDenominator,
        int chanceNumerator,
        int minimumStack,
        int maximumStack,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        IBrainOfCthulhuLootDeliverySink sink,
        ref int delivered)
    {
        if (rolls.RollLuck(chanceDenominator) >= chanceNumerator)
            return true;

        short stack = checked((short)rolls.NextInt32(minimumStack, checked(maximumStack + 1)));
        var drop = new NpcLootDrop(itemType, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Brain loot sink failed advertised world-item support for {itemType.Value}.");
        delivered++;
        return true;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaBrainOfCthulhuLootPlayer> players)
    {
        int previousSlot = -1;
        for (int index = 0; index < players.Length; index++)
        {
            VanillaBrainOfCthulhuLootPlayer player = players[index];
            if (!player.IsValid || player.Slot.Value <= previousSlot)
                return false;
            previousSlot = player.Slot.Value;
        }
        return true;
    }
}
