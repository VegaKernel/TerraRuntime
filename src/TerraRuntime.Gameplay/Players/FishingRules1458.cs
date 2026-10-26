namespace TerraRuntime.Gameplay.Players;

public readonly record struct FishingInventorySlot1458(int ItemType, int Stack);
public readonly record struct FishingPole1458(int ItemType, int Power, int ProjectileType, float ShootSpeed)
{
    public int UseTime => 8;
    public int UseAnimation => 8;
}

public readonly record struct FishingConditions1458(int PoleItemType, int PolePower,
    int BaitItemType, int BaitPower, int FinalFishingLevel, float LevelMultiplier);

public readonly record struct FishingPowerContext1458(bool DayTime, double Time, int MoonPhase,
    bool Raining, float CloudAlpha, bool BloodMoon, int FishingSkill,
    bool Tipsy = false, bool FloatingWhileWet = false, bool Sitting = false);

[Flags]
public enum FishingBiomes1458
{
    None = 0, Corruption = 1, Crimson = 2, Jungle = 4, Snow = 8, Dungeon = 16,
    Beach = 32, Hallow = 64, GlowingMushrooms = 128, Desert = 256
}

public readonly record struct FishingWorld1458(int Width, int Height, double Surface, double RockLayer,
    bool HardMode = false, bool Remix = false, bool NotTheBees = false,
    bool BloodMoon = false, bool DayTime = true, bool SkeletronDefeated = false,
    bool CombatBookUsed = false, bool RedSlimeUnlocked = false, bool DukeFishronActive = false);

public readonly record struct FishingPond1458(int WaterTiles, bool Lava, bool Honey, int ChumCount);
public readonly record struct FishingDropLevels1458(bool Common, bool Uncommon, bool Rare,
    bool VeryRare, bool Legendary, bool Crate);

/// <summary>A detached attempt prepared from authoritative world, inventory and player state.</summary>
public readonly record struct FishingAttempt1458(
    int X, int Y, int Level, int HeightLevel, int WaterNeeded, FishingPond1458 Pond,
    FishingConditions1458 Conditions, FishingWorld1458 World, FishingBiomes1458 TrueBiomes,
    FishingBiomes1458 RolledBiomes, bool InfectedDesert, bool RemixOcean, bool Junk,
    bool CanFishInLava, int QuestFish, FishingDropLevels1458 Levels)
{
    public bool LavaAccessory { get; init; }
}

public readonly record struct FishingResult1458(int ItemType, int NpcType, float BiteClock,
    int BaitItemType, int FishingLevel)
{
    public bool HasBite => ItemType != 0 || NpcType != 0;
    public int WaitBonus { get; init; }
}

/// <summary>Terraria 1.4.5.8 player fishing conditions, payment decisions and AI061 clocks.</summary>
public static partial class FishingRules1458
{
    public static bool TryGetPole(int itemType, out FishingPole1458 pole)
    {
        pole = itemType switch
        {
            2289 => new(2289, 5, 360, 9),
            2291 => new(2291, 15, 361, 11),
            2292 => new(2292, 30, 362, 14),
            2293 => new(2293, 20, 363, 13),
            2294 => new(2294, 50, 364, 17),
            2295 => new(2295, 35, 365, 15),
            2296 => new(2296, 40, 366, 16),
            2421 => new(2421, 22, 381, 13.5f),
            2422 => new(2422, 45, 382, 16.5f),
            4325 => new(4325, 25, 760, 15),
            4442 => new(4442, 30, 775, 15),
            _ => default
        };
        return pole.Power != 0;
    }

    public static bool IsBobber(int projectileType) => projectileType is >= 360 and <= 366 or
        381 or 382 or 760 or 775 or >= 986 and <= 993;

    public static int GetBaitPower(int itemType) => itemType switch
    {
        1992 or 1997 or >= 2436 and <= 2438 or >= 4334 and <= 4339 => 20,
        1994 => 5,
        1998 or 2006 or 2157 or 2740 or 5132 => 10,
        2000 or 2007 or 2156 or 2674 or 3192 or 4845 => 15,
        2001 or 2002 or 3193 or 4847 => 25,
        1996 or 2675 => 30,
        1995 or 2004 or 3191 or 4849 => 35,
        3194 => 40,
        1999 or 2676 or 2891 or 2893 or 2895 or 4340 or 4362 or 4419 => 50,
        4361 or 4418 => 17,
        4363 => 22,
        2673 => 666,
        _ => 0
    };

    public static bool IsLavaBait(int itemType) => itemType is 4845 or 4847 or 4849;

    // Player.Fishing_GetBait and ConsumeBait deliberately share this order, but payment
    // searches the type captured at the bite rather than whichever bait is now first.
    public static int SelectBait(ReadOnlySpan<FishingInventorySlot1458> inventory, int requiredType = 0)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            int first = pass == 0 ? 54 : 0;
            int end = Math.Min(inventory.Length, pass == 0 ? 58 : 50);
            for (int i = first; i < end; i++)
                if (inventory[i].Stack > 0 && (requiredType == 0
                    ? GetBaitPower(inventory[i].ItemType) > 0 : inventory[i].ItemType == requiredType))
                    return i;
        }
        return -1;
    }

    public static FishingConditions1458 GetConditions(ReadOnlySpan<FishingInventorySlot1458> inventory,
        int selectedSlot, in FishingPowerContext1458 context)
    {
        FishingPole1458 pole = default;
        if ((uint)selectedSlot < (uint)inventory.Length)
            TryGetPole(inventory[selectedSlot].ItemType, out pole);
        if (pole.Power == 0)
            for (int i = 0; i < Math.Min(58, inventory.Length); i++)
                if (TryGetPole(inventory[i].ItemType, out var other) && other.Power > pole.Power)
                    pole = other;
        int baitSlot = SelectBait(inventory);
        int baitType = baitSlot < 0 ? 0 : inventory[baitSlot].ItemType;
        int baitPower = GetBaitPower(baitType);
        if (baitType == 2673 || baitPower == 0 || pole.Power == 0)
            return new(pole.ItemType, pole.Power, baitType, baitPower, 0, 0);
        int bonus = (context.Tipsy ? 5 : 0) + (context.FloatingWhileWet ? 5 : 0) + (context.Sitting ? 5 : 0);
        float multiplier = GetPowerMultiplier(context);
        return new(pole.ItemType, pole.Power, baitType, baitPower,
            (int)((baitPower + pole.Power + context.FishingSkill + bonus) * multiplier), multiplier);
    }

    public static float GetPowerMultiplier(in FishingPowerContext1458 context)
    {
        float value = 1;
        if (context.Raining) value *= 1.2f;
        if (context.CloudAlpha > 0) value *= 1.1f;
        if (context.DayTime && (context.Time < 5400 || context.Time > 48600)) value *= 1.3f;
        if (context.DayTime && context.Time > 16200 && context.Time < 37800) value *= .8f;
        if (!context.DayTime && context.Time > 6480 && context.Time < 25920) value *= .8f;
        value *= context.MoonPhase switch { 0 => 1.1f, 1 or 7 => 1.05f, 3 or 5 => .95f, 4 => .9f, _ => 1 };
        if (context.BloodMoon) value *= 1.1f;
        return value;
    }

    public static bool ShouldConsumeBait(int baitType, int caughtItemType, int caughtNpcType,
        bool caughtItemIsJunk, bool tackleBox, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        float denominator = Math.Max(1, 1 + GetBaitPower(baitType) / 6f) + (tackleBox ? 1 : 0);
        bool consume = (float)random.NextDouble() * denominator < 1;
        if (caughtNpcType == 1) consume = true;
        if (caughtItemType > 0 && caughtItemIsJunk) consume = false;
        if (baitType == 2895) consume = random.Next(20) == 0;
        if (baitType == 2673) consume = true;
        return consume;
    }

    public static float AdvanceWait(float timer, int fishingPower, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (random.Next(300) < fishingPower) timer += random.Next(1, 3);
        timer += fishingPower / 30;
        timer += random.Next(1, 3);
        if (random.Next(60) == 0) timer += 60;
        return timer;
    }

    public static bool LineBreaks(bool fishingLine, Random random) => random.Next(7) == 0 && !fishingLine;

    public static int GetCatchStack(int itemType, int fishingPower, Random random)
    {
        if (itemType is not (3196 or 3197)) return 1;
        int min = itemType == 3196 ? (fishingPower / 20 + 3) / 2 : (fishingPower / 4 + 15) / 2;
        int max = itemType == 3196 ? (fishingPower / 10 + 6) / 2 : (fishingPower / 2 + 40) / 2;
        ReadOnlySpan<int> thresholds = [50, 100, 150, 200];
        foreach (int threshold in thresholds)
            if (random.Next(threshold) < fishingPower) max += itemType == 3196 ? 1 : 6;
        return random.Next(min, max + 1);
    }

    public static FishingDropLevels1458 RollDropLevels(int fishingPower, bool cratePotion, Random random)
    {
        if (fishingPower <= 0) throw new ArgumentOutOfRangeException(nameof(fishingPower));
        return new(random.Next(Math.Max(2, 150 / fishingPower)) == 0,
            random.Next(Math.Max(3, 300 / fishingPower)) == 0,
            random.Next(Math.Max(4, 1050 / fishingPower)) == 0,
            random.Next(Math.Max(5, 2250 / fishingPower)) == 0,
            random.Next(Math.Max(6, 4500 / fishingPower)) == 0,
            random.Next(100) < (cratePotion ? 25 : 10));
    }
}
