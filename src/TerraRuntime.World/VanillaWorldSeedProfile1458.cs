using System.Globalization;
using System.Text;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

[Flags]
public enum VanillaSpecialWorldSeed1458 : ushort
{
    None = 0,
    DrunkWorld = 1 << 0,
    ForTheWorthy = 1 << 1,
    CelebrationMk10 = 1 << 2,
    TheConstant = 1 << 3,
    NotTheBees = 1 << 4,
    Remix = 1 << 5,
    NoTraps = 1 << 6,
    Zenith = 1 << 7,
    Skyblock = 1 << 8
}

[Flags]
public enum VanillaSecretWorldSeed1458 : ulong
{
    None = 0,
    AbandonedManors = 1UL << 0,
    Arachnophobia = 1UL << 1,
    BeamMeUp = 1UL << 2,
    BringATowel = 1UL << 3,
    CalmBeforeTheStorm = 1UL << 4,
    DoubleDaringDangers = 1UL << 5,
    ElectricBoogaloo = 1UL << 6,
    FishMox = 1UL << 7,
    HocusPocus = 1UL << 8,
    HowDidIGetHere = 1UL << 9,
    IAmError = 1UL << 10,
    InvisiblePlane = 1UL << 11,
    JaggedRocks = 1UL << 12,
    JingleAllTheWay = 1UL << 13,
    MolePeople = 1UL << 14,
    Monochrome = 1UL << 15,
    MoreTrapsPlease = 1UL << 16,
    NegativeInfinity = 1UL << 17,
    NightOfTheLivingDead = 1UL << 18,
    Planetoids = 1UL << 19,
    PumpkinSeason = 1UL << 20,
    PurifyThis = 1UL << 21,
    RainbowRoad = 1UL << 22,
    RoyaleWithCheese = 1UL << 23,
    DoesThatSparkle = 1UL << 24,
    TooEasy = 1UL << 25,
    Waterpark = 1UL << 26,
    WhatAHorribleNightToHaveACurse = 1UL << 27,
    WinterIsComing = 1UL << 28,
    XRayVision = 1UL << 29,
    TruckStop = 1UL << 30,
    SandyBritches = 1UL << 31,
    SaveTheRainforest = 1UL << 32,
    SuchGreatHeights = 1UL << 33,
    TheCareBearsMovie = 1UL << 34,
    Toadstool = 1UL << 35,
    WeDontEvenTestForThat = 1UL << 36
}

/// <summary>
/// Normalized Terraria 1.4.5.8 seed switches attached to a generated world. Special seed matching follows the
/// 1.4.5 rule that lower-cases input and ignores non-alphanumeric characters. Secret-seed identifiers are deliberately
/// retained as explicit flags so generation and persisted runtime rules can consume the same immutable profile.
/// </summary>
public readonly record struct VanillaWorldSeedProfile1458(
    VanillaSpecialWorldSeed1458 Special,
    VanillaSecretWorldSeed1458 Secret)
{
    public bool Has(VanillaSpecialWorldSeed1458 value) => (Special & value) == value;
    public bool Has(VanillaSecretWorldSeed1458 value) => (Secret & value) == value;
    public bool IsDefault => Special == VanillaSpecialWorldSeed1458.None && Secret == VanillaSecretWorldSeed1458.None;
}

public static class VanillaWorldSeedResolver1458
{
    private static readonly Dictionary<string, VanillaSecretWorldSeed1458> SecretSeeds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["abandoned manors"] = VanillaSecretWorldSeed1458.AbandonedManors,
            ["arachnophobia"] = VanillaSecretWorldSeed1458.Arachnophobia,
            ["beam me up"] = VanillaSecretWorldSeed1458.BeamMeUp,
            ["bring a towel"] = VanillaSecretWorldSeed1458.BringATowel,
            ["calm before the storm"] = VanillaSecretWorldSeed1458.CalmBeforeTheStorm,
            ["double daring dangers"] = VanillaSecretWorldSeed1458.DoubleDaringDangers,
            ["electric boogaloo"] = VanillaSecretWorldSeed1458.ElectricBoogaloo,
            ["fish mox"] = VanillaSecretWorldSeed1458.FishMox,
            ["hocus pocus"] = VanillaSecretWorldSeed1458.HocusPocus,
            ["how did i get here"] = VanillaSecretWorldSeed1458.HowDidIGetHere,
            ["i am error"] = VanillaSecretWorldSeed1458.IAmError,
            ["invisible plane"] = VanillaSecretWorldSeed1458.InvisiblePlane,
            ["jagged rocks"] = VanillaSecretWorldSeed1458.JaggedRocks,
            ["jingle all the way"] = VanillaSecretWorldSeed1458.JingleAllTheWay,
            ["mole people"] = VanillaSecretWorldSeed1458.MolePeople,
            ["monochrome"] = VanillaSecretWorldSeed1458.Monochrome,
            ["more traps please"] = VanillaSecretWorldSeed1458.MoreTrapsPlease,
            ["negative infinity"] = VanillaSecretWorldSeed1458.NegativeInfinity,
            ["night of the living dead"] = VanillaSecretWorldSeed1458.NightOfTheLivingDead,
            ["planetoids"] = VanillaSecretWorldSeed1458.Planetoids,
            ["pumpkin season"] = VanillaSecretWorldSeed1458.PumpkinSeason,
            ["purify this"] = VanillaSecretWorldSeed1458.PurifyThis,
            ["rainbow road"] = VanillaSecretWorldSeed1458.RainbowRoad,
            ["royale with cheese"] = VanillaSecretWorldSeed1458.RoyaleWithCheese,
            ["does that sparkle"] = VanillaSecretWorldSeed1458.DoesThatSparkle,
            ["too easy"] = VanillaSecretWorldSeed1458.TooEasy,
            ["waterpark"] = VanillaSecretWorldSeed1458.Waterpark,
            ["what a horrible night to have a curse"] = VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse,
            ["winter is coming"] = VanillaSecretWorldSeed1458.WinterIsComing,
            ["x-ray vision"] = VanillaSecretWorldSeed1458.XRayVision,
            ["truck stop"] = VanillaSecretWorldSeed1458.TruckStop,
            ["sandy britches"] = VanillaSecretWorldSeed1458.SandyBritches,
            ["save the rainforest"] = VanillaSecretWorldSeed1458.SaveTheRainforest,
            ["such great heights"] = VanillaSecretWorldSeed1458.SuchGreatHeights,
            ["the care bears movie"] = VanillaSecretWorldSeed1458.TheCareBearsMovie,
            ["toadstool"] = VanillaSecretWorldSeed1458.Toadstool,
            ["we don't even test for that"] = VanillaSecretWorldSeed1458.WeDontEvenTestForThat
        };

    public static VanillaWorldSeedProfile1458 Resolve(in WorldGenerationRequest request)
    {
        string seedText = request.SeedText ?? request.Seed.ToString(CultureInfo.InvariantCulture);
        VanillaSpecialWorldSeed1458 special = ResolveSpecial(seedText);
        VanillaSecretWorldSeed1458 secret = ResolveSecret(seedText);
        return new VanillaWorldSeedProfile1458(special, secret);
    }

    public static VanillaSpecialWorldSeed1458 ResolveSpecial(string seedText)
    {
        ArgumentNullException.ThrowIfNull(seedText);
        VanillaSpecialWorldSeed1458 value = VanillaSpecialWorldSeed1458.None;
        foreach (string rawPart in seedText.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            VanillaSpecialWorldSeed1458 part = ResolveSpecialToken(rawPart);
            if (part == VanillaSpecialWorldSeed1458.None)
            {
                int prefix = rawPart.LastIndexOf('.');
                if (prefix >= 0 && prefix + 1 < rawPart.Length)
                    part = ResolveSpecialToken(rawPart[(prefix + 1)..]);
            }
            value |= part;
        }

        if ((value & VanillaSpecialWorldSeed1458.Zenith) != 0)
        {
            value |= VanillaSpecialWorldSeed1458.DrunkWorld |
                VanillaSpecialWorldSeed1458.ForTheWorthy |
                VanillaSpecialWorldSeed1458.CelebrationMk10 |
                VanillaSpecialWorldSeed1458.TheConstant |
                VanillaSpecialWorldSeed1458.NotTheBees |
                VanillaSpecialWorldSeed1458.Remix |
                VanillaSpecialWorldSeed1458.NoTraps;
        }

        return value;
    }

    public static VanillaSecretWorldSeed1458 ResolveSecret(string seedText)
    {
        ArgumentNullException.ThrowIfNull(seedText);
        VanillaSecretWorldSeed1458 result = VanillaSecretWorldSeed1458.None;
        foreach (string rawPart in seedText.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart;
            int prefix = part.LastIndexOf('.');
            if (prefix >= 0 && prefix + 1 < part.Length)
                part = part[(prefix + 1)..];

            if (SecretSeeds.TryGetValue(part.Trim(), out VanillaSecretWorldSeed1458 value))
                result |= value;
        }

        return result;
    }

    private static VanillaSpecialWorldSeed1458 ResolveSpecialToken(string token) => NormalizeSpecial(token) switch
    {
        "05162020" or "5162020" => VanillaSpecialWorldSeed1458.DrunkWorld,
        "fortheworthy" => VanillaSpecialWorldSeed1458.ForTheWorthy,
        "05162021" or "5162021" or "celebrationmk10" => VanillaSpecialWorldSeed1458.CelebrationMk10,
        "theconstant" or "eye4aneye" or "eyeforaneye" => VanillaSpecialWorldSeed1458.TheConstant,
        "notthebees" => VanillaSpecialWorldSeed1458.NotTheBees,
        "dontdigup" => VanillaSpecialWorldSeed1458.Remix,
        "notraps" => VanillaSpecialWorldSeed1458.NoTraps,
        "getfixedboi" => VanillaSpecialWorldSeed1458.Zenith,
        "skyblock" => VanillaSpecialWorldSeed1458.Skyblock,
        _ => VanillaSpecialWorldSeed1458.None
    };

    private static string NormalizeSpecial(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
                normalized.Append(char.ToLowerInvariant(character));
        }
        return normalized.ToString();
    }
}
