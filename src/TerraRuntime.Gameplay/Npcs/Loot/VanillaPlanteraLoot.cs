using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaPlanteraLootContext(bool IsExpertMode, bool IsMasterMode, bool DownedPlantera)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaPlanteraLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IPlanteraLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<VanillaPlanteraLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct PlanteraLootExecutionResult(
    int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount);

/// <summary>
/// TerrariaServer 1.4.5.8 ItemDropDatabase.Populate: RegisterBossTrophies, then RegisterBoss_Plantera.
/// Preserves first-kill ordering, CommonDrop luck/stack, OneFromRules raw selection, and per-player raw RNG.
/// Global coin/heart rules and opening the bag are outside this NPC-specific death-loot slice.
/// </summary>
public static class VanillaPlanteraLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    private static readonly ItemTypeId[] WeaponOptions =
    [
        VanillaPlanteraItemIds.GrenadeLauncher,
        VanillaPlanteraItemIds.VenusMagnum,
        VanillaPlanteraItemIds.NettleBurst,
        VanillaPlanteraItemIds.LeafBlower,
        VanillaPlanteraItemIds.FlowerPow,
        VanillaPlanteraItemIds.WaspGun,
        VanillaPlanteraItemIds.Seedler,
        VanillaPlanteraItemIds.FlowerWhip
    ];

    public static bool TryExecute(
        in VanillaPlanteraLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaPlanteraLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IPlanteraLootDeliverySink sink,
        out PlanteraLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid ||
            !ArePlayersSourceOrdered(activeInteractingPlayers) || !CanDeliverAll(in context, sink))
            return false;

        int worldItems = 0, instancedItems = 0, recipients = 0, petDrops = 0;
        // Populate registers boss trophies BEFORE RegisterBosses; GetRulesForNPCID preserves insertion order.
        RollCommon(VanillaPlanteraItemIds.PlanteraTrophy, 10, 1, 1,
            in npcOrigin, rolls, sink, ref worldItems);
        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(VanillaPlanteraItemIds.PlanteraBossBag,
                checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in npcOrigin, in bag, activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("Plantera loot sink failed advertised Boss Bag delivery.");
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            RollCommon(VanillaPlanteraItemIds.PlanteraMasterTrophy, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            short petStack = checked((short)rolls.NextInt32(1, 2));
            foreach (VanillaPlanteraLootPlayer player in activeInteractingPlayers)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                Deliver(VanillaPlanteraItemIds.PlanteraPetItem, petStack,
                    player.Origin, rolls, sink, ref worldItems);
                petDrops++;
            }
        }

        if (!context.IsExpertMode)
        {
            // FirstTimeKillingPlantera resolves before the other children of NotExpert.
            ItemTypeId weapon = VanillaPlanteraItemIds.GrenadeLauncher;
            if (context.DownedPlantera)
            {
                // OneFromRules uses raw RNG, then resolves the chosen CommonDrop (luck + stack).
                rolls.NextInt32(0, 1);
                weapon = WeaponOptions[rolls.NextInt32(0, WeaponOptions.Length)];
            }
            RollCommon(weapon, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            if (weapon == VanillaPlanteraItemIds.GrenadeLauncher)
                RollCommon(VanillaPlanteraItemIds.RocketI, 1, 50, 150,
                    in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaPlanteraItemIds.PlanteraMask, 7, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaPlanteraItemIds.TempleKey, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaPlanteraItemIds.Seedling, 20, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaPlanteraItemIds.TheAxe, 50, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaPlanteraItemIds.PygmyStaff, 4, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaPlanteraItemIds.ThornHook, 10, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
        }

        result = new PlanteraLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return true;
    }

    private static bool CanDeliverAll(in VanillaPlanteraLootContext context, IPlanteraLootDeliverySink sink)
    {
        if (!sink.CanDeliverWorldItem(VanillaPlanteraItemIds.PlanteraTrophy) ||
            (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaPlanteraItemIds.PlanteraBossBag)) ||
            (context.IsMasterMode &&
             (!sink.CanDeliverWorldItem(VanillaPlanteraItemIds.PlanteraMasterTrophy) ||
              !sink.CanDeliverWorldItem(VanillaPlanteraItemIds.PlanteraPetItem))))
            return false;

        if (!context.IsExpertMode)
        {
            ReadOnlySpan<ItemTypeId> classic =
            [
                VanillaPlanteraItemIds.PlanteraMask,
                VanillaPlanteraItemIds.TempleKey,
                VanillaPlanteraItemIds.Seedling,
                VanillaPlanteraItemIds.TheAxe,
                VanillaPlanteraItemIds.PygmyStaff,
                VanillaPlanteraItemIds.ThornHook,
                VanillaPlanteraItemIds.GrenadeLauncher,
                VanillaPlanteraItemIds.RocketI,
                VanillaPlanteraItemIds.VenusMagnum,
                VanillaPlanteraItemIds.NettleBurst,
                VanillaPlanteraItemIds.LeafBlower,
                VanillaPlanteraItemIds.FlowerPow,
                VanillaPlanteraItemIds.WaspGun,
                VanillaPlanteraItemIds.Seedler,
                VanillaPlanteraItemIds.FlowerWhip
            ];
            foreach (ItemTypeId item in classic)
                if (!sink.CanDeliverWorldItem(item))
                    return false;
        }
        return true;
    }

    private static void RollCommon(ItemTypeId item, int denominator, int minStack, int maxStack,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IPlanteraLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) == 0)
            Deliver(item, checked((short)rolls.NextInt32(minStack, maxStack + 1)),
                in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IPlanteraLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Plantera loot sink failed advertised item {item.Value} delivery.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaPlanteraLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaPlanteraLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
