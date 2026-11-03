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
            ["abandonedmanors"] = VanillaSecretWorldSeed1458.AbandonedManors,
            ["arachnophobia"] = VanillaSecretWorldSeed1458.Arachnophobia,
            ["beammeup"] = VanillaSecretWorldSeed1458.BeamMeUp,
            ["bringatowel"] = VanillaSecretWorldSeed1458.BringATowel,
            ["calmbeforethestorm"] = VanillaSecretWorldSeed1458.CalmBeforeTheStorm,
            ["doubledaringdangers"] = VanillaSecretWorldSeed1458.DoubleDaringDangers,
            ["electricboogaloo"] = VanillaSecretWorldSeed1458.ElectricBoogaloo,
            ["fishmox"] = VanillaSecretWorldSeed1458.FishMox,
            ["hocuspocus"] = VanillaSecretWorldSeed1458.HocusPocus,
            ["howdidigethere"] = VanillaSecretWorldSeed1458.HowDidIGetHere,
            ["iamerror"] = VanillaSecretWorldSeed1458.IAmError,
            ["invisibleplane"] = VanillaSecretWorldSeed1458.InvisiblePlane,
            ["jaggedrocks"] = VanillaSecretWorldSeed1458.JaggedRocks,
            ["jinglealltheway"] = VanillaSecretWorldSeed1458.JingleAllTheWay,
            ["molepeople"] = VanillaSecretWorldSeed1458.MolePeople,
            ["monochrome"] = VanillaSecretWorldSeed1458.Monochrome,
            ["moretrapsplease"] = VanillaSecretWorldSeed1458.MoreTrapsPlease,
            ["negativeinfinity"] = VanillaSecretWorldSeed1458.NegativeInfinity,
            ["nightofthelivingdead"] = VanillaSecretWorldSeed1458.NightOfTheLivingDead,
            ["planetoids"] = VanillaSecretWorldSeed1458.Planetoids,
            ["pumpkinseason"] = VanillaSecretWorldSeed1458.PumpkinSeason,
            ["purifythis"] = VanillaSecretWorldSeed1458.PurifyThis,
            ["rainbowroad"] = VanillaSecretWorldSeed1458.RainbowRoad,
            ["royalewithcheese"] = VanillaSecretWorldSeed1458.RoyaleWithCheese,
            ["doesthatsparkle"] = VanillaSecretWorldSeed1458.DoesThatSparkle,
            ["tooeasy"] = VanillaSecretWorldSeed1458.TooEasy,
            ["waterpark"] = VanillaSecretWorldSeed1458.Waterpark,
            ["whatahorriblenighttohaveacurse"] = VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse,
            ["winteriscoming"] = VanillaSecretWorldSeed1458.WinterIsComing,
            ["xrayvision"] = VanillaSecretWorldSeed1458.XRayVision,
            ["truckstop"] = VanillaSecretWorldSeed1458.TruckStop,
            ["sandybritches"] = VanillaSecretWorldSeed1458.SandyBritches,
            ["savetherainforest"] = VanillaSecretWorldSeed1458.SaveTheRainforest,
            ["suchgreatheights"] = VanillaSecretWorldSeed1458.SuchGreatHeights,
            ["thecarebearsmovie"] = VanillaSecretWorldSeed1458.TheCareBearsMovie,
            ["toadstool"] = VanillaSecretWorldSeed1458.Toadstool,
            ["wedonteventestforthat"] = VanillaSecretWorldSeed1458.WeDontEvenTestForThat
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

            if (SecretSeeds.TryGetValue(NormalizeSecret(rawPart), out VanillaSecretWorldSeed1458 value) ||
                SecretSeeds.TryGetValue(NormalizeSecret(part), out value))
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

    // The source secret matcher retains ASCII letters and digits after case folding.
    private static string NormalizeSecret(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            char lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
                normalized.Append(lower);
        }
        return normalized.ToString();
    }

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
