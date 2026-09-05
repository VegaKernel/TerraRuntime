using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 single-cell mining slice. Multi-tile/frame-important objects stay on their
/// dedicated authority paths, while ordinary terrain blocks are admitted here so normal pickaxes can actually mine
/// a generated world instead of being limited to dirt/stone/sand.
/// </summary>
public static class VanillaSimpleTileKillCatalog
{
    public static bool IsSupported(TileTypeId tileType) => tileType.Value is
        0 or    // Dirt
        1 or    // Stone
        2 or    // Grass
        23 or   // Corrupt grass
        25 or   // Ebonstone
        53 or   // Sand
        57 or   // Ash
        59 or   // Mud
        60 or   // Jungle grass
        70 or   // Mushroom grass
        109 or  // Hallowed grass
        112 or  // Ebonsand
        116 or  // Pearlsand
        117 or  // Pearlstone
        123 or  // Silt
        147 or  // Snow
        161 or  // Ice
        163 or  // Corrupt ice
        164 or  // Hallowed ice
        199 or  // Crimson grass
        200 or  // Crimsand
        203 or  // Crimstone
        224 or  // Slush
        225 or  // Hive
        226 or  // Lihzahrd brick
        234 or  // Chlorophyte ore
        396 or 397 or 398 or 399 or 400 or 401 or 402 or 403 or 404 or
        407 or 408;
}
