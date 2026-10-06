using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Official 1.4.5.8 invasion reward tables; table support grants no actor behavior.</summary>
public static class VanillaInvasionNpcLootCatalog1458
{
    public const int PirateMaximumDropCount = 34;

    private static VanillaNpcLootRule Common(int item, int chance, short min = 1, short max = 1) =>
        new(VanillaNpcLootRuleKind.NormalVsExpertCommon, new(item), chance, chance, min, max, 1);

    private static readonly VanillaNpcLootRule[] GoblinRules =
    [
        new(VanillaNpcLootRuleKind.FailedRollChain, new(160), 1, 1, 1, 1, 1,
            new([Common(160, 200), Common(161, 2, 1, 5)]))
    ];

    private static readonly VanillaNpcLootRule[] PirateRules =
    [
        Common(905, 4000, 1, 1),
        Common(855, 2000, 1, 1),
        Common(854, 1000, 1, 1),
        Common(2584, 1000, 1, 1),
        Common(3033, 500, 1, 1),
        Common(672, 200, 1, 1),
        Common(5460, 200, 1, 1),
        Common(1277, 500, 1, 1),
        Common(1278, 500, 1, 1),
        Common(1279, 500, 1, 1),
        Common(1280, 500, 1, 1),
        Common(1704, 300, 1, 1),
        Common(1705, 300, 1, 1),
        Common(1710, 300, 1, 1),
        Common(1716, 300, 1, 1),
        Common(1720, 300, 1, 1),
        Common(2379, 300, 1, 1),
        Common(2389, 300, 1, 1),
        Common(2405, 300, 1, 1),
        Common(2843, 300, 1, 1),
        Common(3885, 300, 1, 1),
        Common(2663, 300, 1, 1),
        Common(3904, 150, 80, 130),
        Common(3910, 300, 1, 1),
        Common(2238, 300, 1, 1),
        Common(2133, 300, 1, 1),
        Common(2137, 300, 1, 1),
        Common(2143, 300, 1, 1),
        Common(2147, 300, 1, 1),
        Common(2151, 300, 1, 1),
        Common(2155, 300, 1, 1),
        Common(3263, 500, 1, 1),
        Common(3264, 500, 1, 1),
        Common(3265, 500, 1, 1),
    ];

    private static readonly VanillaNpcLootRule[] MartianRules =
    [
        Common(2860, 8, 8, 20),
        Common(2798, 800, 1, 1),
        Common(2800, 800, 1, 1),
        Common(2882, 800, 1, 1),
        Common(2803, 200, 1, 1),
        Common(2804, 200, 1, 1),
        Common(2805, 200, 1, 1),
    ];

    private static readonly VanillaNpcLootRule[] PirateCaptainRules =
    [
        Common(905, 1000),
        Common(855, 500),
        Common(854, 250),
        Common(2584, 250),
        Common(3033, 125),
        Common(672, 50),
        Common(5460, 50)
    ];

    public static bool TryGet(NpcTypeId type, out VanillaNpcLootTable table)
    {
        VanillaNpcLootRule[]? rules = type.Value switch
        {
            26 or 27 or 28 or 29 or 111 => GoblinRules,
            212 or 213 or 214 or 215 => PirateRules,
            216 => PirateCaptainRules,
            // These populated source tables are empty; globals and death events remain separate.
            252 or 662 => [],
            381 => MartianRules,
            _ => null
        };
        table = rules is null ? default : new(type, rules);
        return rules is not null;
    }
}
