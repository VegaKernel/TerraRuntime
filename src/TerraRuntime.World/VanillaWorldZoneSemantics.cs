namespace TerraRuntime.World;

/// <summary>Mutually exclusive vertical SceneMetrics zone for one tile coordinate.</summary>
public enum VanillaWorldDepthZone : byte
{
    Sky = 0,
    Overworld = 1,
    DirtLayer = 2,
    RockLayer = 3,
    Underworld = 4
}

/// <summary>
/// Independent named biome memberships produced by a scene scan. These are runtime semantics, not protocol bits.
/// Multiple memberships may coexist when Terraria thresholds and special seeds allow it.
/// </summary>
[Flags]
public enum VanillaWorldBiomeFlags : uint
{
    None = 0,
    Corruption = 1u << 0,
    Crimson = 1u << 1,
    Hallow = 1u << 2,
    Jungle = 1u << 3,
    Snow = 1u << 4,
    Desert = 1u << 5,
    GlowingMushroom = 1u << 6,
    Meteor = 1u << 7,
    Graveyard = 1u << 8,
    Dungeon = 1u << 9,
    LihzahrdTemple = 1u << 10,
    Granite = 1u << 11,
    Marble = 1u << 12,
    Hive = 1u << 13,
    GemCave = 1u << 14,
    Beach = 1u << 15,
    UndergroundDesert = 1u << 16,
    Shimmer = 1u << 17
}

public static class VanillaWorldBiomeFlagMasks
{
    public const VanillaWorldBiomeFlags Evil =
        VanillaWorldBiomeFlags.Corruption |
        VanillaWorldBiomeFlags.Crimson;

    public const VanillaWorldBiomeFlags Known =
        Evil |
        VanillaWorldBiomeFlags.Hallow |
        VanillaWorldBiomeFlags.Jungle |
        VanillaWorldBiomeFlags.Snow |
        VanillaWorldBiomeFlags.Desert |
        VanillaWorldBiomeFlags.GlowingMushroom |
        VanillaWorldBiomeFlags.Meteor |
        VanillaWorldBiomeFlags.Graveyard |
        VanillaWorldBiomeFlags.Dungeon |
        VanillaWorldBiomeFlags.LihzahrdTemple |
        VanillaWorldBiomeFlags.Granite |
        VanillaWorldBiomeFlags.Marble |
        VanillaWorldBiomeFlags.Hive |
        VanillaWorldBiomeFlags.GemCave |
        VanillaWorldBiomeFlags.Beach |
        VanillaWorldBiomeFlags.UndergroundDesert |
        VanillaWorldBiomeFlags.Shimmer;
}

/// <summary>Validated composition of one mutually exclusive depth zone and independent biome memberships.</summary>
public readonly record struct VanillaWorldZoneState
{
    private VanillaWorldZoneState(VanillaWorldDepthZone depth, VanillaWorldBiomeFlags biomes)
    {
        Depth = depth;
        Biomes = biomes;
    }

    public VanillaWorldDepthZone Depth { get; }

    public VanillaWorldBiomeFlags Biomes { get; }

    public bool BelowSurface => Depth is
        VanillaWorldDepthZone.DirtLayer or
        VanillaWorldDepthZone.RockLayer or
        VanillaWorldDepthZone.Underworld;

    public bool HasBiome(VanillaWorldBiomeFlags biome) =>
        biome != VanillaWorldBiomeFlags.None &&
        (biome & ~VanillaWorldBiomeFlagMasks.Known) == 0 &&
        (Biomes & biome) == biome;

    public static bool TryCreate(
        VanillaWorldDepthZone depth,
        VanillaWorldBiomeFlags biomes,
        out VanillaWorldZoneState state)
    {
        if (!Enum.IsDefined(depth) || (biomes & ~VanillaWorldBiomeFlagMasks.Known) != 0)
        {
            state = default;
            return false;
        }

        state = new VanillaWorldZoneState(depth, biomes);
        return true;
    }
}

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 SceneMetrics vertical classification. Tile-count biome scans remain a
/// separate producer and feed their named result into <see cref="VanillaWorldZoneState"/>.
/// </summary>
public static class VanillaWorldDepthZoneResolver
{
    private const float SkyHeightWorldSurfaceFactor = 0.35f;
    private const int UnderworldLayerOffset = 200;

    public static bool TryResolve(
        WorldDimensions dimensions,
        double worldSurface,
        double rockLayer,
        int tileY,
        out VanillaWorldDepthZone zone)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        int underworldLayer = dimensions.HeightTiles - UnderworldLayerOffset;
        if ((uint)tileY >= (uint)dimensions.HeightTiles ||
            !double.IsFinite(worldSurface) ||
            !double.IsFinite(rockLayer) ||
            worldSurface <= 0d ||
            worldSurface > rockLayer ||
            rockLayer >= underworldLayer)
        {
            zone = default;
            return false;
        }

        if (tileY <= worldSurface * SkyHeightWorldSurfaceFactor)
            zone = VanillaWorldDepthZone.Sky;
        else if (tileY <= worldSurface)
            zone = VanillaWorldDepthZone.Overworld;
        else if (tileY <= rockLayer)
            zone = VanillaWorldDepthZone.DirtLayer;
        else if (tileY <= underworldLayer)
            zone = VanillaWorldDepthZone.RockLayer;
        else
            zone = VanillaWorldDepthZone.Underworld;

        return true;
    }
}
