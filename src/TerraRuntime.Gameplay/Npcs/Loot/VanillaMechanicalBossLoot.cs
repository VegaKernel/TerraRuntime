using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaMechanicalBossLootContext(
    NpcTypeId Type, bool IsExpertMode, bool IsMasterMode, bool OtherTwinActive = false)
{
    public bool IsValid => (!IsMasterMode || IsExpertMode) &&
        (!OtherTwinActive || VanillaMechanicalBossLootEvaluator.IsTwin(Type));
}

public readonly record struct VanillaMechanicalBossLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid => Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);
    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IMechanicalBossLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop,
        ReadOnlySpan<VanillaMechanicalBossLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct MechanicalBossLootExecutionResult(
    int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterPetDropCount);

/// <summary>
/// ItemDropDatabase.RegisterBoss_SkeletronPrime/TheDestroyer/Twins plus later trophies, TerrariaServer 1.4.5.8.
/// MissingTwin gates encounter rewards, never each eye's independent trophy. Mechdusa's conditional Waffle
/// Iron rule remains unadmitted until its world-owned feature condition and item prefix semantics are verified.
/// Global coins/hearts and bag opening are outside this ordinary NPC-specific slice.
/// </summary>
public static class VanillaMechanicalBossLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool IsTwin(NpcTypeId type) =>
        type == VanillaNpcIds.Retinazer || type == VanillaNpcIds.Spazmatism;

    public static bool IsRoot(NpcTypeId type) =>
        IsTwin(type) || type == VanillaNpcIds.Destroyer || type == VanillaNpcIds.SkeletronPrime;

    public static bool TryExecute(in VanillaMechanicalBossLootContext context,
        in NpcLootWorldItemOrigin origin, ReadOnlySpan<VanillaMechanicalBossLootPlayer> players,
        INpcLootRollSource rolls, IMechanicalBossLootDeliverySink sink,
        out MechanicalBossLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !origin.IsValid || !TryGet(context.Type, out Profile profile) ||
            !ArePlayersSourceOrdered(players) || !CanDeliverAll(in context, in profile, sink))
            return false;

        int world = 0, instanced = 0, recipients = 0, pets = 0;
        if (!context.OtherTwinActive)
        {
            if (context.IsExpertMode)
            {
                rolls.NextInt32(0, 1);
                var bag = new NpcLootDrop(profile.Bag, checked((short)rolls.NextInt32(1, 2)));
                if (!sink.TryDeliverInstanced(in origin, in bag, players, InstancedItemSlotLeaseTicks, rolls))
                    throw new InvalidOperationException("Mechanical boss sink failed advertised Boss Bag delivery.");
                instanced = 1;
                recipients = players.Length;
            }

            // Twins registers Classic before Master; conditions are disjoint and consume no RNG themselves.
            if (!context.IsExpertMode)
            {
                RollCommon(profile.Mask, 7, 1, 1, in origin, rolls, sink, ref world);
                RollCommon(VanillaMechanicalBossItemIds.HallowedBar, 1, 15, 30, in origin, rolls, sink, ref world);
                RollCommon(profile.Soul, 1, 25, 40, in origin, rolls, sink, ref world);
            }
            if (context.IsMasterMode)
            {
                RollCommon(profile.Relic, 1, 1, 1, in origin, rolls, sink, ref world);
                short petStack = checked((short)rolls.NextInt32(1, 2));
                foreach (VanillaMechanicalBossLootPlayer player in players)
                {
                    if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                        continue;
                    Deliver(profile.Pet, petStack, player.Origin, rolls, sink, ref world);
                    pets++;
                }
            }
        }
        RollCommon(profile.Trophy, 10, 1, 1, in origin, rolls, sink, ref world);
        result = new MechanicalBossLootExecutionResult(world, instanced, recipients, pets);
        return true;
    }

    private static bool TryGet(NpcTypeId type, out Profile profile)
    {
        if (IsTwin(type))
            profile = new(VanillaMechanicalBossItemIds.TwinsBossBag,
                VanillaMechanicalBossItemIds.TwinsMasterTrophy, VanillaMechanicalBossItemIds.TwinsPetItem,
                VanillaMechanicalBossItemIds.TwinMask, VanillaMechanicalBossItemIds.SoulOfSight,
                type == VanillaNpcIds.Retinazer ? VanillaMechanicalBossItemIds.RetinazerTrophy :
                    VanillaMechanicalBossItemIds.SpazmatismTrophy);
        else if (type == VanillaNpcIds.Destroyer)
            profile = new(VanillaMechanicalBossItemIds.DestroyerBossBag,
                VanillaMechanicalBossItemIds.DestroyerMasterTrophy, VanillaMechanicalBossItemIds.DestroyerPetItem,
                VanillaMechanicalBossItemIds.DestroyerMask, VanillaMechanicalBossItemIds.SoulOfMight,
                VanillaMechanicalBossItemIds.DestroyerTrophy);
        else if (type == VanillaNpcIds.SkeletronPrime)
            profile = new(VanillaMechanicalBossItemIds.SkeletronPrimeBossBag,
                VanillaMechanicalBossItemIds.SkeletronPrimeMasterTrophy, VanillaMechanicalBossItemIds.SkeletronPrimePetItem,
                VanillaMechanicalBossItemIds.SkeletronPrimeMask, VanillaMechanicalBossItemIds.SoulOfFright,
                VanillaMechanicalBossItemIds.SkeletronPrimeTrophy);
        else
        {
            profile = default;
            return false;
        }
        return true;
    }

    private static bool CanDeliverAll(in VanillaMechanicalBossLootContext context, in Profile profile,
        IMechanicalBossLootDeliverySink sink)
    {
        if (!sink.CanDeliverWorldItem(profile.Trophy))
            return false;
        if (context.OtherTwinActive)
            return true;
        if (context.IsExpertMode && !sink.CanDeliverInstanced(profile.Bag))
            return false;
        if (context.IsMasterMode && (!sink.CanDeliverWorldItem(profile.Relic) || !sink.CanDeliverWorldItem(profile.Pet)))
            return false;
        return context.IsExpertMode || (sink.CanDeliverWorldItem(profile.Mask) &&
            sink.CanDeliverWorldItem(VanillaMechanicalBossItemIds.HallowedBar) && sink.CanDeliverWorldItem(profile.Soul));
    }

    private static void RollCommon(ItemTypeId item, int denominator, int min, int max,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IMechanicalBossLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) == 0)
            Deliver(item, checked((short)rolls.NextInt32(min, max + 1)), in origin, rolls, sink, ref count);
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IMechanicalBossLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Mechanical boss sink failed advertised item {item.Value} delivery.");
        count++;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaMechanicalBossLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaMechanicalBossLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous)
                return false;
            previous = player.Slot.Value;
        }
        return true;
    }

    private readonly record struct Profile(ItemTypeId Bag, ItemTypeId Relic, ItemTypeId Pet,
        ItemTypeId Mask, ItemTypeId Soul, ItemTypeId Trophy);
}
