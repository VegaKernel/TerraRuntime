namespace TerraRuntime.Gameplay.Items;

/// <summary>Source 1.4.5.8 Player.OpenFishingCrate. Each grant runs before the next roll.</summary>
public static class VanillaFishingCrate1458
{
    public static bool IsCrate(int type) => type is 2334 or 2335 or 2336 or >= 3979 and <= 3987 or
        >= 3203 and <= 3208 or 4405 or 4406 or 4407 or 4408 or 4877 or 4878 or 5002 or 5003;
    public static bool IsHardmodeCrate(int type) => type is >= 3979 and <= 3987 or 4406 or 4408 or 4878 or 5003;

    public static bool TryOpen(int crate, bool remix, bool lowTiles, Func<int, int, int> next,
        Func<int, int, bool> quickSpawn)
    {
        if (!IsCrate(crate)) return false;
        bool hard = IsHardmodeCrate(crate);
        var roll = new Rolls(next, quickSpawn);
        if (crate is 2334 or 3979) return Wood(roll, hard);
        if (crate is 2335 or 3980) return Iron(roll, hard);
        if (crate is 2336 or 3981) return Gold(roll, hard);
        return Biome(roll, crate, hard, remix, lowTiles);
    }

    private sealed class Rolls(Func<int, int, int> next, Func<int, int, bool> grant)
    {
        internal bool Failed;
        internal int N(int max) => next(0, max);
        internal int N(int min, int max) => next(min, max);
        internal bool Chance(int denominator) => N(denominator) == 0;
        internal int Pick(params int[] items) => items[N(items.Length)];
        internal void Give(int type, int stack = 1) { if (!Failed && !grant(type, stack)) Failed = true; }
    }

    private static bool Wood(Rolls r, bool hard)
    {
        bool pending = true;
        // Source has no cap. A bounded refusal discards the detached transaction;
        // it never pays out a partial retry or commits a shortened random stream.
        for (int attempt = 0; pending; attempt++)
        {
            if (attempt == 256 || r.Failed) return false;
            if (hard && pending && r.Chance(200)) { r.Give(3064); pending = false; }
            if (pending && r.Chance(40)) { r.Give(3200); pending = false; }
            if (pending && r.Chance(40)) { r.Give(3201); pending = false; }
            if (hard && pending && r.Chance(25)) { r.Give(2424); pending = false; }
            if (r.Chance(20)) { r.Give(r.Pick(285, 953, 4341, 3068, 3084, 6165)); pending = false; }
            if (!hard && pending && r.Chance(50)) { r.Give(997); pending = false; }
            if (r.Chance(7))
            {
                int coin = r.Chance(3) ? 73 : 72;
                r.Give(coin, coin == 73 ? r.N(1, 6) : r.N(20, 91)); pending = false;
            }
            if (r.Chance(7))
            {
                int ore = r.Pick(12, 699, 11, 700);
                if (r.Chance(2) & hard) ore = r.Pick(364, 1104);
                r.Give(ore, r.N(4, 16)); pending = false;
            }
            else if (r.Chance(8))
            {
                int bar = r.Pick(20, 703, 22, 704), stack = r.N(2, 6);
                if (r.Chance(2) & hard) { bar = r.Pick(381, 1184); stack = r.N(2, 4); }
                r.Give(bar, stack); pending = false;
            }
            if (r.Chance(7)) { int potion = r.Pick(288, 290, 292, 299, 298, 304, 291, 2322, 2323, 2329); r.Give(potion, r.N(1, 4)); pending = false; }
        }
        if (r.Chance(3)) { int potion = r.Pick(28, 110); r.Give(potion, r.N(5, 16)); }
        if (r.Chance(3)) { int bait = r.Chance(3) ? 2675 : 2674; r.Give(bait, r.N(1, 5)); }
        return !r.Failed;
    }

    private static bool Iron(Rolls r, bool hard)
    {
        bool pending = true;
        for (int attempt = 0; pending; attempt++)
        {
            if (attempt == 256 || r.Failed) return false;
            if (hard && pending && r.Chance(60)) { r.Give(3064); pending = false; }
            if (pending && r.Chance(25)) { r.Give(2501); pending = false; }
            if (pending && r.Chance(20)) { r.Give(2587); pending = false; }
            if (pending && r.Chance(15)) { r.Give(2608); pending = false; }
            if (pending && r.Chance(20)) { r.Give(3200); pending = false; }
            if (pending && r.Chance(20)) { r.Give(3201); pending = false; }
            if (r.Chance(4)) { r.Give(73, r.N(5, 11)); pending = false; }
            if (r.Chance(6))
            {
                int ore = r.Pick(12, 699, 11, 700, 14, 701);
                if (r.Chance(2) & hard) ore = r.Pick(364, 1104, 365, 1105);
                r.Give(ore, r.N(12, 22)); pending = false;
            }
            else if (r.Chance(4))
            {
                int bar = r.Pick(20, 703, 22, 704, 21, 705), stack = r.N(4, 8);
                if ((r.N(3) != 0) & hard) { bar = r.Pick(381, 1184, 382, 1191); stack -= r.N(2); }
                r.Give(bar, stack); pending = false;
            }
            if (r.Chance(4)) { int potion = r.Pick(288, 296, 304, 305, 2322, 2323, 2324, 2327); r.Give(potion, r.N(2, 5)); pending = false; }
        }
        if (r.Chance(2)) { int potion = r.N(188, 190); r.Give(potion, r.N(5, 16)); }
        if (r.Chance(2)) { int bait = r.Chance(3) ? 2676 : 2675; r.Give(bait, r.N(2, 5)); }
        return !r.Failed;
    }

    private static bool Gold(Rolls r, bool hard)
    {
        bool pending = true;
        for (int attempt = 0; pending; attempt++)
        {
            if (attempt == 256 || r.Failed) return false;
            if (hard && pending && r.Chance(20)) { r.Give(3064); pending = false; }
            if (pending && r.Chance(8)) { r.Give(29); pending = false; }
            if (pending && r.Chance(10)) { r.Give(2491); pending = false; }
            if (r.Chance(3)) { r.Give(73, r.N(8, 21)); pending = false; }
            if (r.Chance(5))
            {
                int ore = r.Pick(14, 701, 13, 702);
                if (r.Chance(2) & hard) ore = r.Pick(365, 1105, 366, 1106);
                r.Give(ore, r.N(25, 35)); pending = false;
            }
            else
            {
                if (!r.Chance(3)) continue;
                int bar = r.Pick(21, 19, 705, 706);
                if ((r.N(3) != 0) & hard) bar = r.Pick(382, 391, 1191, 1198);
                r.Give(bar, r.N(8, 12)); pending = false;
            }
        }
        if (r.Chance(3)) { int potion = r.Pick(288, 296, 305, 2322, 2323); r.Give(potion, r.N(2, 6)); }
        if (r.Chance(2)) { int potion = r.N(188, 190); r.Give(potion, r.N(5, 21)); }
        if (r.N(3) != 0) r.Give(2676, r.N(3, 8));
        if (r.Chance(30) && !hard) r.Give(989);
        if (r.Chance(15) & hard) r.Give(989);
        return !r.Failed;
    }

    private static bool Biome(Rolls r, int crate, bool hard, bool remix, bool lowTiles)
    {
        bool ocean = crate is 5002 or 5003, corruption = crate is 3203 or 3982,
            crimson = crate is 3204 or 3983, dungeon = crate is 3205 or 3984,
            sky = crate is 3206 or 3985, jungle = crate is 3208 or 3987,
            ice = crate is 4405 or 4406, oasis = crate is 4407 or 4408, lava = crate is 4877 or 4878;
        bool pending = true;
        for (int attempt = 0; pending; attempt++)
        {
            if (attempt == 256 || r.Failed) return false;
            if (ocean && pending && r.Chance(1))
            {
                int item = r.Pick(186, 4404, 277, 187);
                if (r.Chance(10)) item = 863;
                r.Give(item); pending = false;
            }
            if (corruption && pending && r.Chance(1)) { r.Give(r.Pick(162, 111, 96, 115, 64)); pending = false; }
            if (crimson && pending && r.Chance(1)) { r.Give(r.Pick(800, 802, 1256, 1290, 3062)); pending = false; }
            if (dungeon && pending && r.Chance(1))
            {
                r.Give(3085); pending = false;
                if (r.Chance(2)) r.Give(149, r.N(5, 16));
                if (r.Chance(2)) { int item = r.Pick(134, 137, 139); r.Give(item, r.N(25, 51)); }
            }
            if (sky && pending && r.Chance(1)) { r.Give(r.Pick(158, 65, 159, 2219)); pending = false; }
            if (jungle && pending && r.Chance(1))
            {
                if (r.Chance(20)) { _ = r.N(5); r.Give(3017); }
                else r.Give(r.Pick(212, 964, 211, 213, 2292));
                pending = false;
            }
            if (ice && pending && r.Chance(1))
            {
                int item = r.Pick(670, 724, 950, 1319, 987, 1579, 6153);
                r.Give(remix && item == 1319 ? 725 : item); pending = false;
            }
            if (oasis)
            {
                if (pending && r.Chance(1)) { r.Give(r.Pick(4056, 4442, 4055, 4061, 4062, 4276, 4262, 4263)); pending = false; }
                if (r.Chance(4)) { r.Give(4423, r.N(4, 7)); pending = false; }
                if (r.Chance(2)) { r.Give(3380, r.N(10, 17)); pending = false; }
                if (r.Chance(35)) { r.Give(857); pending = false; }
            }
            if (lava)
            {
                if (pending && r.Chance(1))
                {
                    if (r.Chance(20)) { _ = r.N(5); r.Give(906); }
                    else r.Give(r.Pick(4822, 4828, 4880, 4881, 4868));
                    pending = false;
                    if (r.Chance(4)) r.Give(4858, 2);
                }
                r.Give(4879); pending = false;
                if (r.Chance(3)) r.Give(4824, r.N(7, 11));
                if (r.Chance(2)) r.Give(r.Pick(4902, 4903, 4904, 4905, 4906));
            }
            if (r.Chance(4)) { r.Give(73, r.N(5, 13)); pending = false; }
            if (r.Chance(7))
            {
                int ore = r.Pick(12, 699, 11, 700, 14, 701, 13, 702);
                if (lava) ore = 174;
                if (r.Chance(2) & hard) ore = r.Pick(364, 1104, 365, 1105, 366, 1106);
                r.Give(ore, r.N(20, 36)); pending = false;
            }
            if (!r.Chance(4)) continue;
            int bar = r.Pick(22, 21, 19, 704, 705, 706), stack = r.N(6, 17);
            if (lava) bar = 175;
            if ((r.N(3) != 0) & hard) { bar = r.Pick(381, 382, 391, 1184, 1191, 1198); stack -= r.N(2); }
            r.Give(bar, stack); pending = false;
        }
        if (r.Chance(4)) { int potion = r.Pick(288, 296, 304, 305, 2322, 2323); r.Give(potion, r.N(2, 5)); }
        if (r.Chance(2)) { int potion = r.N(188, 190); r.Give(potion, r.N(5, 18)); }
        if (r.Chance(2)) { int bait = r.Chance(2) ? 2676 : 2675; r.Give(bait, r.N(2, 7)); }
        if (ocean)
        {
            if (r.Chance(5)) r.Give(5527);
            if (r.Chance(3)) r.Give(4090, r.N(20, 51));
            if (r.Chance(10)) r.Give(4460);
            if (r.Chance(10)) r.Give(4425);
        }
        if (dungeon && r.Chance(2)) r.Give(r.Pick(1438, 1375, 1573, 1420, 1435, 1434, 1421, 5234, 1374, 1425, 1372, 1441, 1373, 1433, 1436, 1426, 1424, 1419, 2995, 1422, 1439, 1502, 1423, 1437, 1500));
        if (jungle)
        {
            if (r.Chance(3)) r.Give(4564, r.N(20, 51));
            if (r.Chance(20)) r.Give(753);
        }
        if (ice && r.Chance(20)) r.Give(669);
        if (oasis && r.Chance(2)) r.Give(r.Pick(4639, 4627, 4628, 4632, 4630, 4638, 4629, 4633, 4634, 4635, 4636, 4637, 4631, 4626));
        if (sky)
        {
            if (r.Chance(2)) r.Give(r.Pick(5226, 5254, 5238, 5258, 5255, 5388));
            if (r.Chance(2)) r.Give(751, r.N(50, 101));
            if (r.Chance(40)) r.Give(4978);
            if (lowTiles && r.Chance(3)) r.Give(2197);
        }
        if (lava)
        {
            if (r.Chance(2)) r.Give(r.Pick(1497, 1475, 1479, 1542, 1476, 1538, 1501, 1478, 1539, 1540, 1499, 1541));
            if (r.Chance(20)) r.Give(221);
            if (r.Chance(20)) r.Give(4443);
            if (r.Chance(20)) r.Give(4737);
            if (r.Chance(20)) r.Give(4551);
        }
        if (hard && crate is 3982 or 3986 or 3983)
        {
            if (r.Chance(2)) r.Give(crate == 3986 ? 520 : 521, r.N(2, 6));
            if (r.Chance(2))
            {
                int item = crate == 3983 ? 1332 : crate == 3986 ? 502 : 522, stack = r.N(2, 6);
                if (crate == 3986) stack = r.N(4, 11);
                r.Give(item, stack);
            }
        }
        return !r.Failed;
    }
}
