namespace TerraRuntime.World;

public readonly record struct WorldSpawnPoint(short X, short Y);

public readonly record struct WorldOreTiers(
    short Copper,
    short Iron,
    short Silver,
    short Gold,
    short Cobalt,
    short Mythril,
    short Adamantite);

public sealed class WorldFileRuntimeMetadata
{
    public byte GameMode { get; init; }

    public bool DrunkWorld { get; init; }
    public bool GetGoodWorld { get; init; }
    public bool TenthAnniversaryWorld { get; init; }
    public bool DontStarveWorld { get; init; }
    public bool NotTheBeesWorld { get; init; }
    public bool RemixWorld { get; init; }
    public bool NoTrapsWorld { get; init; }
    public bool ZenithWorld { get; init; }
    public bool SkyblockWorld { get; init; }
    public bool VampireSeed { get; init; }
    public bool InfectedSeed { get; init; }
    public bool TeamBasedSpawnsSeed { get; init; }
    public bool DualDungeonsSeed { get; init; }
    public bool MoreLightningSeed { get; init; }
    public bool NoLightningSeed { get; init; }

    public byte MoonType { get; init; }
    public int[] TreeX { get; init; } = new int[3];
    public byte[] TreeStyles { get; init; } = new byte[4];
    public int[] CaveBackX { get; init; } = new int[3];
    public byte[] CaveBackStyles { get; init; } = new byte[4];
    public byte IceBackStyle { get; init; }
    public byte JungleBackStyle { get; init; }
    public byte HellBackStyle { get; init; }

    public short SpawnX { get; init; }
    public short SpawnY { get; init; }
    public short WorldSurface { get; init; }
    public short RockLayer { get; init; }
    public int Time { get; init; }
    public bool DayTime { get; init; }
    public byte MoonPhase { get; init; }
    public bool BloodMoon { get; init; }
    public bool Eclipse { get; init; }
    public short DungeonX { get; init; }
    public short DungeonY { get; init; }

    public bool Crimson { get; init; }
    public bool DownedBoss1 { get; init; }
    public bool DownedBoss2 { get; init; }
    public bool DownedBoss3 { get; init; }
    public bool DownedQueenBee { get; init; }
    public bool DownedMechBoss1 { get; init; }
    public bool DownedMechBoss2 { get; init; }
    public bool DownedMechBoss3 { get; init; }
    public bool DownedMechBossAny { get; init; }
    public bool DownedPlantBoss { get; init; }
    public bool DownedGolemBoss { get; init; }
    public bool DownedSlimeKing { get; init; }
    public bool DownedGoblins { get; init; }
    public bool DownedClown { get; init; }
    public bool DownedFrost { get; init; }
    public bool DownedPirates { get; init; }
    public bool ShadowOrbSmashed { get; init; }
    public bool HardMode { get; init; }

    public double SlimeRainTime { get; init; }
    public byte SundialCooldown { get; init; }
    public bool Raining { get; init; }
    public float MaxRain { get; init; }
    public WorldOreTiers OreTiers { get; init; }

    public byte TreeBackground { get; init; }
    public byte TreeBackground2 { get; init; }
    public byte TreeBackground3 { get; init; }
    public byte TreeBackground4 { get; init; }
    public byte CorruptionBackground { get; init; }
    public byte JungleBackground { get; init; }
    public byte SnowBackground { get; init; }
    public byte HallowBackground { get; init; }
    public byte CrimsonBackground { get; init; }
    public byte DesertBackground { get; init; }
    public byte OceanBackground { get; init; }
    public byte MushroomBackground { get; init; }
    public byte UnderworldBackground { get; init; }
    public bool CloudBackgroundActive { get; init; }
    public byte CloudCount { get; init; }
    public float WindSpeed { get; init; }

    public bool FastForwardTimeToDawn { get; init; }
    public bool DownedFishron { get; init; }
    public bool DownedMartians { get; init; }
    public bool DownedAncientCultist { get; init; }
    public bool DownedMoonlord { get; init; }
    public bool DownedHalloweenKing { get; init; }
    public bool DownedHalloweenTree { get; init; }
    public bool DownedChristmasIceQueen { get; init; }
    public bool DownedChristmasSantank { get; init; }
    public bool DownedChristmasTree { get; init; }
    public bool DownedTowerSolar { get; init; }
    public bool DownedTowerVortex { get; init; }
    public bool DownedTowerNebula { get; init; }
    public bool DownedTowerStardust { get; init; }

    public bool PartyManual { get; init; }
    public bool PartyGenuine { get; init; }
    public bool SandstormHappening { get; init; }
    public float SandstormIntendedSeverity { get; init; }
    public bool DownedDd2InvasionT1 { get; init; }
    public bool DownedDd2InvasionT2 { get; init; }
    public bool DownedDd2InvasionT3 { get; init; }

    public bool CombatBookWasUsed { get; init; }
    public bool LanternNightGenuine { get; init; }
    public bool LanternNightManual { get; init; }
    public byte[] TreeTopVariations { get; init; } = new byte[13];
    public bool ForceHalloweenForToday { get; init; }
    public bool ForceXMasForToday { get; init; }

    public bool BoughtCat { get; init; }
    public bool BoughtDog { get; init; }
    public bool BoughtBunny { get; init; }
    public bool DownedEmpressOfLight { get; init; }
    public bool DownedQueenSlime { get; init; }
    public bool DownedDeerclops { get; init; }
    public bool UnlockedSlimeBlueSpawn { get; init; }
    public bool UnlockedTruffleSpawn { get; init; }
    public bool CombatBookVolumeTwoWasUsed { get; init; }
    public bool PeddlersSatchelWasUsed { get; init; }
    public bool UnlockedSlimeGreenSpawn { get; init; }
    public bool UnlockedSlimeOldSpawn { get; init; }
    public bool UnlockedSlimePurpleSpawn { get; init; }
    public bool UnlockedSlimeRainbowSpawn { get; init; }
    public bool UnlockedSlimeRedSpawn { get; init; }
    public bool UnlockedSlimeYellowSpawn { get; init; }
    public bool UnlockedSlimeCopperSpawn { get; init; }
    public bool FastForwardTimeToDusk { get; init; }
    public byte MoondialCooldown { get; init; }
    public bool ForceHalloweenForever { get; init; }
    public bool ForceXMasForever { get; init; }

    public sbyte InvasionType { get; init; }
    public WorldSpawnPoint[] ExtraSpawnPoints { get; init; } = Array.Empty<WorldSpawnPoint>();

    public bool PartyIsUp => PartyManual || PartyGenuine;
    public bool LanternsUp => LanternNightGenuine || LanternNightManual;
    public bool SlimeRainActive => SlimeRainTime > 0d;
    public float NetworkRain => Raining ? MaxRain : 0f;
}

public readonly record struct WorldFileRuntimeMetadataLimits(
    int MaxStringBytes,
    long MaxTotalStringBytes,
    int MaxAnglerNames,
    int MaxBannerEntries,
    int MaxPartyNpcEntries,
    int MaxManifestBytes)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxStringBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalStringBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAnglerNames);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBannerEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxPartyNpcEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxManifestBytes);
    }
}
