using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed class VanillaMothronWorldEnvironment1458(WorldTileStore tiles, double worldSurfaceTiles) :
    IVanillaMothronEnvironment1458
{
    public int WidthTiles => tiles.Dimensions.WidthTiles;
    public int HeightTiles => tiles.Dimensions.HeightTiles;
    public double WorldSurfaceTiles => worldSurfaceTiles;

    public bool TryReadTile(int x, int y, out bool solid, out bool lava)
    {
        solid = lava = false;
        if ((uint)x >= (uint)WidthTiles || (uint)y >= (uint)HeightTiles)
            return false;
        var tile = tiles.Get(x, y);
        solid = tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0;
        lava = tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava;
        return true;
    }

    public bool CanHit(float sourceX, float sourceY, float targetX, float targetY) =>
        VanillaWorldCanHit.HasLineOfSight(tiles, sourceX, sourceY, 1, 1, targetX, targetY, 1, 1);

    public bool SolidCollision(float x, float y, int width, int height) =>
        VanillaWorldSolidCollision.Intersects(tiles, x, y, width, height);
}
