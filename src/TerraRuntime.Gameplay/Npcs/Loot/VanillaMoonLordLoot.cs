using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaMoonLordLootContext(bool IsExpertMode, bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaMoonLordLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IMoonLordLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<VanillaMoonLordLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct MoonLordLootExecutionResult(
    int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount);

/// <summary>
/// TerrariaServer 1.4.5.8 ItemDropDatabase.Populate: RegisterBossTrophies, then RegisterBoss_MoonLord.
/// Preserves registration ordering, CommonDrop luck/stack, two non-repeating raw selections, and per-player raw RNG.
/// Global coin/heart rules and opening the bag are outside this NPC-specific death-loot slice.
/// </summary>
public static class VanillaMoonLordLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    private static readonly ItemTypeId[] RewardOptions =
    [
        VanillaMoonLordItemIds.Meowmere,
        VanillaMoonLordItemIds.Terrarian,
        VanillaMoonLordItemIds.StarWrath,
        VanillaMoonLordItemIds.SDMG,
        VanillaMoonLordItemIds.Celeb2,
        VanillaMoonLordItemIds.LastPrism,
        VanillaMoonLordItemIds.LunarFlareBook,
        VanillaMoonLordItemIds.RainbowCrystalStaff,
        VanillaMoonLordItemIds.MoonlordTurretStaff,
        VanillaMoonLordItemIds.MoonLordWhip
    ];

    public static bool TryExecute(
        in VanillaMoonLordLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaMoonLordLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IMoonLordLootDeliverySink sink,
        out MoonLordLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid ||
            !ArePlayersSourceOrdered(activeInteractingPlayers) || !CanDeliverAll(in context, sink))
            return false;

        int worldItems = 0, instancedItems = 0, recipients = 0, petDrops = 0;
        // Populate registers boss trophies BEFORE RegisterBosses; GetRulesForNPCID preserves insertion order.
        RollCommon(VanillaMoonLordItemIds.MoonLordTrophy, 10, 1, 1,
            in npcOrigin, rolls, sink, ref worldItems);
        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(VanillaMoonLordItemIds.MoonLordBossBag,
                checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in npcOrigin, in bag, activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("MoonLord loot sink failed advertised Boss Bag delivery.");
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            RollCommon(VanillaMoonLordItemIds.MoonLordMasterTrophy, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            short petStack = checked((short)rolls.NextInt32(1, 2));
            foreach (VanillaMoonLordLootPlayer player in activeInteractingPlayers)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                Deliver(VanillaMoonLordItemIds.MoonLordPetItem, petStack,
                    player.Origin, rolls, sink, ref worldItems);
                petDrops++;
            }
        }

        if (!context.IsExpertMode)
        {
            RollCommon(VanillaMoonLordItemIds.BossMaskMoonlord, 7, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaMoonLordItemIds.MeowmereMinecart, 10, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaMoonLordItemIds.PortalGun, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaMoonLordItemIds.LunarOre, 1, 70, 90, in npcOrigin, rolls, sink, ref worldItems);
            // FromOptionsWithoutRepeatsDropRule removes the first choice before drawing the second.
            // Delivery (including natural prefix and velocity RNG) happens between the selections.
            int first = rolls.NextInt32(0, RewardOptions.Length);
            Deliver(RewardOptions[first], 1, in npcOrigin, rolls, sink, ref worldItems);
            int second = rolls.NextInt32(0, RewardOptions.Length - 1);
            Deliver(RewardOptions[second >= first ? second + 1 : second], 1,
                in npcOrigin, rolls, sink, ref worldItems);
        }

        result = new MoonLordLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return true;
    }

    private static bool CanDeliverAll(in VanillaMoonLordLootContext context, IMoonLordLootDeliverySink sink)
    {
        if (!sink.CanDeliverWorldItem(VanillaMoonLordItemIds.MoonLordTrophy) ||
            (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaMoonLordItemIds.MoonLordBossBag)) ||
            (context.IsMasterMode &&
             (!sink.CanDeliverWorldItem(VanillaMoonLordItemIds.MoonLordMasterTrophy) ||
              !sink.CanDeliverWorldItem(VanillaMoonLordItemIds.MoonLordPetItem))))
            return false;

        if (!context.IsExpertMode)
        {
            ReadOnlySpan<ItemTypeId> classic =
            [
                VanillaMoonLordItemIds.BossMaskMoonlord,
                VanillaMoonLordItemIds.MeowmereMinecart,
                VanillaMoonLordItemIds.PortalGun,
                VanillaMoonLordItemIds.LunarOre
            ];
            foreach (ItemTypeId item in RewardOptions)
                if (!sink.CanDeliverWorldItem(item))
                    return false;
            foreach (ItemTypeId item in classic)
                if (!sink.CanDeliverWorldItem(item))
                    return false;
        }
        return true;
    }

    private static void RollCommon(ItemTypeId item, int denominator, int minStack, int maxStack,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IMoonLordLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) == 0)
            Deliver(item, checked((short)rolls.NextInt32(minStack, maxStack + 1)),
                in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IMoonLordLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"MoonLord loot sink failed advertised item {item.Value} delivery.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaMoonLordLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaMoonLordLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
