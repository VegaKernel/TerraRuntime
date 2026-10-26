namespace TerraRuntime.Gameplay.Players;

/// <summary>Known source scene at a local client's fishing check; no client random cursor is trusted.</summary>
public readonly record struct FishingClientCatchContext1458(int X, int Y, float BobberTopY,
    FishingConditions1458 Conditions, FishingWorld1458 World, FishingBiomes1458 Biomes,
    FishingPond1458 Pond, float Luck, int AvailableQuestFish, bool LavaAccessory = false);

[Flags]
public enum FishingBaitConsumption1458 : byte { None = 0, Conserved = 1, Consumed = 2 }

public static partial class FishingRules1458
{
    private const int CommonOutcome = 1, UncommonOutcome = 2, RareOutcome = 4,
        VeryRareOutcome = 8, LegendaryOutcome = 16, CrateOutcome = 32;
    /// <summary>Nonzero source probability, including earlier matching rules that can prevent reaching a later row.</summary>
    public static bool IsPossibleClientCatch(in FishingClientCatchContext1458 context, int itemType, int npcType = 0)
    {
        if ((itemType > 0) == (npcType > 0) || itemType < 0 || npcType < 0 || context.Pond.WaterTiles < 75 ||
            context.X < 0 || context.Y < 0 || context.X >= context.World.Width || context.Y >= context.World.Height ||
            !float.IsFinite(context.BobberTopY) || !float.IsFinite(context.Luck) ||
            GetBaitPower(context.Conditions.BaitItemType) <= 0 || context.Conditions.PolePower <= 0) return false;
        if (context.Conditions.BaitItemType == 2673)
            return itemType == 0 && npcType == 370 && !context.World.DukeFishronActive &&
                context.Pond.WaterTiles > 1000 && (context.X < 380 || context.X > context.World.Width - 380);
        if (context.Conditions.FinalFishingLevel <= 0) return false;
        if (npcType > 0)
        {
            if (context.Pond.Lava || context.Pond.Honey && !context.World.NotTheBees || !context.World.BloodMoon || context.World.DayTime) return false;
            return npcType switch
            {
                682 => !context.World.RedSlimeUnlocked,
                586 or 587 => true,
                618 or 620 or 621 => context.World.HardMode,
                _ => false
            };
        }
        float scale = context.World.Width / 4200f;
        float atmosphere = (float)((context.BobberTopY / 16 - (60f + 10f * scale * scale)) / (context.World.Surface / 6));
        int needed = (int)(300 * Math.Clamp(atmosphere, .25f, 1));
        if (needed <= 0) return false;
        int level = context.Conditions.FinalFishingLevel + (context.Pond.ChumCount > 0 ? 11 : 0) +
            (context.Pond.ChumCount > 1 ? 6 : 0) + (context.Pond.ChumCount > 2 ? 3 : 0);
        if (context.Pond.WaterTiles < needed) level = (int)(level * (context.Pond.WaterTiles / (float)needed));
        if (level <= 0) return false;
        // Only junk feasibility depends on the random power adjustment. All five rarity
        // flags and crate are independent nonzero/non-one probabilities at positive power.
        int lowestLevel = context.Luck < 0 ? (int)(level * .6) : context.Luck >= 1 ? (int)(level * 1.1) : level;
        bool canJunk = lowestLevel <= 48 && context.Pond.WaterTiles < needed;
        int height = context.Y < context.World.Surface * .5 ? 0 : context.Y < context.World.Surface ? 1 :
            context.Y < context.World.RockLayer ? (context.World.Remix ? 3 : 2) :
            context.Y < context.World.Height - 300 ? (context.World.Remix ? 2 : 3) : 4;
        // Seven bounded binary source branches; impossible branches are excluded, not
        // interpreted as permission to invent a biome, depth, liquid or quest identity.
        for (int branches = 0; branches < 128; branches++)
        {
            var pond = context.Pond;
            if ((branches & 1) != 0) { if (!context.World.NotTheBees || !pond.Honey) continue; pond = pond with { Honey = false }; }
            int rolledHeight = height;
            if ((branches & 2) != 0) { if (!context.World.Remix || height != 2) continue; rolledHeight = 1; }
            var biomes = context.Biomes;
            if (!context.World.SkeletronDefeated) biomes &= ~FishingBiomes1458.Dungeon;
            if ((branches & 4) != 0) { if (!context.World.NotTheBees || context.World.Remix || !biomes.HasFlag(FishingBiomes1458.Jungle)) continue; biomes &= ~FishingBiomes1458.Jungle; }
            if (context.World.Remix && rolledHeight == 0)
            { if ((branches & 8) != 0) continue; biomes &= ~(FishingBiomes1458.Corruption | FishingBiomes1458.Crimson); }
            else if ((biomes & (FishingBiomes1458.Corruption | FishingBiomes1458.Crimson)) ==
                     (FishingBiomes1458.Corruption | FishingBiomes1458.Crimson))
                biomes &= (branches & 8) == 0 ? ~FishingBiomes1458.Corruption : ~FishingBiomes1458.Crimson;
            else if ((branches & 8) != 0) continue;
            if ((branches & 16) != 0)
            { if (!biomes.HasFlag(FishingBiomes1458.Snow) || !biomes.HasFlag(FishingBiomes1458.Jungle)) continue; biomes &= ~FishingBiomes1458.Snow; }
            if (biomes.HasFlag(FishingBiomes1458.Dungeon)) biomes &= ~FishingBiomes1458.Desert;
            bool infectedDesert = (branches & 32) != 0;
            if (infectedDesert && !biomes.HasFlag(FishingBiomes1458.Desert)) continue;
            bool remixOcean = (branches & 64) != 0;
            if (remixOcean && (!context.World.Remix || rolledHeight != 1 || context.Y < context.World.RockLayer)) continue;
            for (int junk = 0; junk < (canJunk ? 2 : 1); junk++)
            for (int flags = 0; flags < 64; flags++)
            {
                var tiers = new FishingDropLevels1458((flags & CommonOutcome) != 0, (flags & UncommonOutcome) != 0,
                    (flags & RareOutcome) != 0, (flags & VeryRareOutcome) != 0,
                    (flags & LegendaryOutcome) != 0, (flags & CrateOutcome) != 0);
                var attempt = new FishingAttempt1458(context.X, context.Y, level, rolledHeight, needed, pond,
                    context.Conditions, context.World, context.Biomes, biomes, infectedDesert, remixOcean, junk != 0,
                    context.Conditions.PoleItemType == 2422 || IsLavaBait(context.Conditions.BaitItemType) || context.LavaAccessory,
                    context.AvailableQuestFish, tiers);
                if (IsPossibleItemDrop(attempt, itemType)) return true;
            }
        }
        return false;
    }

    /// <summary>Possibility in one source context branch, preserving guaranteed earlier-rule occlusion.</summary>
    public static bool IsPossibleItemDrop(in FishingAttempt1458 attempt, int itemType, int rolledEnemy = 0)
    {
        if (itemType <= 0) return false;
        C matching = GetMatchingConditions(attempt, rolledEnemy);
        foreach (DropRow row in DropRows)
        {
            if ((matching & row.Conditions) != row.Conditions || row.QuestFish != 0 && row.QuestFish != attempt.QuestFish ||
                row.Numerator <= 0 || !MatchesTier(row.Tier, attempt.Levels)) continue;
            foreach (int candidate in row.Items) if (candidate == itemType) return true;
            // A guaranteed prior result, including empty liquid terminators,
            // prevents every later rule for this exact branch assignment.
            if (row.Numerator >= row.Denominator) break;
        }
        return false;
    }

    public static FishingBaitConsumption1458 GetPossibleClientBaitConsumption(int baitType, int itemType, int npcType)
    {
        if (GetBaitPower(baitType) <= 0) return FishingBaitConsumption1458.None;
        if (baitType == 2673) return FishingBaitConsumption1458.Consumed;
        if (baitType == 2895) return FishingBaitConsumption1458.Conserved | FishingBaitConsumption1458.Consumed;
        if (npcType == 1) return FishingBaitConsumption1458.Consumed;
        if (baitType != 2895 && itemType is >= 2337 and <= 2339) return FishingBaitConsumption1458.Conserved;
        return FishingBaitConsumption1458.Conserved | FishingBaitConsumption1458.Consumed;
    }

    /// <summary>Fastest first attempt, assuming every source bonus succeeds; caller counts only settled-water frames.</summary>
    public static int GetMinimumClientFishingWaitFrames(int maximumFishingPower)
    {
        if (maximumFishingPower < 0) return int.MaxValue;
        long increment = (maximumFishingPower == 0 ? 62L : 64L) + maximumFishingPower / 30;
        return (int)((661 + increment - 1) / increment);
    }

    public static (int Minimum, int Maximum) GetClientCatchStackBounds(int itemType, int currentFishingPower)
    {
        if (currentFishingPower < 0) return default;
        if (itemType is not (3196 or 3197)) return (1, 1);
        int minimum = itemType == 3196 ? (currentFishingPower / 20 + 3) / 2 : (currentFishingPower / 4 + 15) / 2;
        int maximum = itemType == 3196 ? (currentFishingPower / 10 + 6) / 2 : (currentFishingPower / 2 + 40) / 2;
        if (currentFishingPower > 0) maximum += itemType == 3196 ? 4 : 24;
        return (minimum, maximum);
    }
}
