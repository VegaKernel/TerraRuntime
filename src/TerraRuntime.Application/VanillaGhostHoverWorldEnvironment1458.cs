using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed class VanillaGhostHoverWorldEnvironment1458(WorldTileStore tiles) : IVanillaGhostHoverEnvironment1458
{
    public bool TryHasObstacle(int x, int y, out bool obstacle)
    {
        obstacle = false;
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            return false;
        var tile = tiles.Get(x, y);
        obstacle = (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType)) ||
            tile.LiquidAmount > 0;
        return true;
    }

    public bool CanHit(float sourceX, float sourceY, int sourceWidth, int sourceHeight,
        float targetX, float targetY, int targetWidth, int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(tiles, sourceX, sourceY, sourceWidth, sourceHeight,
            targetX, targetY, targetWidth, targetHeight);
}
