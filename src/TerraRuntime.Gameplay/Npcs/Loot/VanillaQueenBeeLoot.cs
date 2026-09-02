using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaQueenBeeLootContext(bool IsExpertMode, bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaQueenBeeLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid => Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots && float.IsFinite(CenterX) && float.IsFinite(CenterY);
    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IQueenBeeLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, ReadOnlySpan<VanillaQueenBeeLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct QueenBeeLootExecutionResult(int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount)
{
    public bool IsValid => WorldItemCount >= 0 && InstancedItemCount >= 0 && InstancedRecipientCount >= 0 && MasterPetDropCount >= 0 && MasterPetDropCount <= WorldItemCount;
}

/// <summary>Source-order implementation of TerrariaServer 1.4.5.8 RegisterBoss_QueenBee plus its trophy rule.</summary>
public static class VanillaQueenBeeLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    private static readonly ItemTypeId[] ClassicWeaponOptions = [VanillaQueenBeeItemIds.BeeGun, VanillaQueenBeeItemIds.BeeKeeper, VanillaQueenBeeItemIds.BeesKnees];
    private static readonly ItemTypeId[] VanityOptions = [VanillaQueenBeeItemIds.BeeHat, VanillaQueenBeeItemIds.BeeShirt, VanillaQueenBeeItemIds.BeePants];

    public static bool TryExecute(
        in VanillaQueenBeeLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaQueenBeeLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IQueenBeeLootDeliverySink sink,
        out QueenBeeLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !npcOrigin.IsValid || !ArePlayersSourceOrdered(activeInteractingPlayers) || !CanDeliverAll(in context, sink))
            return false;

        int worldItems = 0;
        int instancedItems = 0;
        int recipients = 0;
        int petDrops = 0;

        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(VanillaQueenBeeItemIds.QueenBeeBossBag, checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in npcOrigin, in bag, activeInteractingPlayers, InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("Queen Bee loot sink failed advertised Boss Bag delivery.");
            instancedItems = 1;
            recipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            Roll(VanillaQueenBeeItemIds.QueenBeeMasterTrophy, 1, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            short petStack = checked((short)rolls.NextInt32(1, 2));
            foreach (VanillaQueenBeeLootPlayer player in activeInteractingPlayers)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                NpcLootWorldItemOrigin origin = player.Origin;
                var pet = new NpcLootDrop(VanillaQueenBeeItemIds.QueenBeePetItem, petStack);
                if (!sink.TryDeliverWorldItem(in origin, in pet, rolls))
                    throw new InvalidOperationException("Queen Bee loot sink failed advertised Master pet delivery.");
                worldItems++;
                petDrops++;
            }
        }
        else if (!context.IsExpertMode)
        {
            Roll(VanillaQueenBeeItemIds.BeeMask, 7, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            DropOneOf(ClassicWeaponOptions, 1, in npcOrigin, rolls, sink, ref worldItems);
            Roll(VanillaQueenBeeItemIds.HoneyComb, 3, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            Roll(VanillaQueenBeeItemIds.Nectar, 15, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            Roll(VanillaQueenBeeItemIds.HoneyedGoggles, 20, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
            Roll(VanillaQueenBeeItemIds.QueenOfBees, 15, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);

            if (rolls.RollLuck(3) == 0)
                DropGuaranteed(VanillaQueenBeeItemIds.HiveWand, in npcOrigin, rolls, sink, ref worldItems);
            else if (rolls.NextInt32(0, 2) == 0)
                DropOneOf(VanityOptions, 1, in npcOrigin, rolls, sink, ref worldItems);

            Roll(VanillaQueenBeeItemIds.Beenade, 4, 3, 10, 30, in npcOrigin, rolls, sink, ref worldItems);
            Roll(VanillaQueenBeeItemIds.BeeWax, 1, 1, 17, 30, in npcOrigin, rolls, sink, ref worldItems);
        }

        Roll(VanillaQueenBeeItemIds.QueenBeeTrophy, 10, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);
        result = new QueenBeeLootExecutionResult(worldItems, instancedItems, recipients, petDrops);
        return result.IsValid;
    }

    private static bool CanDeliverAll(in VanillaQueenBeeLootContext context, IQueenBeeLootDeliverySink sink)
    {
        if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaQueenBeeItemIds.QueenBeeBossBag)) return false;
        if (context.IsMasterMode && (!sink.CanDeliverWorldItem(VanillaQueenBeeItemIds.QueenBeeMasterTrophy) || !sink.CanDeliverWorldItem(VanillaQueenBeeItemIds.QueenBeePetItem))) return false;
        ItemTypeId[] common = [VanillaQueenBeeItemIds.QueenBeeTrophy];
        foreach (ItemTypeId item in common) if (!sink.CanDeliverWorldItem(item)) return false;
        if (!context.IsExpertMode)
        {
            ItemTypeId[] classic = [VanillaQueenBeeItemIds.BeeMask, VanillaQueenBeeItemIds.BeeGun, VanillaQueenBeeItemIds.BeeKeeper, VanillaQueenBeeItemIds.BeesKnees, VanillaQueenBeeItemIds.HoneyComb, VanillaQueenBeeItemIds.Nectar, VanillaQueenBeeItemIds.HoneyedGoggles, VanillaQueenBeeItemIds.QueenOfBees, VanillaQueenBeeItemIds.HiveWand, VanillaQueenBeeItemIds.BeeHat, VanillaQueenBeeItemIds.BeeShirt, VanillaQueenBeeItemIds.BeePants, VanillaQueenBeeItemIds.Beenade, VanillaQueenBeeItemIds.BeeWax];
            foreach (ItemTypeId item in classic) if (!sink.CanDeliverWorldItem(item)) return false;
        }
        return true;
    }

    private static void Roll(ItemTypeId item, int denominator, int numerator, int minStack, int maxStack, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IQueenBeeLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) >= numerator) return;
        short stack = checked((short)rolls.NextInt32(minStack, checked(maxStack + 1)));
        Deliver(item, stack, in origin, rolls, sink, ref count);
    }

    private static void DropGuaranteed(ItemTypeId item, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IQueenBeeLootDeliverySink sink, ref int count) =>
        Deliver(item, checked((short)rolls.NextInt32(1, 2)), in origin, rolls, sink, ref count);

    private static void DropOneOf(ReadOnlySpan<ItemTypeId> options, int denominator, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IQueenBeeLootDeliverySink sink, ref int count)
    {
        if (rolls.NextInt32(0, denominator) != 0) return;
        ItemTypeId item = options[rolls.NextInt32(0, options.Length)];
        Deliver(item, checked((short)rolls.NextInt32(1, 2)), in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IQueenBeeLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Queen Bee loot sink failed advertised world-item support for {item.Value}.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaQueenBeeLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaQueenBeeLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous) return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
