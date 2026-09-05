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
        6 or 7 or 8 or 9 or // Iron/Copper/Gold/Silver ores
        22 or   // Demonite
        23 or   // Corrupt grass
        25 or   // Ebonstone
        30 or   // Wood block
        37 or   // Meteorite
        38 or 39 or 40 or // Gray/Red brick, Clay
        41 or 43 or 44 or // Dungeon bricks
        45 or 46 or 47 or // Gold/Silver/Copper brick
        53 or   // Sand
        54 or   // Glass
        56 or   // Obsidian
        57 or   // Ash
        58 or   // Hellstone
        59 or   // Mud
        60 or   // Jungle grass
        63 or 64 or 65 or 66 or 67 or 68 or // Gem blocks/ores
        70 or   // Mushroom grass
        75 or 76 or // Obsidian/Hellstone brick
        107 or 108 or 111 or // Cobalt/Mythril/Adamantite
        109 or  // Hallowed grass
        112 or  // Ebonsand
        116 or  // Pearlsand
        117 or  // Pearlstone
        123 or  // Silt
        147 or  // Snow
        151 or  // Sandstone brick
        161 or  // Ice
        163 or  // Corrupt ice
        164 or  // Hallowed ice
        166 or 167 or 168 or 169 or // Tin/Lead/Tungsten/Platinum
        179 or 180 or 181 or 182 or 183 or // Mossy stone variants
        189 or  // Cloud
        191 or  // Living wood
        196 or  // Rain cloud
        199 or  // Crimson grass
        202 or  // Sunplate block
        200 or  // Flesh ice
        203 or  // Crimstone
        204 or  // Crimtane
        211 or  // Chlorophyte
        221 or 222 or 223 or // Palladium/Orichalcum/Titanium
        224 or  // Slush
        225 or  // Hive
        226 or  // Lihzahrd brick
        234 or  // Crimsand
        357 or  // Marble block
        367 or 368 or 369 or // Natural marble/granite + granite block
        370 or  // Meteorite brick
        383 or  // Living mahogany
        396 or 397 or 398 or 399 or 400 or 401 or 402 or 403 or 404 or
        407 or 408;
}
