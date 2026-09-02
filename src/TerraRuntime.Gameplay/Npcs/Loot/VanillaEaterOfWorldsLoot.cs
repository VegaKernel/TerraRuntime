using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaEaterOfWorldsLootContext(
    bool IsExpertMode,
    bool IsMasterMode,
    bool IsBoss)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaEaterOfWorldsLootPlayer(
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

/// <summary>
/// Delivery boundary for Eater of Worlds segment drops and last-segment boss wrappers. CanDeliver methods are
/// side-effect free; successful evaluation materializes each item inline so Item.NewItem RNG remains interleaved with
/// subsequent loot-rule RNG exactly like the TerrariaServer rule chain.
/// </summary>
public interface IEaterOfWorldsLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);

    bool CanDeliverWorldItem(ItemTypeId itemType);

    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random);

    bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random);
}

public readonly record struct EaterOfWorldsLootExecutionResult(
    int SegmentWorldItemCount,
    int BossWorldItemCount,
    int InstancedItemCount,
    int InstancedRecipientCount,
    int MasterPetDropCount)
{
    public int TotalLogicalItemCount => checked(SegmentWorldItemCount + BossWorldItemCount + InstancedItemCount);

    public bool IsValid =>
        SegmentWorldItemCount >= 0 &&
        BossWorldItemCount >= 0 &&
        InstancedItemCount >= 0 &&
        InstancedRecipientCount >= 0 &&
        MasterPetDropCount >= 0 &&
        MasterPetDropCount <= BossWorldItemCount;
}

/// <summary>
/// Source-order implementation of RegisterBoss_EOW from TerrariaServer 1.4.5.8. Every segment owns the two small
/// difficulty-scaled material rules. DropEoWLoot promotes only the final active 13/14/15 segment to boss, which then
/// unlocks the Expert bag, Master relic/pet path or normal-only finishing drops, followed by the separately registered
/// boss trophy rule.
/// </summary>
public static class VanillaEaterOfWorldsLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool TryExecute(
        in VanillaEaterOfWorldsLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IEaterOfWorldsLootDeliverySink sink,
        out EaterOfWorldsLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;

        if (!context.IsValid || !npcOrigin.IsValid || !ArePlayersSourceOrdered(activeInteractingPlayers) ||
            !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.ShadowScale) ||
            !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.DemoniteOre))
        {
            return false;
        }

        if (context.IsBoss)
        {
            if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag))
                return false;
            if (context.IsMasterMode &&
                (!sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy) ||
                 !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem)))
            {
                return false;
            }
            if (!context.IsExpertMode &&
                (!sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EatersBone) ||
                 !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterMask)))
            {
                return false;
            }
            if (!sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy))
                return false;
        }

        int segmentItems = 0;
        int bossItems = 0;
        int petDrops = 0;

        int scaleChance = context.IsMasterMode ? 10 : context.IsExpertMode ? 5 : 2;
        if (!TryRollWorldItem(
                VanillaEaterOfWorldsItemIds.ShadowScale,
                scaleChance,
                1,
                2,
                in npcOrigin,
                rolls,
                sink,
                ref segmentItems))
        {
            return false;
        }

        int oreChance = context.IsMasterMode ? 3 : 2;
        int oreMin = context.IsMasterMode || context.IsExpertMode ? 1 : 2;
        int oreMax = context.IsMasterMode ? 2 : context.IsExpertMode ? 3 : 5;
        if (!TryRollWorldItem(
                VanillaEaterOfWorldsItemIds.DemoniteOre,
                oreChance,
                oreMin,
                oreMax,
                in npcOrigin,
                rolls,
                sink,
                ref segmentItems))
        {
            return false;
        }

        if (!context.IsBoss)
        {
            result = new EaterOfWorldsLootExecutionResult(segmentItems, 0, 0, 0, 0);
            return result.IsValid;
        }

        int instancedItems = 0;
        int instancedRecipients = 0;
        if (context.IsExpertMode)
        {
            // BossBagByCondition -> DropLocalPerClientAndResetsNPCMoneyTo0: raw chance RNG then shared stack RNG.
            rolls.NextInt32(0, 1);
            short bagStack = checked((short)rolls.NextInt32(1, 2));
            var bag = new NpcLootDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag, bagStack);
            if (!sink.TryDeliverInstanced(
                    in npcOrigin,
                    in bag,
                    activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks,
                    rolls))
            {
                throw new InvalidOperationException(
                    "Eater of Worlds loot sink advertised instanced bag support but failed after preflight.");
            }
            instancedItems = 1;
            instancedRecipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            if (!TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy,
                    1,
                    1,
                    1,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems))
            {
                return false;
            }

            // MasterModeDropOnAllPlayers chooses one stack before its ascending active-player loop and uses raw RNG.
            short petStack = checked((short)rolls.NextInt32(1, 2));
            for (int index = 0; index < activeInteractingPlayers.Length; index++)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                VanillaEaterOfWorldsLootPlayer player = activeInteractingPlayers[index];
                NpcLootWorldItemOrigin playerOrigin = player.Origin;
                var pet = new NpcLootDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem, petStack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in pet, rolls))
                {
                    throw new InvalidOperationException(
                        "Eater of Worlds loot sink advertised Master pet support but failed after preflight.");
                }
                bossItems++;
                petDrops++;
            }
        }
        else if (!context.IsExpertMode)
        {
            if (!TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.DemoniteOre,
                    1,
                    20,
                    60,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems) ||
                !TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.EatersBone,
                    20,
                    1,
                    1,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems) ||
                !TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.EaterMask,
                    7,
                    1,
                    1,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems))
            {
                return false;
            }
        }

        // Trophy rules for 13/14/15 are registered later and retain their source position after RegisterBoss_EOW.
        if (!TryRollWorldItem(
                VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy,
                10,
                1,
                1,
                in npcOrigin,
                rolls,
                sink,
                ref bossItems))
        {
            return false;
        }

        result = new EaterOfWorldsLootExecutionResult(
            segmentItems,
            bossItems,
            instancedItems,
            instancedRecipients,
            petDrops);
        return result.IsValid;
    }

    private static bool TryRollWorldItem(
        ItemTypeId itemType,
        int chanceDenominator,
        int minimumStack,
        int maximumStack,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        IEaterOfWorldsLootDeliverySink sink,
        ref int delivered)
    {
        if (rolls.RollLuck(chanceDenominator) != 0)
            return true;

        short stack = checked((short)rolls.NextInt32(minimumStack, checked(maximumStack + 1)));
        var drop = new NpcLootDrop(itemType, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
        {
            throw new InvalidOperationException(
                $"Eater of Worlds loot sink advertised world-item support for {itemType.Value} but failed after preflight.");
        }
        delivered++;
        return true;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> players)
    {
        int previousSlot = -1;
        for (int index = 0; index < players.Length; index++)
        {
            VanillaEaterOfWorldsLootPlayer player = players[index];
            if (!player.IsValid || player.Slot.Value <= previousSlot)
                return false;
            previousSlot = player.Slot.Value;
        }
        return true;
    }
}
