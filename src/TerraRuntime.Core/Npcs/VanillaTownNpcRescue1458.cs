using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum VanillaTownNpcRescueTrigger1458 : byte
{
    Talk = 0,
    PurificationPowder = 1
}

public enum VanillaTownNpcRescueFact1458 : byte
{
    Goblin = 0,
    Wizard = 1,
    Mechanic = 2,
    Stylist = 3,
    Angler = 4,
    Bartender = 5,
    Golfer = 6,
    TaxCollector = 7
}

public readonly record struct VanillaTownNpcRescueRule1458(
    NpcTypeId BoundType,
    NpcTypeId ResidentType,
    VanillaTownNpcRescueTrigger1458 Trigger,
    VanillaTownNpcRescueFact1458 Fact,
    int BoundWidth,
    int BoundHeight,
    int BoundLifeMax)
{
    public bool IsValid =>
        BoundType.IsAssigned && ResidentType.IsAssigned && BoundWidth > 0 && BoundHeight > 0 && BoundLifeMax > 0;
}

/// <summary>
/// TerrariaServer 1.4.5.8 bound-town rescue/transform facts. Talk rules come directly from NPC.AI style 0 and
/// AI_000_TransformBoundNPC. Demon Tax Collector is catalogued separately because vanilla transforms it only when
/// Purification Powder projectile 10 intersects NPC 534.
/// </summary>
public static class VanillaTownNpcRescue1458
{
    private static readonly VanillaTownNpcRescueRule1458[] Rules =
    [
        new(VanillaNpcIds.BoundGoblin, VanillaNpcIds.GoblinTinkerer, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Goblin, 18, 34, 250),
        new(VanillaNpcIds.BoundWizard, VanillaNpcIds.Wizard, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Wizard, 18, 40, 250),
        new(VanillaNpcIds.BoundMechanic, VanillaNpcIds.Mechanic, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Mechanic, 16, 30, 250),
        new(VanillaNpcIds.WebbedStylist, VanillaNpcIds.Stylist, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Stylist, 16, 30, 250),
        new(VanillaNpcIds.SleepingAngler, VanillaNpcIds.Angler, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Angler, 30, 7, 250),
        new(VanillaNpcIds.BartenderUnconscious, VanillaNpcIds.Tavernkeep, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Bartender, 34, 8, 250),
        new(VanillaNpcIds.GolferRescue, VanillaNpcIds.Golfer, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Golfer, 18, 34, 250),
        new(VanillaNpcIds.DemonTaxCollector, VanillaNpcIds.TaxCollector, VanillaTownNpcRescueTrigger1458.PurificationPowder, VanillaTownNpcRescueFact1458.TaxCollector, 18, 40, 400)
    ];

    public static ReadOnlySpan<VanillaTownNpcRescueRule1458> All => Rules;

    public static bool TryGet(NpcTypeId boundType, out VanillaTownNpcRescueRule1458 rule)
    {
        foreach (VanillaTownNpcRescueRule1458 candidate in Rules)
        {
            if (candidate.BoundType == boundType)
            {
                rule = candidate;
                return true;
            }
        }
        rule = default;
        return false;
    }

    public static bool TryGetTalkRule(NpcTypeId boundType, out VanillaTownNpcRescueRule1458 rule) =>
        TryGet(boundType, out rule) && rule.Trigger == VanillaTownNpcRescueTrigger1458.Talk;
}
