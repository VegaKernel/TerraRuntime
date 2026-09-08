using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaQueenSlimeLootContext(bool IsExpertMode, bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaQueenSlimeLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IQueenSlimeLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<VanillaQueenSlimeLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct QueenSlimeLootExecutionResult(
    int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount);

/// <summary>
/// TerrariaServer 1.4.5.8 ItemDropDatabase.RegisterBoss_QueenSlime, then RegisterBossTrophies.
/// Preserves CommonDrop luck/stack, OneFromOptions luck/selection, and NotScalingWithLuck raw RNG order.
/// Global coin/heart rules and opening the bag are outside this NPC-specific death-loot slice.
/// </summary>
public static class VanillaQueenSlimeLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    private static readonly ItemTypeId[] ArmorOptions =
    [
        VanillaQueenSlimeItemIds.CrystalNinjaHelmet,
        VanillaQueenSlimeItemIds.CrystalNinjaChestplate,
        VanillaQueenSlimeItemIds.CrystalNinjaLeggings
    ];

    public static bool TryExecute(
        in VanillaQueenSlimeLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaQueenSlimeLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IQueenSlimeLootDeliverySink sink,
        out QueenSlimeLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid ||
            !ArePlayersSourceOrdered(activeInteractingPlayers) || !CanDeliverAll(in context, sink))
            return false;

        int worldItems = 0, instancedItems = 0, recipients = 0, petDrops = 0;
        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(VanillaQueenSlimeItemIds.QueenSlimeBossBag,
                checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in npcOrigin, in bag, activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("Queen Slime loot sink failed advertised Boss Bag delivery.");
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            RollCommon(VanillaQueenSlimeItemIds.QueenSlimeMasterTrophy, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            short petStack = checked((short)rolls.NextInt32(1, 2));
            foreach (VanillaQueenSlimeLootPlayer player in activeInteractingPlayers)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                Deliver(VanillaQueenSlimeItemIds.QueenSlimePetItem, petStack,
                    player.Origin, rolls, sink, ref worldItems);
                petDrops++;
            }
        }

        if (!context.IsExpertMode)
        {
            RollCommon(VanillaQueenSlimeItemIds.GelBalloon, 1, 25, 75,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaQueenSlimeItemIds.QueenSlimeMask, 7, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            // OneFromOptions has no additional guaranteed chance or stack draw after choosing armor.
            if (rolls.RollLuck(1) == 0)
                Deliver(ArmorOptions[rolls.NextInt32(0, ArmorOptions.Length)], 1,
                    in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaQueenSlimeItemIds.BladeStaff, 4, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaQueenSlimeItemIds.QueenSlimeMountSaddle, 4, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            if (rolls.NextInt32(0, 3) == 0)
                Deliver(VanillaQueenSlimeItemIds.QueenSlimeHook, checked((short)rolls.NextInt32(1, 2)),
                    in npcOrigin, rolls, sink, ref worldItems);
        }

        RollCommon(VanillaQueenSlimeItemIds.QueenSlimeTrophy, 10, 1, 1,
            in npcOrigin, rolls, sink, ref worldItems);
        result = new QueenSlimeLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return true;
    }

    private static bool CanDeliverAll(in VanillaQueenSlimeLootContext context, IQueenSlimeLootDeliverySink sink)
    {
        if (!sink.CanDeliverWorldItem(VanillaQueenSlimeItemIds.QueenSlimeTrophy) ||
            (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaQueenSlimeItemIds.QueenSlimeBossBag)) ||
            (context.IsMasterMode &&
             (!sink.CanDeliverWorldItem(VanillaQueenSlimeItemIds.QueenSlimeMasterTrophy) ||
              !sink.CanDeliverWorldItem(VanillaQueenSlimeItemIds.QueenSlimePetItem))))
            return false;

        if (!context.IsExpertMode)
        {
            ReadOnlySpan<ItemTypeId> classic =
            [
                VanillaQueenSlimeItemIds.GelBalloon,
                VanillaQueenSlimeItemIds.QueenSlimeMask,
                VanillaQueenSlimeItemIds.CrystalNinjaHelmet,
                VanillaQueenSlimeItemIds.CrystalNinjaChestplate,
                VanillaQueenSlimeItemIds.CrystalNinjaLeggings,
                VanillaQueenSlimeItemIds.BladeStaff,
                VanillaQueenSlimeItemIds.QueenSlimeMountSaddle,
                VanillaQueenSlimeItemIds.QueenSlimeHook
            ];
            foreach (ItemTypeId item in classic)
                if (!sink.CanDeliverWorldItem(item))
                    return false;
        }
        return true;
    }

    private static void RollCommon(ItemTypeId item, int denominator, int minStack, int maxStack,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IQueenSlimeLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) == 0)
            Deliver(item, checked((short)rolls.NextInt32(minStack, maxStack + 1)),
                in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IQueenSlimeLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Queen Slime loot sink failed advertised item {item.Value} delivery.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaQueenSlimeLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaQueenSlimeLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
