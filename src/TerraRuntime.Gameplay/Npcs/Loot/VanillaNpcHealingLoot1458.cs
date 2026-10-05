using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Gameplay.Npcs.Loot;

public readonly record struct VanillaNpcHealingContext1458(
    NpcTypeId Type, NpcNetId NetId, int LifeMax, int Damage,
    bool NeedsLife, bool NeedsMana, bool ExpertMode, bool LifeEligibilityKnown = true);

/// <summary>Source NPC.NPCLoot_DropHeals, after the money phase.</summary>
public static class VanillaNpcHealingLoot1458
{
    public const int MaximumHealingDrops = 13;

    public static bool TryExecute(in VanillaNpcHealingContext1458 context,
        in NpcLootWorldItemOrigin origin, INpcLootRollSource random,
        IBossRecoveryLootDeliverySink1458 sink)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(sink);
        if (!origin.IsValid ||
            !sink.CanDeliverWorldItem(VanillaBossRecoveryItemIds1458.Star) ||
            !sink.CanDeliverWorldItem(VanillaWallOfFleshItemIds.Heart))
            return false;

        if (context.NetId.Value is not (16 or 81 or 121))
        {
            // Offers precede combat and vitals checks, even when no item can drop.
            if (random.RollLuck(6) == 0 && context.LifeMax > 1 && context.Damage > 0)
            {
                if (random.NextInt32(0, 2) == 0 && context.NeedsMana)
                {
                    if (!TryDrop(VanillaBossRecoveryItemIds1458.Star, in origin, random, sink))
                        return false;
                }
                else if (random.NextInt32(0, 2) == 0)
                {
                    if (!context.LifeEligibilityKnown) return false;
                    if (context.NeedsLife && !TryDrop(VanillaWallOfFleshItemIds.Heart, in origin, random, sink))
                        return false;
                }
            }
            if (random.RollLuck(2) == 0 && context.LifeMax > 1 && context.Damage > 0 && context.NeedsMana &&
                !TryDrop(VanillaBossRecoveryItemIds1458.Star, in origin, random, sink))
                return false;
        }

        if (context.Type.Value == 267 || context.Type.Value is >= 13 and <= 15)
        {
            int denominator = context.Type.Value == 267 ? 2 : 4;
            if (random.NextInt32(0, denominator) != 0) return true;
            if (!context.LifeEligibilityKnown) return false;
            return !context.NeedsLife || TryDrop(VanillaWallOfFleshItemIds.Heart, in origin, random, sink);
        }
        int hearts = context.Type.Value switch
        {
            >= 305 and <= 314 or 329 or 330 => random.RollLuck(4) == 0 ? 1 : 0,
            326 => random.RollLuck(6) == 0 ? 1 : 0,
            315 => 1,
            341 => random.NextInt32(5, 11),
            >= 338 and <= 340 => random.RollLuck(5) == 0 ? 1 : 0,
            342 => random.NextInt32(0, 3) != 0 ? 1 : 0,
            325 or 327 or 344 or 345 or 346 => random.NextInt32(0, 6) + 6,
            >= 116 and <= 119 => !context.ExpertMode || random.NextInt32(0, 5) == 0 ? 1 : 0,
            139 => random.NextInt32(0, 2) == 0 ? 1 : 0,
            _ => 0
        };
        for (int index = 0; index < hearts; index++)
            if (!TryDrop(VanillaWallOfFleshItemIds.Heart, in origin, random, sink))
                return false;
        return true;
    }

    private static bool TryDrop(ItemTypeId type, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource random, IBossRecoveryLootDeliverySink1458 sink)
    {
        var drop = new NpcLootDrop(type, 1);
        return sink.TryDeliverWorldItem(in origin, in drop, random);
    }
}
