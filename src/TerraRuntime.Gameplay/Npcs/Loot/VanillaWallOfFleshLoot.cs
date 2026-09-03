using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaWallOfFleshLootContext(bool IsExpertMode, bool IsMasterMode)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaWallOfFleshLootPlayer(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid => Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots && float.IsFinite(CenterX) && float.IsFinite(CenterY);
    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IWallOfFleshLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);
    bool CanDeliverWorldItem(ItemTypeId itemType);
    bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, ReadOnlySpan<VanillaWallOfFleshLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct WallOfFleshLootExecutionResult(int WorldItemCount, int InstancedItemCount, int InstancedRecipientCount, int MasterMountDropCount)
{
    public bool IsValid => WorldItemCount >= 0 && InstancedItemCount >= 0 && InstancedRecipientCount >= 0 && MasterMountDropCount >= 0 && MasterMountDropCount <= WorldItemCount;
}

/// <summary>
/// Source-order server loot for TerrariaServer 1.4.5.8 RegisterBoss_WOF plus its general boss-trophy rule.
/// Presentation-only death effects are deliberately absent.
/// </summary>
public static class VanillaWallOfFleshLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterMountChanceDenominator = 4;

    private static readonly ItemTypeId[] Emblems =
    [
        VanillaWallOfFleshItemIds.WarriorEmblem,
        VanillaWallOfFleshItemIds.RangerEmblem,
        VanillaWallOfFleshItemIds.SorcererEmblem,
        VanillaWallOfFleshItemIds.SummonerEmblem
    ];

    private static readonly ItemTypeId[] Weapons =
    [
        VanillaWallOfFleshItemIds.BreakerBlade,
        VanillaWallOfFleshItemIds.ClockworkAssaultRifle,
        VanillaWallOfFleshItemIds.LaserRifle,
        VanillaWallOfFleshItemIds.Firecracker
    ];

    public static bool TryExecute(
        in VanillaWallOfFleshLootContext context,
        in NpcLootWorldItemOrigin origin,
        ReadOnlySpan<VanillaWallOfFleshLootPlayer> players,
        INpcLootRollSource rolls,
        IWallOfFleshLootDeliverySink sink,
        out WallOfFleshLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        if (!context.IsValid || !origin.IsValid || !PlayersAreOrdered(players) || !CanDeliverAll(in context, sink))
            return false;

        int world = 0;
        int instanced = 0;
        int recipients = 0;
        int masterMounts = 0;

        if (context.IsExpertMode)
        {
            rolls.NextInt32(0, 1);
            var bag = new NpcLootDrop(VanillaWallOfFleshItemIds.WallOfFleshBossBag, checked((short)rolls.NextInt32(1, 2)));
            if (!sink.TryDeliverInstanced(in origin, in bag, players, InstancedItemSlotLeaseTicks, rolls))
                throw new InvalidOperationException("Wall of Flesh loot sink failed advertised Boss Bag delivery.");
            instanced = 1;
            recipients = players.Length;
        }

        if (context.IsMasterMode)
        {
            DropGuaranteed(VanillaWallOfFleshItemIds.WallOfFleshRelic, in origin, rolls, sink, ref world);
            short stack = checked((short)rolls.NextInt32(1, 2));
            for (int index = 0; index < players.Length; index++)
            {
                if (rolls.NextInt32(0, MasterMountChanceDenominator) != 0)
                    continue;
                NpcLootWorldItemOrigin playerOrigin = players[index].Origin;
                var mount = new NpcLootDrop(VanillaWallOfFleshItemIds.GoatSkull, stack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in mount, rolls))
                    throw new InvalidOperationException("Wall of Flesh loot sink failed advertised Master mount delivery.");
                world++;
                masterMounts++;
            }
        }
        else if (!context.IsExpertMode)
        {
            Roll(VanillaWallOfFleshItemIds.FleshMask, 7, in origin, rolls, sink, ref world);
            DropGuaranteed(VanillaWallOfFleshItemIds.Pwnhammer, in origin, rolls, sink, ref world);
            DropOneOf(Emblems, in origin, rolls, sink, ref world);
            DropOneOf(Weapons, in origin, rolls, sink, ref world);
        }

        Roll(VanillaWallOfFleshItemIds.WallOfFleshTrophy, 10, in origin, rolls, sink, ref world);
        result = new WallOfFleshLootExecutionResult(world, instanced, recipients, masterMounts);
        return result.IsValid;
    }

    private static bool CanDeliverAll(in VanillaWallOfFleshLootContext context, IWallOfFleshLootDeliverySink sink)
    {
        if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaWallOfFleshItemIds.WallOfFleshBossBag)) return false;
        if (!sink.CanDeliverWorldItem(VanillaWallOfFleshItemIds.WallOfFleshTrophy)) return false;
        if (context.IsMasterMode && (!sink.CanDeliverWorldItem(VanillaWallOfFleshItemIds.WallOfFleshRelic) || !sink.CanDeliverWorldItem(VanillaWallOfFleshItemIds.GoatSkull))) return false;
        if (!context.IsExpertMode)
        {
            ItemTypeId[] ids = [VanillaWallOfFleshItemIds.FleshMask, VanillaWallOfFleshItemIds.Pwnhammer,
                VanillaWallOfFleshItemIds.WarriorEmblem, VanillaWallOfFleshItemIds.RangerEmblem, VanillaWallOfFleshItemIds.SorcererEmblem, VanillaWallOfFleshItemIds.SummonerEmblem,
                VanillaWallOfFleshItemIds.BreakerBlade, VanillaWallOfFleshItemIds.ClockworkAssaultRifle, VanillaWallOfFleshItemIds.LaserRifle, VanillaWallOfFleshItemIds.Firecracker];
            foreach (ItemTypeId id in ids) if (!sink.CanDeliverWorldItem(id)) return false;
        }
        return true;
    }

    private static void Roll(ItemTypeId item, int denominator, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IWallOfFleshLootDeliverySink sink, ref int count)
    {
        if (rolls.RollLuck(denominator) != 0) return;
        DropGuaranteed(item, in origin, rolls, sink, ref count);
    }

    private static void DropOneOf(ReadOnlySpan<ItemTypeId> options, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IWallOfFleshLootDeliverySink sink, ref int count)
    {
        rolls.NextInt32(0, 1);
        ItemTypeId item = options[rolls.NextInt32(0, options.Length)];
        DropGuaranteed(item, in origin, rolls, sink, ref count);
    }

    private static void DropGuaranteed(ItemTypeId item, in NpcLootWorldItemOrigin origin, INpcLootRollSource rolls, IWallOfFleshLootDeliverySink sink, ref int count)
    {
        var drop = new NpcLootDrop(item, checked((short)rolls.NextInt32(1, 2)));
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Wall of Flesh loot sink failed advertised world-item support for {item.Value}.");
        count++;
    }

    private static bool PlayersAreOrdered(ReadOnlySpan<VanillaWallOfFleshLootPlayer> players)
    {
        int previous = -1;
        foreach (VanillaWallOfFleshLootPlayer player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous) return false;
            previous = player.Slot.Value;
        }
        return true;
    }
}
