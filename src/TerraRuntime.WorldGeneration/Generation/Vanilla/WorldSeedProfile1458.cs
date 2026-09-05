using System.Globalization;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

public static class WorldSeedResolver1458
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
