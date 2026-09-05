using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// TerrariaServer 1.4.5.8 pick-power gates for the ordinary single-cell mining slice.
/// Position-dependent gates intentionally receive the authoritative world store instead of trusting packet data.
/// </summary>
internal static class VanillaTileMiningRequirements1458
{
    public static bool CanMine(
        WorldTileStore tiles,
        int tileX,
        int tileY,
        TileTypeId tileType,
        short pickPower)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        int type = tileType.Value;
        double worldSurface = tiles.WorldSurfaceTiles ?? tiles.Dimensions.HeightTiles * 0.3d;
        int required = type switch
        {
            25 or 117 or 203 => 65,      // Ebonstone / Pearlstone / Crimstone
            37 => 50,                    // Meteorite
            56 => 55,                    // Obsidian
            58 => 65,                    // Hellstone
            107 or 221 => 100,           // Cobalt / Palladium
            108 or 222 => 110,           // Mythril / Orichalcum
            111 or 223 => 150,           // Adamantite / Titanium
            211 => 200,                  // Chlorophyte
            226 => 210,                  // Lihzahrd brick
            _ => 0
        };

        if ((type == 22 || type == 204) && tileY > worldSurface)
            required = Math.Max(required, 55);

        if (IsDungeonBrick(type) && tileY > worldSurface &&
            (tileX < tiles.Dimensions.WidthTiles * 0.35d || tileX > tiles.Dimensions.WidthTiles * 0.65d))
        {
            required = Math.Max(required, 100);
        }

        return pickPower >= required;
    }

    private static bool IsDungeonBrick(int type) => type is 41 or 43 or 44 or 677 or 678 or 679;
}
