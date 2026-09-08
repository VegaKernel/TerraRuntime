using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaEyeOfCthulhuLootContext(bool IsExpertMode, bool IsMasterMode, bool IsCrimson)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaEyeOfCthulhuLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid =>
        Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IEyeOfCthulhuLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<VanillaEyeOfCthulhuLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct EyeOfCthulhuLootExecutionResult(
    int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount);

/// <summary>
/// TerrariaServer 1.4.5.8 ItemDropDatabase.RegisterBoss_EOC, then RegisterBossTrophies.
/// Preserves CommonDrop luck/stack, difficulty gates and world-owned Crimson/Corruption branches.
/// Global coin/heart rules and opening the bag are outside this NPC-specific death-loot slice.
/// </summary>
public static class VanillaEyeOfCthulhuLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool TryExecute(
        in VanillaEyeOfCthulhuLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaEyeOfCthulhuLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IEyeOfCthulhuLootDeliverySink sink,
        out EyeOfCthulhuLootExecutionResult result)
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
            var bag = new NpcLootDrop(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuBossBag,
                checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in npcOrigin, in bag, activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("Eye of Cthulhu loot sink failed advertised Boss Bag delivery.");
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            RollCommon(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuMasterTrophy, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaEyeOfCthulhuItemIds.AviatorSunglasses, 1, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            short petStack = checked((short)rolls.NextInt32(1, 2));
            foreach (VanillaEyeOfCthulhuLootPlayer player in activeInteractingPlayers)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                Deliver(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuPetItem, petStack,
                    player.Origin, rolls, sink, ref worldItems);
                petDrops++;
            }
        }

        if (!context.IsExpertMode)
        {
            RollCommon(VanillaEyeOfCthulhuItemIds.EyeMask, 7, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaEyeOfCthulhuItemIds.Binoculars, 40, 1, 1,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(VanillaItemIds.UnholyArrow, 1, 20, 50,
                in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(context.IsCrimson ? VanillaBrainOfCthulhuItemIds.CrimtaneOre :
                VanillaEaterOfWorldsItemIds.DemoniteOre, 1, 30, 90, in npcOrigin, rolls, sink, ref worldItems);
            RollCommon(context.IsCrimson ? VanillaEyeOfCthulhuItemIds.CrimsonSeeds :
                VanillaEyeOfCthulhuItemIds.CorruptSeeds, 1, 1, 3, in npcOrigin, rolls, sink, ref worldItems);
        }

        RollCommon(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuTrophy, 10, 1, 1,
            in npcOrigin, rolls, sink, ref worldItems);
        result = new EyeOfCthulhuLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return true;
    }

    private static bool CanDeliverAll(in VanillaEyeOfCthulhuLootContext context, IEyeOfCthulhuLootDeliverySink sink)
    {
        if (!sink.CanDeliverWorldItem(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuTrophy) ||
            (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuBossBag)) ||
            (context.IsMasterMode &&
             (!sink.CanDeliverWorldItem(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuMasterTrophy) ||
              !sink.CanDeliverWorldItem(VanillaEyeOfCthulhuItemIds.AviatorSunglasses) ||
              !sink.CanDeliverWorldItem(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuPetItem))))
            return false;

        if (!context.IsExpertMode)
        {
            ReadOnlySpan<ItemTypeId> classic =
            [
                VanillaEyeOfCthulhuItemIds.EyeMask,
                VanillaEyeOfCthulhuItemIds.Binoculars,
                VanillaItemIds.UnholyArrow,
                context.IsCrimson ? VanillaBrainOfCthulhuItemIds.CrimtaneOre : VanillaEaterOfWorldsItemIds.DemoniteOre,
                context.IsCrimson ? VanillaEyeOfCthulhuItemIds.CrimsonSeeds : VanillaEyeOfCthulhuItemIds.CorruptSeeds
            ];
            foreach (ItemTypeId item in classic)
                if (!sink.CanDeliverWorldItem(item))
                    return false;
        }
        return true;
    }

    private static void RollCommon(ItemTypeId item, int denominator, int minStack, int maxStack,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IEyeOfCthulhuLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) == 0)
            Deliver(item, checked((short)rolls.NextInt32(minStack, maxStack + 1)),
                in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IEyeOfCthulhuLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Eye of Cthulhu loot sink failed advertised item {item.Value} delivery.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaEyeOfCthulhuLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaEyeOfCthulhuLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
