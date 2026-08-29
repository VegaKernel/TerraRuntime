using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace TerraRuntime.World;

[Flags]
internal enum VanillaWorldSeedFlags1458 : ushort
{
    None = 0,
    DrunkWorld = 1 << 0,
    GetGoodWorld = 1 << 1,
    TenthAnniversaryWorld = 1 << 2,
    DontStarveWorld = 1 << 3,
    NotTheBeesWorld = 1 << 4,
    RemixWorld = 1 << 5,
    NoTrapsWorld = 1 << 6,
    ZenithWorld = 1 << 7,
    SkyblockWorld = 1 << 8,
    VampireSeed = 1 << 9,
    InfectedSeed = 1 << 10,
    TeamBasedSpawnsSeed = 1 << 11,
    DualDungeonsSeed = 1 << 12,
    MoreLightningSeed = 1 << 13,
    NoLightningSeed = 1 << 14
}

[Flags]
internal enum VanillaSecretSeedModifier1458 : ulong
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
/// Terraria 1.4.5.8 seed interpretation used by the built-in vanilla generator. Secret modifier codes are kept as
/// data rather than spread through generation passes so combination handling remains deterministic and testable.
/// The 37-code catalog is the current 1.4.5.8 set; the six modifiers with dedicated SaveWorldFlags booleans are
/// projected into <see cref="VanillaWorldSeedFlags1458"/> as well.
/// </summary>
internal readonly record struct VanillaWorldSeedProfile1458(
    int NumericSeed,
    VanillaWorldSeedFlags1458 Flags,
    VanillaSecretSeedModifier1458 SecretModifiers)
{
    public bool HasFlag(VanillaWorldSeedFlags1458 flag) => (Flags & flag) == flag;
    public bool HasModifier(VanillaSecretSeedModifier1458 modifier) => (SecretModifiers & modifier) == modifier;

    public static VanillaWorldSeedProfile1458 Parse(string? seedText, ulong fallbackSeed)
    {
        string text = string.IsNullOrWhiteSpace(seedText)
            ? fallbackSeed.ToString(CultureInfo.InvariantCulture)
            : seedText.Trim();

        int numericSeed = ResolveNumericSeed(text);
        VanillaWorldSeedFlags1458 flags = VanillaWorldSeedFlags1458.None;
        VanillaSecretSeedModifier1458 modifiers = VanillaSecretSeedModifier1458.None;

        foreach (string rawToken in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string token = StripVersionPrefix(rawToken);
            string normalized = NormalizeIdentifier(token);
            if (normalized.Length == 0)
                continue;

            flags |= ResolveSpecialFlags(normalized);
            VanillaSecretSeedModifier1458 modifier = ResolveSecretModifier(normalized);
            modifiers |= modifier;
            flags |= ResolvePersistentSecretFlag(modifier);
        }

        // Zenith is the vanilla aggregate special world. Keeping its constituent flags explicit is important because
        // existing save/runtime paths query those booleans independently rather than treating Zenith as an alias.
        if ((flags & VanillaWorldSeedFlags1458.ZenithWorld) != 0)
        {
            flags |= VanillaWorldSeedFlags1458.DrunkWorld |
                VanillaWorldSeedFlags1458.GetGoodWorld |
                VanillaWorldSeedFlags1458.DontStarveWorld |
                VanillaWorldSeedFlags1458.NotTheBeesWorld |
                VanillaWorldSeedFlags1458.RemixWorld |
                VanillaWorldSeedFlags1458.NoTrapsWorld;
        }

        return new VanillaWorldSeedProfile1458(numericSeed, flags, modifiers);
    }

    internal static int ResolveNumericSeed(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
            return numeric;

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        return unchecked((int)Crc32.HashToUInt32(utf8));
    }

    internal static string NormalizeIdentifier(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string StripVersionPrefix(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan().Trim();
        int lastDot = span.LastIndexOf('.');
        if (lastDot <= 0)
            return span.ToString();

        ReadOnlySpan<char> prefix = span[..lastDot];
        bool versionLike = true;
        foreach (char character in prefix)
        {
            if (character != '.' && !char.IsAsciiDigit(character))
            {
                versionLike = false;
                break;
            }
        }

        return versionLike ? span[(lastDot + 1)..].ToString() : span.ToString();
    }

    private static VanillaWorldSeedFlags1458 ResolveSpecialFlags(string normalized) => normalized switch
    {
        "05162020" or "5162020" => VanillaWorldSeedFlags1458.DrunkWorld,
        "fortheworthy" => VanillaWorldSeedFlags1458.GetGoodWorld,
        "05162021" or "5162021" or "celebrationmk10" => VanillaWorldSeedFlags1458.TenthAnniversaryWorld,
        "constant" or "theconstant" or "eye4aneye" or "eyeforaneye" => VanillaWorldSeedFlags1458.DontStarveWorld,
        "notthebees" => VanillaWorldSeedFlags1458.NotTheBeesWorld,
        "dontdigup" => VanillaWorldSeedFlags1458.RemixWorld,
        "notraps" => VanillaWorldSeedFlags1458.NoTrapsWorld,
        "getfixedboi" => VanillaWorldSeedFlags1458.ZenithWorld,
        "skyblock" => VanillaWorldSeedFlags1458.SkyblockWorld,
        _ => VanillaWorldSeedFlags1458.None
    };

    private static VanillaSecretSeedModifier1458 ResolveSecretModifier(string normalized) => normalized switch
    {
        "abandonedmanors" => VanillaSecretSeedModifier1458.AbandonedManors,
        "arachnophobia" => VanillaSecretSeedModifier1458.Arachnophobia,
        "beamMeup" => VanillaSecretSeedModifier1458.BeamMeUp,
        "bringatowel" => VanillaSecretSeedModifier1458.BringATowel,
        "calmbeforethestorm" => VanillaSecretSeedModifier1458.CalmBeforeTheStorm,
        "doubledaringdangers" => VanillaSecretSeedModifier1458.DoubleDaringDangers,
        "electricboogaloo" => VanillaSecretSeedModifier1458.ElectricBoogaloo,
        "fishmox" => VanillaSecretSeedModifier1458.FishMox,
        "hocuspocus" => VanillaSecretSeedModifier1458.HocusPocus,
        "howdidigethere" => VanillaSecretSeedModifier1458.HowDidIGetHere,
        "iamerror" => VanillaSecretSeedModifier1458.IAmError,
        "invisibleplane" => VanillaSecretSeedModifier1458.InvisiblePlane,
        "jaggedrocks" => VanillaSecretSeedModifier1458.JaggedRocks,
        "jinglealltheway" => VanillaSecretSeedModifier1458.JingleAllTheWay,
        "molepeople" => VanillaSecretSeedModifier1458.MolePeople,
        "monochrome" => VanillaSecretSeedModifier1458.Monochrome,
        "moretrapsplease" => VanillaSecretSeedModifier1458.MoreTrapsPlease,
        "negativeinfinity" => VanillaSecretSeedModifier1458.NegativeInfinity,
        "nightofthelivingdead" => VanillaSecretSeedModifier1458.NightOfTheLivingDead,
        "planetoids" => VanillaSecretSeedModifier1458.Planetoids,
        "pumpkinseason" => VanillaSecretSeedModifier1458.PumpkinSeason,
        "purifythis" => VanillaSecretSeedModifier1458.PurifyThis,
        "rainbowroad" => VanillaSecretSeedModifier1458.RainbowRoad,
        "royalewithcheese" => VanillaSecretSeedModifier1458.RoyaleWithCheese,
        "doesthatsparkle" => VanillaSecretSeedModifier1458.DoesThatSparkle,
        "tooeasy" => VanillaSecretSeedModifier1458.TooEasy,
        "waterpark" => VanillaSecretSeedModifier1458.Waterpark,
        "whatahorriblenighttohaveacurse" => VanillaSecretSeedModifier1458.WhatAHorribleNightToHaveACurse,
        "winteriscoming" => VanillaSecretSeedModifier1458.WinterIsComing,
        "xrayvision" => VanillaSecretSeedModifier1458.XRayVision,
        "truckstop" => VanillaSecretSeedModifier1458.TruckStop,
        "sandybritches" => VanillaSecretSeedModifier1458.SandyBritches,
        "savetherainforest" => VanillaSecretSeedModifier1458.SaveTheRainforest,
        "suchgreatheights" => VanillaSecretSeedModifier1458.SuchGreatHeights,
        "thecarebearsmovie" => VanillaSecretSeedModifier1458.TheCareBearsMovie,
        "toadstool" => VanillaSecretSeedModifier1458.Toadstool,
        "wedonteventestforthat" => VanillaSecretSeedModifier1458.WeDontEvenTestForThat,
        _ => VanillaSecretSeedModifier1458.None
    };

    private static VanillaWorldSeedFlags1458 ResolvePersistentSecretFlag(VanillaSecretSeedModifier1458 modifier) => modifier switch
    {
        VanillaSecretSeedModifier1458.WhatAHorribleNightToHaveACurse => VanillaWorldSeedFlags1458.VampireSeed,
        VanillaSecretSeedModifier1458.PurifyThis => VanillaWorldSeedFlags1458.InfectedSeed,
        VanillaSecretSeedModifier1458.RoyaleWithCheese => VanillaWorldSeedFlags1458.TeamBasedSpawnsSeed,
        VanillaSecretSeedModifier1458.DoubleDaringDangers => VanillaWorldSeedFlags1458.DualDungeonsSeed,
        VanillaSecretSeedModifier1458.ElectricBoogaloo => VanillaWorldSeedFlags1458.MoreLightningSeed,
        VanillaSecretSeedModifier1458.CalmBeforeTheStorm => VanillaWorldSeedFlags1458.NoLightningSeed,
        _ => VanillaWorldSeedFlags1458.None
    };
}
