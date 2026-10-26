using static TerraRuntime.Gameplay.Items.FishingCrateGrammar1458;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Source 1.4.5.8 Player.OpenFishingCrate; generation and possible outcomes share one grammar.</summary>
public static class VanillaFishingCrate1458
{
    public const int MaximumGrants = 16;
    public const int MaximumValidationSteps = 65_536;
    private const int LowTilesContext = 1;
    private const int RemixContext = 2;
    private static readonly int[] crates = [2334, 2335, 2336, 3979, 3980, 3981, 3203, 3982, 3204, 3983,
        3205, 3984, 3206, 3985, 3207, 3986, 3208, 3987, 4405, 4406, 4407, 4408, 4877, 4878, 5002, 5003];
    private static readonly Node[] programs = BuildPrograms();

    public static bool IsCrate(int type) => type is 2334 or 2335 or 2336 or >= 3979 and <= 3987 or
        >= 3203 and <= 3208 or 4405 or 4406 or 4407 or 4408 or 4877 or 4878 or 5002 or 5003;
    public static bool IsHardmodeCrate(int type) => type is >= 3979 and <= 3987 or 4406 or 4408 or 4878 or 5003;

    public static bool TryOpen(int crate, bool remix, bool lowTiles, Func<int, int, int> next, Func<int, int, bool> quickSpawn)
    {
        var program = Program(crate, remix, lowTiles);
        bool pending = true;
        return program is not null && program.Generate(new(next, quickSpawn), ref pending);
    }

    /// <summary>Validates complete unordered raw grants and returns one legal source QuickSpawn order.</summary>
    public static bool TryValidateOutcome(int crate, bool remix, bool lowTiles,
        IReadOnlyList<FishingReward1458> rewards, out FishingReward1458[] orderedWitness) =>
        Validate(crate, remix, lowTiles, rewards, false, out orderedWitness, out _);

    public static bool TryValidateOutcome(int crate, bool remix, bool lowTiles,
        IReadOnlyList<FishingReward1458> rewards, out FishingReward1458[] orderedWitness, out int steps) =>
        Validate(crate, remix, lowTiles, rewards, false, out orderedWitness, out steps);

    public static bool IsPossibleOrderedOutcome(int crate, bool remix, bool lowTiles,
        IReadOnlyList<FishingReward1458> rewards) => Validate(crate, remix, lowTiles, rewards, true, out _, out _);

    private static bool Validate(int crate, bool remix, bool lowTiles, IReadOnlyList<FishingReward1458> rewards,
        bool ordered, out FishingReward1458[] witness, out int steps)
    {
        witness = []; steps = 0;
        var program = Program(crate, remix, lowTiles);
        if (program is null || rewards is null) return false;
        int count = rewards.Count;
        if (count is < 1 or > MaximumGrants) return false;
        var input = new FishingReward1458[count];
        var seen = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            var reward = rewards[i];
            if (reward.Type <= 0 || reward.Stack <= 0 || reward.Stack > 9999 || !seen.Add(reward.Type)) return false;
            input[i] = reward;
        }
        var match = new Match(input, ordered);
        var states = program.Accept(match, new(0, true, null));
        steps = match.Steps;
        if (match.Exhausted) return false;
        uint complete = (1u << count) - 1;
        foreach (var state in states)
        {
            if (state.Used != complete) continue;
            witness = new FishingReward1458[count];
            var trace = state.Trace;
            for (int i = witness.Length - 1; i >= 0; i--)
            {
                if (trace is null) { witness = []; return false; }
                witness[i] = input[trace.Index]; trace = trace.Previous;
            }
            return true;
        }
        return false;
    }

    private static Node? Program(int crate, bool remix, bool lowTiles)
    {
        int index = Array.IndexOf(crates, crate);
        return index < 0 ? null : programs[index * 4 + (remix ? 2 : 0) + (lowTiles ? 1 : 0)];
    }
    private static Node[] BuildPrograms()
    {
        var result = new Node[crates.Length * 4];
        for (int i = 0; i < crates.Length; i++)
        for (int flags = 0; flags < 4; flags++)
        {
            int crate = crates[i]; bool hard = IsHardmodeCrate(crate);
            result[i * 4 + flags] = crate is 2334 or 3979 ? Wood(hard) :
                crate is 2335 or 3980 ? Iron(hard) : crate is 2336 or 3981 ? Gold(hard) :
                Biome(crate, hard, (flags & RemixContext) != 0, (flags & LowTilesContext) != 0);
        }
        return result;
    }

    private static Node Pool(int[] types, int min, int max) => P(types, type => R(type, min, max));
    private static Node Replace(bool hard, int denominator, bool nonzero, Node replacement, Node original) =>
        !hard ? D(0, denominator, original) : nonzero ? C(denominator, original, replacement) : C(denominator, replacement, original);
    private static Node Ore(bool hard, int[] basic, int[] advanced, int min, int max, int lavaType = 0) =>
        P(basic, type => Replace(hard, 2, false, Pool(advanced, min, max), R(lavaType == 0 ? type : lavaType, min, max)));

    private static Node Wood(bool hard) => S(A(S(
        hard ? Q(C(200, E(3064))) : None,
        Q(C(40, E(3200))), Q(C(40, E(3201))), hard ? Q(C(25, E(2424))) : None,
        C(20, P(285, 953, 4341, 3068, 3084, 6165)), !hard ? Q(C(50, E(997))) : None,
        C(7, C(3, R(73, 1, 6), R(72, 20, 91))),
        C(7, Ore(hard, [12, 699, 11, 700], [364, 1104], 4, 16),
            C(8, P([20, 703, 22, 704], type => T(2, 6, count =>
                Replace(hard, 2, false, Pool([381, 1184], 2, 4), E(type, count)))))),
        C(7, Pool([288, 290, 292, 299, 298, 304, 291, 2322, 2323, 2329], 1, 4)))),
        C(3, Pool([28, 110], 5, 16)), C(3, C(3, R(2675, 1, 5), R(2674, 1, 5))));

    private static Node Iron(bool hard) => S(A(S(
        hard ? Q(C(60, E(3064))) : None,
        Q(C(25, E(2501))), Q(C(20, E(2587))), Q(C(15, E(2608))), Q(C(20, E(3200))), Q(C(20, E(3201))),
        C(4, R(73, 5, 11)),
        C(6, Ore(hard, [12, 699, 11, 700, 14, 701], [364, 1104, 365, 1105], 12, 22),
            C(4, P([20, 703, 22, 704, 21, 705], type => T(4, 8, count =>
                Replace(hard, 3, true, P([381, 1184, 382, 1191], item => T(0, 2, delta => E(item, count - delta))), E(type, count)))))),
        C(4, Pool([288, 296, 304, 305, 2322, 2323, 2324, 2327], 2, 5)))),
        C(2, T(188, 190, type => R(type, 5, 16))), C(2, C(3, R(2676, 2, 5), R(2675, 2, 5))));

    private static Node Gold(bool hard) => S(A(S(
        hard ? Q(C(20, E(3064))) : None, Q(C(8, E(29))), Q(C(10, E(2491))), C(3, R(73, 8, 21)),
        C(5, Ore(hard, [14, 701, 13, 702], [365, 1105, 366, 1106], 25, 35),
            C(3, P([21, 19, 705, 706], type => Replace(hard, 3, true,
                Pool([382, 391, 1191, 1198], 8, 12), R(type, 8, 12))))))),
        C(3, Pool([288, 296, 305, 2322, 2323], 2, 6)), C(2, T(188, 190, type => R(type, 5, 21))),
        C(3, None, R(2676, 3, 8)),
        hard ? D(0, 30, C(15, E(989))) : S(C(30, E(989)), D(0, 15, None)));

    private static Node Biome(int crate, bool hard, bool remix, bool lowTiles)
    {
        bool ocean = crate is 5002 or 5003, corruption = crate is 3203 or 3982,
            crimson = crate is 3204 or 3983, dungeon = crate is 3205 or 3984,
            sky = crate is 3206 or 3985, jungle = crate is 3208 or 3987,
            ice = crate is 4405 or 4406, oasis = crate is 4407 or 4408, lava = crate is 4877 or 4878;
        var body = new List<Node>(); var tail = new List<Node>();
        if (ocean) body.Add(Q(C(1, P([186, 4404, 277, 187], type => C(10, E(863), E(type))))));
        if (corruption) body.Add(Q(C(1, P(162, 111, 96, 115, 64))));
        if (crimson) body.Add(Q(C(1, P(800, 802, 1256, 1290, 3062))));
        if (dungeon) body.Add(Q(C(1, S(E(3085), C(2, R(149, 5, 16)), C(2, Pool([134, 137, 139], 25, 51))))));
        if (sky) body.Add(Q(C(1, P(158, 65, 159, 2219))));
        if (jungle) body.Add(Q(C(1, C(20, D(0, 5, E(3017)), P(212, 964, 211, 213, 2292)))));
        if (ice) body.Add(Q(C(1, P([670, 724, 950, 1319, 987, 1579, 6153], type => E(remix && type == 1319 ? 725 : type)))));
        if (oasis) body.Add(S(Q(C(1, P(4056, 4442, 4055, 4061, 4062, 4276, 4262, 4263))),
            C(4, R(4423, 4, 7)), C(2, R(3380, 10, 17)), C(35, E(857))));
        if (lava) body.Add(S(Q(C(1, S(C(20, D(0, 5, E(906)), P(4822, 4828, 4880, 4881, 4868)), C(4, E(4858, 2))))),
            E(4879), C(3, R(4824, 7, 11)), C(2, P(4902, 4903, 4904, 4905, 4906))));
        body.Add(C(4, R(73, 5, 13)));
        body.Add(C(7, Ore(hard, [12, 699, 11, 700, 14, 701, 13, 702], [364, 1104, 365, 1105, 366, 1106], 20, 36, lava ? 174 : 0)));
        body.Add(C(4, P([22, 21, 19, 704, 705, 706], type => T(6, 17, count => Replace(hard, 3, true,
            P([381, 382, 391, 1184, 1191, 1198], item => T(0, 2, delta => E(item, count - delta))), E(lava ? 175 : type, count))))));
        tail.Add(C(4, Pool([288, 296, 304, 305, 2322, 2323], 2, 5)));
        tail.Add(C(2, T(188, 190, type => R(type, 5, 18))));
        tail.Add(C(2, C(2, R(2676, 2, 7), R(2675, 2, 7))));
        if (ocean) tail.Add(S(C(5, E(5527)), C(3, R(4090, 20, 51)), C(10, E(4460)), C(10, E(4425))));
        if (dungeon) tail.Add(C(2, P(1438, 1375, 1573, 1420, 1435, 1434, 1421, 5234, 1374, 1425, 1372, 1441, 1373, 1433, 1436, 1426, 1424, 1419, 2995, 1422, 1439, 1502, 1423, 1437, 1500)));
        if (jungle) tail.Add(S(C(3, R(4564, 20, 51)), C(20, E(753))));
        if (ice) tail.Add(C(20, E(669)));
        if (oasis) tail.Add(C(2, P(4639, 4627, 4628, 4632, 4630, 4638, 4629, 4633, 4634, 4635, 4636, 4637, 4631, 4626)));
        if (sky) tail.Add(S(C(2, P(5226, 5254, 5238, 5258, 5255, 5388)), C(2, R(751, 50, 101)), C(40, E(4978)), lowTiles ? C(3, E(2197)) : None));
        if (lava) tail.Add(S(C(2, P(1497, 1475, 1479, 1542, 1476, 1538, 1501, 1478, 1539, 1540, 1499, 1541)),
            C(20, E(221)), C(20, E(4443)), C(20, E(4737)), C(20, E(4551))));
        if (hard && crate is 3982 or 3986 or 3983) tail.Add(S(C(2, R(crate == 3986 ? 520 : 521, 2, 6)),
            C(2, crate == 3986 ? T(2, 6, _ => R(502, 4, 11)) : R(crate == 3983 ? 1332 : 522, 2, 6))));
        return S(A(S(body.ToArray())), S(tail.ToArray()));
    }
}
