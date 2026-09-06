using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// TerrariaServer 1.4.5.8 Projectile.CanExplodeTile and ShouldWallExplode world gates. This policy does not
/// decide whether TerraRuntime has a safe mutation implementation for the tile family; callers must still fail
/// closed when the authoritative mutation service does not support that family.
/// </summary>
public static class VanillaProjectileTileExplosionRules1458
{
    public static bool CanExplodeTile(
        WorldTileStore tiles,
        int x,
        int y,
        bool explodeHardmodeOres,
        bool hardMode,
        bool goodWorld,
        bool golemDowned)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            return false;

        WorldTile tile = tiles.Get(x, y);
        if (!tile.IsActive)
            return true;

        int type = tile.TileType.Value;
        if (type is 41 or 43 or 44 or 677 or 678 or 679 ||
            tile.TileType == VanillaTileIds.Containers || tile.TileType == VanillaTileIds.Containers2 ||
            tile.WallType == VanillaWallIds.UnbreakableTemple)
        {
            return false;
        }

        switch (type)
        {
            case 26:
            case 88:
            case 121:
            case 122:
            case 150:
            case 211:
            case 226:
            case 237:
            case 248:
            case 249:
            case 250:
            case 346:
            case 470:
            case 475:
            case 504:
            case 685:
            case 686:
                return false;

            case 107:
            case 108:
            case 111:
            case 221:
            case 222:
            case 223:
                return explodeHardmodeOres;

            case 37:
            case 58:
                if (!hardMode)
                    return false;
                break;

            case 77:
                if (!hardMode && y >= tiles.Dimensions.HeightTiles - 200)
                    return false;
                break;

            case 48:
            case 232:
                if (goodWorld)
                    return false;
                break;

            case 137:
                if (!golemDowned)
                {
                    int frameRow = tile.FrameY / 18;
                    if ((uint)(frameRow - 1) <= 3u)
                        return false;
                }
                break;
        }

        return true;
    }

    public static bool ShouldExplodeWalls(
        WorldTileStore tiles,
        float compareX,
        float compareY,
        int radiusTiles,
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                float dx = MathF.Abs(x - compareX / 16f);
                float dy = MathF.Abs(y - compareY / 16f);
                if (MathF.Sqrt(dx * dx + dy * dy) < radiusTiles && tiles.Get(x, y).WallType == VanillaWallIds.None)
                    return true;
            }
        }

        return false;
    }
}
