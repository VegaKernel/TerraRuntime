using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed subset of TerrariaServer 1.4.5.8 WorldGen.Reset state that is required by generation passes and
/// persisted-world parity. The bootstrap deliberately owns the random choices made before Terrain so later source
/// ports continue from the same shared UnifiedRandom stream instead of reconstructing values independently.
/// </summary>
internal sealed class VanillaWorldGenerationBootstrapState1458
{
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
