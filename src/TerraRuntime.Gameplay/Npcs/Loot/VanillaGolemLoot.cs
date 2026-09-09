using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaGolemLootContext(bool IsExpertMode, bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaGolemLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IGolemLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<VanillaGolemLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct GolemLootExecutionResult(
    int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount);

/// <summary>
/// TerrariaServer 1.4.5.8 ItemDropDatabase.Populate: RegisterBossTrophies, then RegisterBoss_Golem.
/// Preserves registration ordering, CommonDrop luck/stack, OneFromRules raw selection, and per-player raw RNG.
/// Global coin/heart rules and opening the bag are outside this NPC-specific death-loot slice.
/// </summary>
public static class VanillaGolemLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    private static readonly ItemTypeId[] RewardOptions =
    [
        VanillaGolemItemIds.Stynger,
        VanillaGolemItemIds.PossessedHatchet,
        VanillaGolemItemIds.SunStone,
        VanillaGolemItemIds.EyeoftheGolem,
        VanillaGolemItemIds.HeatRay,
        VanillaGolemItemIds.StaffofEarth,
        VanillaGolemItemIds.GolemFist
    ];

    public static bool TryExecute(
        in VanillaGolemLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaGolemLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IGolemLootDeliverySink sink,
        out GolemLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid ||
            !ArePlayersSourceOrdered(activeInteractingPlayers) || !CanDeliverAll(in context, sink))
            return false;

        int worldItems = 0, instancedItems = 0, recipients = 0, petDrops = 0;
        // Populate registers boss trophies BEFORE RegisterBosses; GetRulesForNPCID preserves insertion order.
        RollCommon(VanillaGolemItemIds.GolemTrophy, 10, 1, 1,
            in npcOrigin, rolls, sink, ref worldItems);
        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(VanillaGolemItemIds.GolemBossBag,
                checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in npcOrigin, in bag, activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("Golem loot sink failed advertised Boss Bag delivery.");
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            RollCommon(VanillaGolemItemIds.GolemMasterTrophy, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            short petStack = checked((short)rolls.NextInt32(1, 2));
            foreach (VanillaGolemLootPlayer player in activeInteractingPlayers)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                Deliver(VanillaGolemItemIds.GolemPetItem, petStack,
                    player.Origin, rolls, sink, ref worldItems);
                petDrops++;
            }
        }

        if (!context.IsExpertMode)
        {
            RollCommon(VanillaGolemItemIds.GolemMask, 7, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaGolemItemIds.Picksaw, 4, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaGolemItemIds.MobiusStrip, 6, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            // OneFromRules uses raw chance/selection, then the selected CommonDrop uses luck + stack.
            rolls.NextInt32(0, 1);
            ItemTypeId reward = RewardOptions[rolls.NextInt32(0, RewardOptions.Length)];
            RollCommon(reward, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            if (reward == VanillaGolemItemIds.Stynger)
                RollCommon(VanillaGolemItemIds.StyngerBolt, 1, 60, 180,
                    in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaGolemItemIds.BeetleHusk, 1, 4, 8, in npcOrigin, rolls, sink, ref worldItems);
        }

        result = new GolemLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return true;
    }

    private static bool CanDeliverAll(in VanillaGolemLootContext context, IGolemLootDeliverySink sink)
    {
        if (!sink.CanDeliverWorldItem(VanillaGolemItemIds.GolemTrophy) ||
            (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaGolemItemIds.GolemBossBag)) ||
            (context.IsMasterMode &&
             (!sink.CanDeliverWorldItem(VanillaGolemItemIds.GolemMasterTrophy) ||
              !sink.CanDeliverWorldItem(VanillaGolemItemIds.GolemPetItem))))
            return false;

        if (!context.IsExpertMode)
        {
            ReadOnlySpan<ItemTypeId> classic =
            [
                VanillaGolemItemIds.GolemMask,
                VanillaGolemItemIds.Picksaw,
                VanillaGolemItemIds.MobiusStrip,
                VanillaGolemItemIds.Stynger,
                VanillaGolemItemIds.StyngerBolt,
                VanillaGolemItemIds.PossessedHatchet,
                VanillaGolemItemIds.SunStone,
                VanillaGolemItemIds.EyeoftheGolem,
                VanillaGolemItemIds.HeatRay,
                VanillaGolemItemIds.StaffofEarth,
                VanillaGolemItemIds.GolemFist,
                VanillaGolemItemIds.BeetleHusk
            ];
            foreach (ItemTypeId item in classic)
                if (!sink.CanDeliverWorldItem(item))
                    return false;
        }
        return true;
    }

    private static void RollCommon(ItemTypeId item, int denominator, int minStack, int maxStack,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IGolemLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) == 0)
            Deliver(item, checked((short)rolls.NextInt32(minStack, maxStack + 1)),
                in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IGolemLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Golem loot sink failed advertised item {item.Value} delivery.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaGolemLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaGolemLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
