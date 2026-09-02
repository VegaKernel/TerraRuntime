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

    /// <summary>
    /// True when the source-backed <c>WorldGen.Reset</c> and <c>TerrainPass</c> slice can run.
    /// TerrariaServer 1.4.5.8 gives the pure Don't Dig Up/Remix profile distinct Reset and
    /// Terrain branches, both of which are ported here. Zenith implies Remix but also enables
    /// several other special-seed branches, so it is deliberately excluded until those branches
    /// have independent source-backed evidence. Secret switches are excluded for the same reason.
    /// This is intentionally narrower than complete source-backed pipeline support: all later
    /// source-shaped overlays remain ordinary-world only.
    /// </summary>
    public bool SupportsSourceBackedResetAndTerrain =>
        IsDefault ||
        (Special == VanillaSpecialWorldSeed1458.Remix && Secret == VanillaSecretWorldSeed1458.None);
}
