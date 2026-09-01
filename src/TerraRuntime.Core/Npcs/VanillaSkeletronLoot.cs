using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaSkeletronLootContext(
    bool IsExpertMode,
    bool IsMasterMode,
    bool RedHatAdjustmentsEnabled)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaSkeletronLootPlayer(
    PlayerSlotId Slot,
    float CenterX,
    float CenterY)
{
    public bool IsValid =>
        Slot.Value < RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots &&
        float.IsFinite(CenterX) &&
        float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface ISkeletronLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);

    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaSkeletronLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random);

    bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random);
}

public readonly record struct SkeletronLootExecutionResult(
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
/// Source-order implementation of TerrariaServer 1.4.5.8 RegisterBoss_Skeletron plus the later boss-trophy
/// registration. Expert owns the Boss Bag, Master owns relic/pet delivery, Classic preserves the chained
/// mask -> hand -> Book of Skulls failure rolls, Chippy's Couch is global to the boss rule set, and the
/// RedHat Skeletron condition emits its five source-registered vanity drops without inventing extra rolls.
/// </summary>
public static class VanillaSkeletronLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool TryExecute(
        in VanillaSkeletronLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaSkeletronLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        ISkeletronLootDeliverySink sink,
        out SkeletronLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid || !ArePlayersSourceOrdered(activeInteractingPlayers))
            return false;

        if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaSkeletronItemIds.SkeletronBossBag))
            return false;
        if (context.IsMasterMode &&
            (!sink.CanDeliverWorldItem(VanillaSkeletronItemIds.SkeletronMasterTrophy) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.SkeletronPetItem)))
        {
            return false;
        }
        if (!context.IsExpertMode &&
            (!sink.CanDeliverWorldItem(VanillaSkeletronItemIds.SkeletronMask) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.SkeletronHand) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.BookOfSkulls)))
        {
            return false;
        }
        if (!sink.CanDeliverWorldItem(VanillaSkeletronItemIds.ChippysCouch) ||
            !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.SkeletronTrophy))
        {
            return false;
        }
        if (context.RedHatAdjustmentsEnabled &&
            (!sink.CanDeliverWorldItem(VanillaSkeletronItemIds.ChippysHead) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.ChippysBody) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.ChippysLegs) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.ChippysWingsInactive) ||
             !sink.CanDeliverWorldItem(VanillaSkeletronItemIds.ChippysHeadband)))
        {
            return false;
        }

        int worldItems = 0;
        int instancedItems = 0;
        int recipients = 0;
        int petDrops = 0;

        if (context.IsExpertMode)
        {
            // BossBag() consumes the same guaranteed rule shape used by the imported Brain/Eater evaluators.
            rolls.NextInt32(0, 1);
            short stack = checked((short)rolls.NextInt32(1, 2));
            var bag = new NpcLootDrop(VanillaSkeletronItemIds.SkeletronBossBag, stack);
            if (!sink.TryDeliverInstanced(
                    in npcOrigin,
                    in bag,
                    activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks,
                    rolls))
            {
                throw new InvalidOperationException("Skeletron loot sink failed an advertised instanced Boss Bag delivery.");
            }
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            if (!TryRollWorldItem(
                    VanillaSkeletronItemIds.SkeletronMasterTrophy,
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
                VanillaSkeletronLootPlayer player = activeInteractingPlayers[index];
                NpcLootWorldItemOrigin playerOrigin = player.Origin;
                var pet = new NpcLootDrop(VanillaSkeletronItemIds.SkeletronPetItem, petStack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in pet, rolls))
                    throw new InvalidOperationException("Skeletron loot sink failed an advertised Master pet delivery.");
                worldItems++;
                petDrops++;
            }
        }
        else if (!context.IsExpertMode)
        {
            bool maskDropped = TryChainedOneInSeven(
                VanillaSkeletronItemIds.SkeletronMask,
                in npcOrigin,
                rolls,
                sink,
                ref worldItems);
            if (!maskDropped)
            {
                bool handDropped = TryChainedOneInSeven(
                    VanillaSkeletronItemIds.SkeletronHand,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref worldItems);
                if (!handDropped)
                {
                    _ = TryChainedOneInSeven(
                        VanillaSkeletronItemIds.BookOfSkulls,
                        in npcOrigin,
                        rolls,
                        sink,
                        ref worldItems);
                }
            }
        }

        if (!TryRollWorldItem(
                VanillaSkeletronItemIds.ChippysCouch,
                7, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems))
        {
            return false;
        }

        if (context.RedHatAdjustmentsEnabled)
        {
            ReadOnlySpan<ItemTypeId> redHatDrops =
            [
                VanillaSkeletronItemIds.ChippysHead,
                VanillaSkeletronItemIds.ChippysBody,
                VanillaSkeletronItemIds.ChippysLegs,
                VanillaSkeletronItemIds.ChippysWingsInactive,
                VanillaSkeletronItemIds.ChippysHeadband
            ];
            for (int index = 0; index < redHatDrops.Length; index++)
            {
                if (!TryRollWorldItem(
                        redHatDrops[index],
                        1, 1, 1, 1,
                        in npcOrigin, rolls, sink, ref worldItems))
                {
                    return false;
                }
            }
        }

        if (!TryRollWorldItem(
                VanillaSkeletronItemIds.SkeletronTrophy,
                10, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems))
        {
            return false;
        }

        result = new SkeletronLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return result.IsValid;
    }

    private static bool TryChainedOneInSeven(
        ItemTypeId itemType,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        ISkeletronLootDeliverySink sink,
        ref int delivered)
    {
        if (rolls.RollLuck(7) != 0)
            return false;

        short stack = checked((short)rolls.NextInt32(1, 2));
        var drop = new NpcLootDrop(itemType, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Skeletron loot sink failed advertised world-item support for {itemType.Value}.");
        delivered++;
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
        ISkeletronLootDeliverySink sink,
        ref int delivered)
    {
        if (rolls.RollLuck(chanceDenominator) >= chanceNumerator)
            return true;

        short stack = checked((short)rolls.NextInt32(minimumStack, checked(maximumStack + 1)));
        var drop = new NpcLootDrop(itemType, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Skeletron loot sink failed advertised world-item support for {itemType.Value}.");
        delivered++;
        return true;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaSkeletronLootPlayer> players)
    {
        int previousSlot = -1;
        for (int index = 0; index < players.Length; index++)
        {
            VanillaSkeletronLootPlayer player = players[index];
            if (!player.IsValid || player.Slot.Value <= previousSlot)
                return false;
            previousSlot = player.Slot.Value;
        }
        return true;
    }
}
