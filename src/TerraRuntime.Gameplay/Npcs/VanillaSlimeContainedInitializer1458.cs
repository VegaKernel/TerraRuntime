using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaSlimeContainedFacts1458(
    double WorldSurfaceTiles, double RockLayerTiles, bool GoodWorld, bool RemixWorld,
    bool NoTrapsWorld, bool VampireSeed, bool GenuineParty, bool NotTheBeesWorld,
    bool LowTiles, bool SlimeRain, bool HardMode, bool NoHellstone, bool DownedSkeletron,
    bool NoLifeCrystals, bool HasHeartSlime, int MoonPhase);

public readonly record struct VanillaSlimeContainedInput1458(
    int Type, short NetId, float PositionY, int Height, float MoneyValue, int Item, bool Ballooned);

public readonly record struct VanillaSlimeContainedSelection1458(int Item, bool Initialized, bool Admitted);

/// <summary>Source 1.4.5.8 AI001 generic Blue/Lava contained-item selection; runtime owns acceptance and effects.</summary>
public static class VanillaSlimeContainedInitializer1458
{
    public static bool TrySelect(in VanillaSlimeContainedInput1458 input,
        in VanillaSlimeContainedFacts1458 facts, IVanillaNpcRandom random,
        out VanillaSlimeContainedSelection1458 selection)
    {
        selection = default;
        if (input.Type != VanillaNpcIds.BlueSlime.Value && input.Type != VanillaNpcIds.LavaSlime.Value ||
            !float.IsFinite(input.PositionY) || !float.IsFinite(input.MoneyValue) || input.MoneyValue < 0f ||
            input.Height <= 0 || !double.IsFinite(facts.WorldSurfaceTiles) ||
            !double.IsFinite(facts.RockLayerTiles) || facts.MoonPhase is < 0 or > 7)
            return false;
        bool good = facts.GoodWorld, remix = facts.RemixWorld, noTraps = facts.NoTrapsWorld,
            vampire = facts.VampireSeed, party = facts.GenuineParty, bees = facts.NotTheBeesWorld,
            low = facts.LowTiles, rain = facts.SlimeRain, hard = facts.HardMode;
        var retainedFacts = facts;
        bool InRockLayer(float y)
        {
            int tile = (int)(y / 16f);
            return remix ? tile > retainedFacts.WorldSurfaceTiles && tile <= retainedFacts.RockLayerTiles : tile > retainedFacts.RockLayerTiles;
        }
        int item = input.Item;
        if (input.Item == 0 && input.MoneyValue > 0)
        {
            item = -1;
            int attempts = 1 + (low ? (input.NetId == -6 ? 9 : 4) : (input.NetId == -6 ? 4 : 0)) + (rain ? 2 : 0);

            for (int attempt = 0; attempt < attempts && item == -1; attempt++)
            {
                if (input.Type == VanillaNpcIds.LavaSlime.Value)
                {
                    if (remix && random.NextInt32(0, low ? 15 : 20) == 0)
                        item = GenerateServerItem(random, input.Ballooned, low, facts.MoonPhase, InRockLayer(input.PositionY + input.Height * .5f), hard);
                    else if (facts.NoHellstone && facts.DownedSkeletron && random.NextInt32(0, 15) == 0)
                        item = 174;
                    continue;
                }
                if (input.NetId is -5 or -4)
                    continue;
                int trap = noTraps ? 20 : good ? 100 : input.PositionY + input.Height * .5f < facts.WorldSurfaceTiles * 16d ? -1 : 500;
                if (InRockLayer(input.PositionY) && (facts.NoLifeCrystals || low) && !facts.HasHeartSlime && random.NextInt32(0, 200) == 0)
                    item = 29;
                else if (low && input.PositionY / 16f > facts.WorldSurfaceTiles && random.NextInt32(0, 1000) == 0)
                    item = new[] { 5499, 5500, 5501, 5502, 5503, 5504, 5505, 5506, 5507, 5508, 5509, 5484, 5485, 5534 }[random.NextInt32(0, 14)];
                else if (party && input.PositionY + input.Height * .5f < facts.WorldSurfaceTiles * 16d)
                    item = random.NextInt32(0, 2) == 0 ? random.NextInt32(3736, 3739) : 1345;
                else if (input.NetId == -10 && random.NextInt32(0, 20) == 0)
                    item = new[] { 1124, 1125, 314, 5395 }[random.NextInt32(0, bees ? 4 : 3)];
                else if (random.NextInt32(0, low ? 15 : 20) == 0)
                    item = GenerateServerItem(random, input.Ballooned, low, facts.MoonPhase, InRockLayer(input.PositionY + input.Height * .5f), hard);
                else if (random.NextInt32(0, low ? 20 : 40) == 0)
                {
                    if (input.PositionY / 16f <= facts.WorldSurfaceTiles)
                    {
                        if (low && (facts.MoonPhase == (int)VanillaMoonPhase.Full || random.NextInt32(0, 2) == 0))
                            item = random.NextInt32(0, 2) == 0 ? (random.NextInt32(0, 50) == 0 ? 194 : random.NextInt32(0, 10) == 0 ? 195 : 62) : 27;
                        else
                            item = 751;
                    }
                    else if (!InRockLayer(input.PositionY))
                        item = new[] { 2, 3, 9 }[random.NextInt32(0, 3)];
                    else if (random.NextInt32(0, 10) == 0)
                        item = 3609;
                    else if (low && hard && random.NextInt32(0, 2) == 0)
                        item = new[] { 364, 1104, 365, 1105, 366, 1106 }[random.NextInt32(0, 6)];
                    else
                        item = new[] { 3, 150, 3086, 3081 }[random.NextInt32(0, 4)];
                }
                else if (trap > 0 && random.NextInt32(0, trap) == 0)
                    item = 539;
                else if (good && input.PositionY / 16f > facts.WorldSurfaceTiles && random.NextInt32(0, trap) == 0)
                    item = 147;
                else if (attempt == 0 && remix && !input.Ballooned && random.NextInt32(0, 3) == 0)
                    item = 75;
                else if (vampire && !remix && random.NextInt32(0, 13) == 0 && input.PositionY / 16f > facts.WorldSurfaceTiles)
                    item = 9;
            }
        }

        // These source-selected contents call AddBuff or mutate tiles. Reject before live ownership/RNG adoption.
        bool admitted = !(item == 8 && good) && item is not (314 or 150);
        selection = new(item, input.Item == 0 && input.MoneyValue > 0f, admitted);
        return true;
    }

    public static void ObserveBeforeSelection(int type, int retainedItem, IVanillaNpcRandom random)
    {
        if (type == VanillaNpcIds.BlueSlime.Value && retainedItem == 75 && random.NextInt32(0, 12) == 0)
        {
            random.NextDouble(); // circular-edge angle
            random.NextDouble(); // edge radius multiplier
            random.NextDouble(); // rising speed
        }
    }

    public static (bool Trap, int HiveType) ObserveContents(int item, bool good, bool noTraps,
        IVanillaNpcRandom random)
    {
        if (item == 539)
            return (random.NextInt32(0, 300 - (noTraps ? 120 : 0) - (good ? 120 : 0)) == 0, 0);
        if (item == 1124)
            return (false, random.NextInt32(0, 60) == 7 ? 1 : 0); // Type draw follows actual LOS, in the runtime plan.
        if (item == 5395 && random.NextInt32(0, 30) == 0)
        {
            random.NextDouble();
            random.NextDouble();
        }
        else if (item == 1345)
        {
            if (random.NextInt32(0, 30) == 0)
            {
                random.NextInt32(139, 143);
                random.NextInt32(-30, 31);
                random.NextInt32(-50, 51);
            }
            if (random.NextInt32(0, 60) == 0)
            {
                random.NextInt32(276, 283);
                random.NextInt32(-20, 21);
                random.NextInt32(-50, 51);
            }
        }
        else if (item is 1103 or 593)
        {
            if (random.NextInt32(0, 3) == 0)
                random.NextInt32(0, 4);
        }
        else if (item == 174)
        {
            random.NextInt32(0, 5);
            random.NextInt32(0, 5);
        }
        return default;
    }

    public static int GenerateServerItem(IVanillaNpcRandom random, bool balloon, bool low, int moon, bool rock, bool hard)
    {
        int family = random.NextInt32(0, 4);
        if (low)
        {
            if (random.NextInt32(0, 3) != 0)
                family = random.NextInt32(1, 3);
            if (random.NextInt32(0, 3) != 0)
                balloon = false;
        }
        if (balloon)
            return new[] { 4367, 4368, 4369, 4370, 4371, 4612, 4674, 4343, 4343, 4343, 4344, 4344, 4344 }[random.NextInt32(0, 13)];
        if (family == 0)
        {
            int potion = random.NextInt32(0, 7);
            if (potion < 4)
                return new[] { 290, 292, 296, 2322 }[potion];
            return random.NextInt32(0, 2) == 0 ? 2997 : 2350;
        }
        if (family == 1)
        {
            int utility = random.NextInt32(0, 4);
            if (low)
            {
                if (moon == (int)VanillaMoonPhase.Full)
                    utility = random.NextInt32(0, 2);
                if (utility == 2)
                    utility = random.NextInt32(0, 4);
            }
            return new[] { 8, 965, 166, 58 }[utility];
        }
        if (family == 2)
        {
            if (rock && low && hard && random.NextInt32(0, 2) == 0)
                return new[] { 364, 1104, 365, 1105, 366, 1106 }[random.NextInt32(0, 6)];
            return random.NextInt32(0, 2) == 0 ? random.NextInt32(11, 15) : random.NextInt32(699, 703);
        }
        int coin = random.NextInt32(0, 3);
        if (low && random.NextInt32(0, 5) != 0)
            coin = 0;
        return new[] { 71, 72, 73 }[coin];
    }

}
