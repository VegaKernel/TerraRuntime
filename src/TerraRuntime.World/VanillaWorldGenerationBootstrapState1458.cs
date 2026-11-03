using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed subset of TerrariaServer 1.4.5.8 WorldGen.Reset state that is required by generation passes and
/// persisted-world parity. The bootstrap deliberately owns the random choices made before Terrain so later source
/// ports continue from the same shared UnifiedRandom stream instead of reconstructing values independently.
/// </summary>
internal sealed class VanillaWorldGenerationBootstrapState1458
{
    public int RainTime { get; init; }
    public bool Raining { get; init; }
    public float MaxRaining { get; init; }
    public float CloudAlpha { get; init; }
    public float WindSpeedTarget { get; init; }
    public int[] DungeonSides { get; init; } = [];
    public int[] DungeonLocations { get; init; } = [];
    public int CurrentDungeon { get; init; }
    public double DualDungeonNormalizedDistanceSafeFromDither { get; init; }
    public bool DayTime { get; init; } = true;
    public double Time { get; init; } = WorldFileFreshRuntimeMetadata326Encoder.InitialTime;
    public bool GenuineParty { get; init; }
    public int PartyDaysOnCooldown { get; init; }
    public bool AfterPartyOfDoom { get; init; }
    // Main.starGame affects generation but has no vanilla .wld field.
    public bool StarGame { get; init; }
    public bool NotTheBeesAndForTheWorthyNoCelebration { get; init; }
    public bool NoTrapsAndForTheWorthyNoCelebration { get; init; }
    public bool FlipInfections { get; init; }
    public int JungleHut { get; init; }
    public bool CrimsonLeft { get; init; }
    public int NumClouds { get; init; }
    public float WindSpeedCurrent { get; init; }
    public required int[] HellChestItems { get; init; }
    public int SlimeRainTime { get; init; }
    public int CloudBackgroundActive { get; init; }
    public int CopperOre { get; init; }
    public int IronOre { get; init; }
    public int SilverOre { get; init; }
    public int GoldOre { get; init; }
    public int CopperBar { get; init; }
    public int IronBar { get; init; }
    public int SilverBar { get; init; }
    public int GoldBar { get; init; }
    public bool RandomEvilCrimson { get; init; }
    public bool EffectiveCrimson { get; init; }
    public int WorldId { get; init; }
    public required int[] TreeX { get; init; }
    public required int[] TreeStyle { get; init; }
    public required int[] CaveBackX { get; init; }
    public required int[] CaveBackStyle { get; init; }
    public int IceBackStyle { get; init; }
    public int HellBackStyle { get; init; }
    public int JungleBackStyle { get; init; }
    public required int[] ForestBackgroundStyles { get; init; }
    public int CorruptBackground { get; init; }
    public int JungleBackground { get; init; }
    public int SnowBackground { get; init; }
    public int HallowBackground { get; init; }
    public int CrimsonBackground { get; init; }
    public int DesertBackground { get; init; }
    public int OceanBackground { get; init; }
    public int MushroomBackground { get; init; }
    public int UnderworldBackground { get; init; }
    public int MoonType { get; init; }
    public int DungeonSide { get; init; }
    public int JungleOriginX { get; init; }
    public int SnowOriginLeft { get; init; }
    public int SnowOriginRight { get; init; }
    public int LeftBeachEnd { get; init; }
    public int RightBeachStart { get; init; }
    public int DungeonLocation { get; init; }
    public int SkyLakes { get; init; }
    public int ExtraBastStatueCountMax { get; init; }
}
