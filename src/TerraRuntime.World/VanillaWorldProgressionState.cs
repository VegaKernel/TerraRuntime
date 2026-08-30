namespace TerraRuntime.World;

/// <summary>Semantic TerrariaServer 1.4.5.8 milestone identities, independent from .wld field order.</summary>
public enum VanillaWorldProgressionId : byte
{
    EyeOfCthulhu = 0,
    EvilBoss = 1,
    Skeletron = 2,
    QueenBee = 3,
    Destroyer = 4,
    Twins = 5,
    SkeletronPrime = 6,
    AnyMechanicalBoss = 7,
    Plantera = 8,
    Golem = 9,
    KingSlime = 10,
    GoblinArmy = 11,
    Clown = 12,
    FrostLegion = 13,
    PirateInvasion = 14,
    ShadowOrbSmashed = 15,
    Hardmode = 16,
    DukeFishron = 17,
    MartianMadness = 18,
    LunaticCultist = 19,
    MoonLord = 20,
    Pumpking = 21,
    MourningWood = 22,
    IceQueen = 23,
    SantaNk1 = 24,
    Everscream = 25,
    SolarPillar = 26,
    VortexPillar = 27,
    NebulaPillar = 28,
    StardustPillar = 29,
    OldOnesArmyTier1 = 30,
    OldOnesArmyTier2 = 31,
    OldOnesArmyTier3 = 32,
    EmpressOfLight = 33,
    QueenSlime = 34,
    Deerclops = 35
}

/// <summary>Immutable semantic progression view. The packed bits are a runtime detail, not .wld flags.</summary>
public readonly record struct VanillaWorldProgressionState
{
    public const int MilestoneCount = 36;

    private readonly ulong completed;

    internal VanillaWorldProgressionState(ulong completed) => this.completed = completed;

    public bool IsComplete(VanillaWorldProgressionId milestone)
    {
        int index = (int)milestone;
        return (uint)index < MilestoneCount && (completed & (1UL << index)) != 0;
    }
}

/// <summary>Version-pinned active invasion identity. Unknown persisted values fail closed during projection.</summary>
public enum VanillaWorldInvasionId : sbyte
{
    Unknown = -1,
    None = 0,
    GoblinArmy = 1,
    SnowLegion = 2,
    PirateInvasion = 3,
    MartianMadness = 4
}

public static class VanillaWorldInvasionIds
{
    public const int Count = 5;

    public static bool TryCreate(sbyte rawType, out VanillaWorldInvasionId type)
    {
        if ((uint)rawType >= Count)
        {
            type = VanillaWorldInvasionId.Unknown;
            return false;
        }

        type = (VanillaWorldInvasionId)rawType;
        return true;
    }
}

public enum VanillaWorldEventId : byte
{
    BloodMoon = 0,
    Eclipse = 1,
    SlimeRain = 2,
    Party = 3,
    LanternNight = 4,
    Sandstorm = 5,
    Halloween = 6,
    Christmas = 7
}

/// <summary>Immutable semantic event view kept separate from permanent progression milestones.</summary>
public readonly record struct VanillaWorldEventState(
    VanillaWorldInvasionId Invasion,
    bool HasKnownInvasionIdentity,
    bool BloodMoon,
    bool Eclipse,
    bool SlimeRain,
    bool Party,
    bool LanternNight,
    bool Sandstorm,
    bool Halloween,
    bool Christmas)
{
    public bool HasActiveInvasion =>
        HasKnownInvasionIdentity && Invasion != VanillaWorldInvasionId.None;

    public bool IsActive(VanillaWorldEventId worldEvent) => worldEvent switch
    {
        VanillaWorldEventId.BloodMoon => BloodMoon,
        VanillaWorldEventId.Eclipse => Eclipse,
        VanillaWorldEventId.SlimeRain => SlimeRain,
        VanillaWorldEventId.Party => Party,
        VanillaWorldEventId.LanternNight => LanternNight,
        VanillaWorldEventId.Sandstorm => Sandstorm,
        VanillaWorldEventId.Halloween => Halloween,
        VanillaWorldEventId.Christmas => Christmas,
        _ => false
    };
}

/// <summary>Projects validated persistence fields into gameplay-owned progression and event state.</summary>
public static class VanillaWorldStateProjection
{
    public static VanillaWorldProgressionState GetProgression(WorldFileRuntimeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ulong completed = 0;
        Set(ref completed, VanillaWorldProgressionId.EyeOfCthulhu, metadata.DownedBoss1);
        Set(ref completed, VanillaWorldProgressionId.EvilBoss, metadata.DownedBoss2);
        Set(ref completed, VanillaWorldProgressionId.Skeletron, metadata.DownedBoss3);
        Set(ref completed, VanillaWorldProgressionId.QueenBee, metadata.DownedQueenBee);
        Set(ref completed, VanillaWorldProgressionId.Destroyer, metadata.DownedMechBoss1);
        Set(ref completed, VanillaWorldProgressionId.Twins, metadata.DownedMechBoss2);
        Set(ref completed, VanillaWorldProgressionId.SkeletronPrime, metadata.DownedMechBoss3);
        Set(ref completed, VanillaWorldProgressionId.AnyMechanicalBoss, metadata.DownedMechBossAny);
        Set(ref completed, VanillaWorldProgressionId.Plantera, metadata.DownedPlantBoss);
        Set(ref completed, VanillaWorldProgressionId.Golem, metadata.DownedGolemBoss);
        Set(ref completed, VanillaWorldProgressionId.KingSlime, metadata.DownedSlimeKing);
        Set(ref completed, VanillaWorldProgressionId.GoblinArmy, metadata.DownedGoblins);
        Set(ref completed, VanillaWorldProgressionId.Clown, metadata.DownedClown);
        Set(ref completed, VanillaWorldProgressionId.FrostLegion, metadata.DownedFrost);
        Set(ref completed, VanillaWorldProgressionId.PirateInvasion, metadata.DownedPirates);
        Set(ref completed, VanillaWorldProgressionId.ShadowOrbSmashed, metadata.ShadowOrbSmashed);
        Set(ref completed, VanillaWorldProgressionId.Hardmode, metadata.HardMode);
        Set(ref completed, VanillaWorldProgressionId.DukeFishron, metadata.DownedFishron);
        Set(ref completed, VanillaWorldProgressionId.MartianMadness, metadata.DownedMartians);
        Set(ref completed, VanillaWorldProgressionId.LunaticCultist, metadata.DownedAncientCultist);
        Set(ref completed, VanillaWorldProgressionId.MoonLord, metadata.DownedMoonlord);
        Set(ref completed, VanillaWorldProgressionId.Pumpking, metadata.DownedHalloweenKing);
        Set(ref completed, VanillaWorldProgressionId.MourningWood, metadata.DownedHalloweenTree);
        Set(ref completed, VanillaWorldProgressionId.IceQueen, metadata.DownedChristmasIceQueen);
        Set(ref completed, VanillaWorldProgressionId.SantaNk1, metadata.DownedChristmasSantank);
        Set(ref completed, VanillaWorldProgressionId.Everscream, metadata.DownedChristmasTree);
        Set(ref completed, VanillaWorldProgressionId.SolarPillar, metadata.DownedTowerSolar);
        Set(ref completed, VanillaWorldProgressionId.VortexPillar, metadata.DownedTowerVortex);
        Set(ref completed, VanillaWorldProgressionId.NebulaPillar, metadata.DownedTowerNebula);
        Set(ref completed, VanillaWorldProgressionId.StardustPillar, metadata.DownedTowerStardust);
        Set(ref completed, VanillaWorldProgressionId.OldOnesArmyTier1, metadata.DownedDd2InvasionT1);
        Set(ref completed, VanillaWorldProgressionId.OldOnesArmyTier2, metadata.DownedDd2InvasionT2);
        Set(ref completed, VanillaWorldProgressionId.OldOnesArmyTier3, metadata.DownedDd2InvasionT3);
        Set(ref completed, VanillaWorldProgressionId.EmpressOfLight, metadata.DownedEmpressOfLight);
        Set(ref completed, VanillaWorldProgressionId.QueenSlime, metadata.DownedQueenSlime);
        Set(ref completed, VanillaWorldProgressionId.Deerclops, metadata.DownedDeerclops);
        return new VanillaWorldProgressionState(completed);
    }

    public static VanillaWorldEventState GetEvents(WorldFileRuntimeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        bool knownInvasion = VanillaWorldInvasionIds.TryCreate(metadata.InvasionType, out VanillaWorldInvasionId invasion);
        return new VanillaWorldEventState(
            invasion,
            knownInvasion,
            metadata.BloodMoon,
            metadata.Eclipse,
            metadata.SlimeRainActive,
            metadata.PartyIsUp,
            metadata.LanternsUp,
            metadata.SandstormHappening,
            metadata.ForceHalloweenForToday || metadata.ForceHalloweenForever,
            metadata.ForceXMasForToday || metadata.ForceXMasForever);
    }

    private static void Set(ref ulong state, VanillaWorldProgressionId milestone, bool complete)
    {
        if (complete)
            state |= 1UL << (int)milestone;
    }
}
