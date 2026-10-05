using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Represented server loot locale, independent from host OS culture and documentation language.</summary>
public enum VanillaTownNpcLootLanguage1458 : byte { English = 1 }

public readonly record struct VanillaTownNpcLootContext1458(bool? NamedEligible, bool? Hardmode);

public readonly record struct VanillaTownNpcLootRule1458(
    VanillaNpcLootRule Common, bool RequiresName = false, bool RequiresHardmode = false);

/// <summary>Official 1.4.5.8 RegisterTownNPCDrops, in per-NPC registration order.</summary>
public static class VanillaTownNpcLootRules1458
{
    private static VanillaTownNpcLootRule1458 Common(int item, int denominator = 1, short min = 1, short max = 1,
        bool named = false, bool hard = false) => new(new(VanillaNpcLootRuleKind.NormalVsExpertCommon,
            new(item), denominator, denominator, min, max, 1), named, hard);
    private static readonly VanillaTownNpcLootRule1458[] Guide = [Common(867, named: true)];
    private static readonly VanillaTownNpcLootRule1458[] Steampunker = [Common(4372, named: true)];
    private static readonly VanillaTownNpcLootRule1458[] Painter = [Common(5290, named: true), Common(3350, 8)];
    private static readonly VanillaTownNpcLootRule1458[] Stylist = [Common(3352, 8)];
    private static readonly VanillaTownNpcLootRule1458[] TaxCollector = [Common(3351, 8)];
    private static readonly VanillaTownNpcLootRule1458[] Tavernkeep = [Common(3821, 8)];
    private static readonly VanillaTownNpcLootRule1458[] PartyGirl = [Common(3548, 4, 30, 60)];
    private static readonly VanillaTownNpcLootRule1458[] DyeTrader = [Common(3349, 8)];
    private static readonly VanillaTownNpcLootRule1458[] Mechanic = [Common(4818, 8)];
    private static readonly VanillaTownNpcLootRule1458[] Princess = [Common(5065, 8, hard: true)];
    private static readonly VanillaTownNpcLootRule1458[] Clothier = [Common(260)];
    private static readonly VanillaTownNpcLootRule1458[] TravelingMerchant = [Common(2222)];

    public static bool RequiresName(NpcTypeId type) => type == VanillaNpcIds.Guide ||
        type == VanillaNpcIds.Steampunker || type == VanillaNpcIds.Painter;

    public static bool TryGet(NpcTypeId type, out ReadOnlySpan<VanillaTownNpcLootRule1458> rules)
    {
        rules = type.Value switch
        {
            22 => Guide, 178 => Steampunker, 227 => Painter, 353 => Stylist, 441 => TaxCollector,
            550 => Tavernkeep, 208 => PartyGirl, 207 => DyeTrader, 124 => Mechanic, 663 => Princess,
            54 => Clothier, 368 => TravelingMerchant, _ => default
        };
        return !rules.IsEmpty;
    }

    public static bool? CaptureNamedEligibility(NpcTypeId type, string? givenName,
        VanillaTownNpcLootLanguage1458? language)
    {
        if (!RequiresName(type)) return false;
        if (givenName is null) return null;
        // NamedNPC first checks HasGivenName; known empty bypasses localization entirely.
        if (givenName.Length == 0) return false;
        if (language != VanillaTownNpcLootLanguage1458.English) return null;
        string expected = type.Value switch { 22 => "Andrew", 178 => "Whitney", 227 => "Jim", _ => "" };
        return string.Equals(givenName, expected, StringComparison.Ordinal);
    }

    public static bool TryValidateContext(ReadOnlySpan<VanillaTownNpcLootRule1458> rules,
        in VanillaTownNpcLootContext1458 context)
    {
        if (rules.IsEmpty) return false;
        foreach (ref readonly var rule in rules)
            if (!rule.Common.IsValid || (rule.RequiresName && context.NamedEligible is null) ||
                (rule.RequiresHardmode && context.Hardmode is null)) return false;
        return true;
    }

    public static bool TryEvaluateRule(in VanillaTownNpcLootRule1458 rule,
        in VanillaTownNpcLootContext1458 context, INpcLootRollSource random,
        out bool dropped, out NpcLootDrop drop)
    {
        dropped = false; drop = default;
        if (!rule.Common.IsValid || (rule.RequiresName && context.NamedEligible is null) ||
            (rule.RequiresHardmode && context.Hardmode is null)) return false;
        if ((rule.RequiresName && context.NamedEligible == false) ||
            (rule.RequiresHardmode && context.Hardmode == false)) return true;
        // Reuse the admitted source CommonDrop evaluator; fixed stacks still consume Next(min,max+1).
        return VanillaNpcLootEvaluator.TryEvaluateRule(rule.Common, default, random, out dropped, out drop);
    }
}
