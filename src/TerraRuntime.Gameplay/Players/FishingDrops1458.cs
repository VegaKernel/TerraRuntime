namespace TerraRuntime.Gameplay.Players;

/// <summary>Cell information read from the existing guarded world owner.</summary>
public readonly record struct FishingWaterCell1458(byte Liquid, byte LiquidKind, bool Solid, int Chum = 0);
public delegate FishingWaterCell1458 FishingCellQuery1458(int x, int y);
public delegate bool FishingSolidRectangleQuery1458(int x, int y, int width, int height);

public static partial class FishingRules1458
{
    // Authoritative servers consolidate work originally performed by separate
    // local clients. Refuse oversized connected pools before an unbounded scan;
    // this is an explicit safety ceiling, not a vanilla water-count constant.
    public const int MaximumPondCellReads = 262_144;
    public static FishingPond1458 MeasurePond(int x, int y, int width, int height,
        FishingCellQuery1458 query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (x < 0 || x >= width || y < 0 || y >= height - 10) return default;
        GetPondWidth(x, y, width, query, out int left, out int right);
        int count = 0, chums = 0;
        int reads = 0;
        bool lava = false, honey = false;
        for (int column = left; column <= right; column++)
            for (int row = y; row < height - 10; row++)
            {
                if (++reads > MaximumPondCellReads) return default;
                var cell = query(column, row);
                if (cell.Liquid == 0 || cell.Solid) break;
                count++;
                chums += cell.Chum;
                lava |= cell.LiquidKind == 1;
                honey |= cell.LiquidKind == 2;
            }
        return new(honey ? (int)(count * 1.5) : count, lava, honey, chums);
    }

    private static void GetPondWidth(int x, int y, int width, FishingCellQuery1458 query,
        out int left, out int right)
    {
        left = x; right = x;
        while (left > 10)
        {
            var cell = query(left, y);
            if (cell.Liquid == 0 || cell.Solid) break;
            left--;
        }
        while (right < width - 10)
        {
            var cell = query(right, y);
            if (cell.Liquid == 0 || cell.Solid) break;
            right++;
        }
    }

    public static float GetWaterLine(int x, int y, float fallback, FishingCellQuery1458 query)
    {
        for (int row = y - 1; row <= y + 1; row++)
        {
            byte amount = query(x, row).Liquid;
            if (amount != 0) return (row + 1) * 16 - amount / 16;
        }
        return fallback;
    }

    public static bool TryPrepareAttempt(int x, int y, float bobberTopY,
        in FishingConditions1458 conditions, in FishingWorld1458 world,
        FishingBiomes1458 biomes, FishingPond1458 pond, float luck, bool cratePotion,
        bool lavaAccessory, int availableQuestFish, Random random, out FishingAttempt1458 attempt)
    {
        ArgumentNullException.ThrowIfNull(random);
        attempt = default;
        if (world.NotTheBees && random.Next(2) == 0) pond = pond with { Honey = false };
        if (pond.WaterTiles < 75 || conditions.FinalFishingLevel == 0 || conditions.BaitItemType == 2673)
            return false;
        int level = conditions.FinalFishingLevel;
        if (pond.ChumCount > 0) level += 11;
        if (pond.ChumCount > 1) level += 6;
        if (pond.ChumCount > 2) level += 3;
        float sizeScale = world.Width / 4200f;
        sizeScale *= sizeScale;
        float atmosphere = (float)((bobberTopY / 16 - (60f + 10f * sizeScale)) / (world.Surface / 6));
        atmosphere = Math.Clamp(atmosphere, .25f, 1);
        int needed = (int)(300 * atmosphere);
        float quality = pond.WaterTiles / (float)needed;
        if (quality < 1) level = (int)(level * quality);
        if (luck < 0)
        {
            if ((float)random.NextDouble() < -luck)
                level = (int)(level * (.9 - (float)random.NextDouble() * .3));
        }
        else if ((float)random.NextDouble() < luck)
            level = (int)(level * (1.1 + (float)random.NextDouble() * .3));
        int height = y < world.Surface * .5 ? 0 : y < world.Surface ? 1 : y < world.RockLayer
            ? (world.Remix ? 3 : 2) : y < world.Height - 300 ? (world.Remix ? 2 : 3) : 4;
        if (world.Remix && height == 2 && random.Next(2) == 0) height = 1;
        bool junk = random.Next(50) > level && random.Next(50) > level && pond.WaterTiles < needed;
        // At least one point is required by the source's rarity divisors. Malformed or
        // unsupported negative skill input is refused rather than creating a second rule.
        if (level <= 0) return false;
        var levels = RollDropLevels(level, cratePotion, random);
        FishingBiomes1458 rolled = biomes;
        if (!world.SkeletronDefeated) rolled &= ~FishingBiomes1458.Dungeon;
        if (world.NotTheBees && !world.Remix && random.Next(2) == 0) rolled &= ~FishingBiomes1458.Jungle;
        if (world.Remix && height == 0) rolled &= ~(FishingBiomes1458.Corruption | FishingBiomes1458.Crimson);
        else if ((rolled & (FishingBiomes1458.Corruption | FishingBiomes1458.Crimson)) ==
            (FishingBiomes1458.Corruption | FishingBiomes1458.Crimson))
            rolled &= random.Next(2) == 0 ? ~FishingBiomes1458.Crimson : ~FishingBiomes1458.Corruption;
        if (rolled.HasFlag(FishingBiomes1458.Snow) && rolled.HasFlag(FishingBiomes1458.Jungle)
            && random.Next(2) == 0) rolled &= ~FishingBiomes1458.Snow;
        if (rolled.HasFlag(FishingBiomes1458.Dungeon)) rolled &= ~FishingBiomes1458.Desert;
        bool remixOcean = world.Remix && height == 1 && y >= world.RockLayer && random.Next(3) == 0;
        bool infectedDesert = rolled.HasFlag(FishingBiomes1458.Desert) && random.Next(2) == 0;
        attempt = new(x, y, level, height, needed, pond, conditions, world, biomes, rolled,
            infectedDesert, remixOcean, junk, conditions.PoleItemType == 2422 ||
            IsLavaBait(conditions.BaitItemType) || lavaAccessory, availableQuestFish, levels)
        { LavaAccessory = lavaAccessory };
        return true;
    }

    public static FishingResult1458 RollSpecialBait(int x, in FishingPond1458 pond,
        in FishingWorld1458 world, Random random)
    {
        if (pond.WaterTiles < 75 || world.DukeFishronActive || pond.WaterTiles <= 1000 ||
            (x >= 380 && x <= world.Width - 380)) return default;
        return new(0, 370, random.Next(-180, -60) - 100, 2673, 0);
    }

    // Call ConsumePreviewRandom before this method: FishingCheck displays the eligible
    // fish before its bite-chance draw. Both source streams are supplied by runtime owners.
    public static FishingResult1458 RollOutcome(in FishingAttempt1458 attempt, Random mainRandom,
        Random dropRandom, bool sonarPotion = false)
    {
        if (mainRandom.Next(100) > (attempt.Level + 75) / 2) return default;
        int npc = RollEnemy(attempt, mainRandom);
        int item = RollItem(attempt, npc, dropRandom);
        float clock = 0;
        if (item > 0)
        {
            if (sonarPotion) { mainRandom.NextDouble(); mainRandom.NextDouble(); }
            clock = mainRandom.Next(-240, -90) - attempt.Level;
        }
        if (npc > 0) clock = mainRandom.Next(-240, -90) - attempt.Level;
        int lavaSources = (IsLavaBait(attempt.Conditions.BaitItemType) ? 1 : 0) +
            (attempt.Conditions.PoleItemType == 2422 ? 1 : 0) + (attempt.LavaAccessory ? 1 : 0);
        int waitBonus = item == 0 && npc == 0 && attempt.Pond.Lava
            ? lavaSources >= 3 ? 480 : lavaSources >= 2 ? 240 : 0 : 0;
        return new(item, npc, clock, attempt.Conditions.BaitItemType, attempt.Level) { WaitBonus = waitBonus };
    }

    private static int RollEnemy(in FishingAttempt1458 attempt, Random random)
    {
        if (attempt.Pond.Lava || attempt.Pond.Honey || !attempt.World.BloodMoon || attempt.World.DayTime)
            return 0;
        if (random.Next(attempt.Conditions.PoleItemType == 4325 ? 3 : 6) != 0) return 0;
        if (!attempt.World.RedSlimeUnlocked && random.Next(5) == 0) return 682;
        if (!attempt.World.HardMode) return random.Next(2) == 0 ? 586 : 587;
        int npc = random.Next(4) switch { 0 => 620, 1 => 621, 2 => 586, _ => 587 };
        return random.Next(10) == 0 ? 618 : npc;
    }

    public static int RollItem(in FishingAttempt1458 attempt, int rolledEnemy, Random random)
    {
        C matches = GetMatchingConditions(attempt, rolledEnemy);
        foreach (DropRow row in DropRows)
        {
            if ((matches & row.Conditions) != row.Conditions ||
                row.QuestFish != 0 && row.QuestFish != attempt.QuestFish) continue;
            // FishDropRule.Attempt consumes probability BEFORE checking the rarity flag,
            // and even a one-element item group consumes a separate sample.
            if (random.Next(row.Denominator) >= row.Numerator || !MatchesTier(row.Tier, attempt.Levels)) continue;
            return row.Items.Length == 0 ? 0 : row.Items[random.Next(row.Items.Length)];
        }
        return 0;
    }

    public static void ConsumePreviewRandom(in FishingAttempt1458 attempt, Random mainRandom,
        Random dropRandom, FishingCellQuery1458 query, FishingSolidRectangleQuery1458 solidRectangle)
    {
        C conditions = GetMatchingConditions(attempt, 0);
        bool any = false;
        foreach (DropRow row in DropRows)
        {
            if ((conditions & row.Conditions) != row.Conditions ||
                row.QuestFish != 0 && row.QuestFish != attempt.QuestFish) continue;
            any = true;
            if (row.Items.Length != 0) dropRandom.Next(row.Items.Length);
            if (row.Items.Length == 0 || row.Tier == T.Any && row.Denominator == row.Numerator) break;
        }
        if (!any) return;
        GetPondWidth(attempt.X, attempt.Y, attempt.World.Width, query, out int left, out int right);
        left = Math.Max(attempt.X - 10, left) + 1;
        right = Math.Min(attempt.X + 10, right) - 1;
        for (int x = left; x <= right; x++)
        {
            int depth = 0;
            for (int y = attempt.Y; y < attempt.World.Height - 10 && depth < 10; y++)
            {
                var cell = query(x, y);
                if (cell.Liquid == 0 || cell.Solid) break;
                depth++;
            }
            if (depth < 3) continue;
            int maxY = depth * 16 - 3;
            if (mainRandom.Next(5) == 0)
            {
                mainRandom.Next(4, 13); mainRandom.Next(20, maxY); mainRandom.NextDouble();
            }
            if (mainRandom.Next(10) != 0) continue;
            mainRandom.Next(4, 13); mainRandom.Next(20, maxY); mainRandom.NextDouble();
            if (mainRandom.Next(4) != 0) continue;
            int first = mainRandom.Next(left, right + 1), second = mainRandom.Next(left, right + 1);
            int top = attempt.Y * 16 - 2, headroom = 0;
            while (headroom < 6 && !solidRectangle(Math.Min(first, second) * 16 + 2,
                top, Math.Abs(first - second) * 16 - 4, 12))
            { headroom++; top -= 16; }
            if (headroom < 3) continue;
            int extra = mainRandom.Next(1, 4);
            mainRandom.Next(16 * (3 + extra), (extra + headroom) * 16);
            mainRandom.Next(9); mainRandom.Next(9);
        }
    }

    private static bool MatchesTier(T tier, in FishingDropLevels1458 levels) => tier switch
    {
        T.Any => true, T.Common => levels.Common, T.Uncommon => levels.Uncommon,
        T.Rare => levels.Rare, T.VeryRare => levels.VeryRare, T.Legendary => levels.Legendary,
        T.UncommonOrCommon => levels.Uncommon || levels.Common,
        T.BombRarityOfNotLegendaryAndNotVeryRareAndUncommon => !levels.Legendary && !levels.VeryRare && levels.Uncommon,
        _ => false
    };

    private static C GetMatchingConditions(in FishingAttempt1458 a, int enemy)
    {
        C result = 0;
        void Set(C flag, bool value) { if (value) result |= flag; }
        Set(C.HardMode, a.World.HardMode); Set(C.EarlyMode, !a.World.HardMode);
        Set(C.InLava, a.Pond.Lava); Set(C.InHoney, a.Pond.Honey); Set(C.Junk, a.Junk);
        Set(C.Crate, a.Levels.Crate); Set(C.AnyEnemies, enemy > 0); Set(C.CanFishInLava, a.CanFishInLava);
        Set(C.Dungeon, a.TrueBiomes.HasFlag(FishingBiomes1458.Dungeon) && a.World.SkeletronDefeated);
        Set(C.Beach, a.TrueBiomes.HasFlag(FishingBiomes1458.Beach)); Set(C.Hallow, a.TrueBiomes.HasFlag(FishingBiomes1458.Hallow));
        Set(C.GlowingMushrooms, a.TrueBiomes.HasFlag(FishingBiomes1458.GlowingMushrooms));
        Set(C.TrueDesert, a.TrueBiomes.HasFlag(FishingBiomes1458.Desert)); Set(C.TrueSnow, a.TrueBiomes.HasFlag(FishingBiomes1458.Snow));
        Set(C.Remix, a.World.Remix); Set(C.Height1, a.HeightLevel == 1); Set(C.Height1And2, a.HeightLevel is 1 or 2);
        Set(C.HeightAbove1, a.HeightLevel > 1); Set(C.HeightAboveAnd1, a.HeightLevel >= 1); Set(C.HeightUnder2, a.HeightLevel < 2);
        Set(C.HeightAbove2, a.HeightLevel > 2); Set(C.Height0, a.HeightLevel == 0); Set(C.Height2, a.HeightLevel == 2);
        Set(C.Height3, a.HeightLevel == 3); Set(C.UnderRockLayer, a.Y >= a.World.RockLayer);
        Set(C.Corruption, a.RolledBiomes.HasFlag(FishingBiomes1458.Corruption)); Set(C.Crimson, a.RolledBiomes.HasFlag(FishingBiomes1458.Crimson));
        Set(C.Jungle, a.RolledBiomes.HasFlag(FishingBiomes1458.Jungle)); Set(C.Snow, a.RolledBiomes.HasFlag(FishingBiomes1458.Snow));
        Set(C.Desert, a.RolledBiomes.HasFlag(FishingBiomes1458.Desert)); Set(C.RolledHallowDesert, a.InfectedDesert && a.TrueBiomes.HasFlag(FishingBiomes1458.Hallow));
        bool ocean = a.HeightLevel <= 1 && (a.X < 380 || a.X > a.World.Width - 380) && a.Pond.WaterTiles > 1000;
        Set(C.OriginalOcean, ocean); Set(C.RemixOcean, a.RemixOcean); Set(C.Ocean, ocean || a.RemixOcean);
        Set(C.Water1000, a.Pond.WaterTiles > 1000); Set(C.BloodMoon, a.World.BloodMoon); Set(C.DidNotUseCombatBook, !a.World.CombatBookUsed);
        return result;
    }

    private enum T { Any, Common, Uncommon, Rare, VeryRare, Legendary, UncommonOrCommon, BombRarityOfNotLegendaryAndNotVeryRareAndUncommon }
    private readonly record struct DropRow(T Tier, int Numerator, int Denominator, int[] Items, C Conditions, int QuestFish);
    [Flags]
    private enum C : ulong
    {
        HardMode = 1UL << 0, EarlyMode = 1UL << 1, InLava = 1UL << 2, InHoney = 1UL << 3,
        Junk = 1UL << 4, Crate = 1UL << 5, AnyEnemies = 1UL << 6, CanFishInLava = 1UL << 7,
        Dungeon = 1UL << 8, Beach = 1UL << 9, Hallow = 1UL << 10, GlowingMushrooms = 1UL << 11,
        TrueDesert = 1UL << 12, TrueSnow = 1UL << 13, Remix = 1UL << 14, Height1 = 1UL << 15,
        Height1And2 = 1UL << 16, HeightAbove1 = 1UL << 17, HeightAboveAnd1 = 1UL << 18,
        HeightUnder2 = 1UL << 19, HeightAbove2 = 1UL << 20, Height0 = 1UL << 21,
        Height2 = 1UL << 22, Height3 = 1UL << 23, UnderRockLayer = 1UL << 24,
        Corruption = 1UL << 25, Crimson = 1UL << 26, Jungle = 1UL << 27, Snow = 1UL << 28,
        Desert = 1UL << 29, RolledHallowDesert = 1UL << 30, OriginalOcean = 1UL << 31,
        RemixOcean = 1UL << 32, Ocean = 1UL << 33, Water1000 = 1UL << 34,
        BloodMoon = 1UL << 35, DidNotUseCombatBook = 1UL << 36
    }
}
