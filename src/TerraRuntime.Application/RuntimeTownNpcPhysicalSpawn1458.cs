using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Final TerrariaServer 1.4.5.8 SpawnTownNPC physical bottom-tile choice. Vanilla always materializes the NPC after
/// this search even when no player-safe point was found, so SafeFromPlayers is diagnostic rather than a spawn gate.
/// </summary>
internal readonly record struct RuntimeTownNpcPhysicalSpawn1458(
    int TileX,
    int TileY,
    int DirectionX,
    bool SafeFromPlayers,
    bool UsedFallbackSearch)
{
    public bool IsValid => DirectionX is >= -1 and <= 1;
}

/// <summary>
/// Clean-room port of the physical placement slice in TerrariaServer 1.4.5.8 WorldGen.SpawnTownNPC. The home tile is
/// tried first against the pinned screen-sized player safety rectangle. Surface homes that are unsafe then search up
/// to 499 tiles, right before left, top-to-bottom inside each column, using Main.tileSolid floor and Collision.SolidTiles
/// three-tile clearance semantics. If the search exhausts, vanilla still spawns at the final scanned coordinates.
/// </summary>
internal sealed class RuntimeTownNpcPhysicalSpawnResolver1458
{
    internal const int ScreenWidth1458 = 1920;
    internal const int ScreenHeight1458 = 1200;
    internal const int SafeRangeX1458 = 62;
    internal const int SafeRangeY1458 = 39;
    internal const int MaximumSearchRadius1458 = 499;
    internal const int WorldEdgeMargin1458 = 10;

    private readonly WorldTileStore tiles;

    public RuntimeTownNpcPhysicalSpawnResolver1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public RuntimeTownNpcPhysicalSpawn1458 Resolve(
        int homeTileX,
        int homeTileY,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players)
    {
        WorldDimensions dimensions = tiles.Dimensions;
        if ((uint)homeTileX >= (uint)dimensions.WidthTiles ||
            (uint)homeTileY >= (uint)dimensions.HeightTiles)
        {
            throw new ArgumentOutOfRangeException(nameof(homeTileX), "Town NPC home tile is outside the current world.");
        }

        int spawnTileX = homeTileX;
        int spawnTileY = homeTileY;
        bool safeFromPlayers = IsSafeSpawnTile(spawnTileX, spawnTileY, players);
        bool usedFallbackSearch = false;
        double worldSurface = tiles.WorldSurfaceTiles ?? Math.Max(1d, dimensions.HeightTiles / 3d);

        if (!safeFromPlayers && !((double)spawnTileY > worldSurface))
        {
            usedFallbackSearch = true;
            for (int radius = 1; radius < 500; radius++)
            {
                for (int side = 0; side < 2; side++)
                {
                    spawnTileX = side != 0 ? homeTileX - radius : homeTileX + radius;
                    if (spawnTileX > WorldEdgeMargin1458 && spawnTileX < dimensions.WidthTiles - WorldEdgeMargin1458)
                    {
                        int startY = homeTileY - radius;
                        double endY = homeTileY + radius;
                        if (startY < WorldEdgeMargin1458)
                            startY = WorldEdgeMargin1458;
                        if (endY > worldSurface)
                            endY = worldSurface;

                        for (int y = startY; (double)y < endY; y++)
                        {
                            spawnTileY = y;
                            if (!IsActiveSolidFloor(spawnTileX, spawnTileY))
                                continue;

                            if (HasSolidTiles(spawnTileX - 1, spawnTileX + 1, spawnTileY - 3, spawnTileY - 1))
                                break;

                            safeFromPlayers = true;
                            if (!IsSafeSpawnTile(spawnTileX, spawnTileY, players))
                                safeFromPlayers = false;
                            break;
                        }
                    }

                    if (safeFromPlayers)
                        break;
                }

                if (safeFromPlayers)
                    break;
            }
        }

        int directionX = spawnTileX < homeTileX ? 1 : spawnTileX > homeTileX ? -1 : 0;
        return new RuntimeTownNpcPhysicalSpawn1458(
            spawnTileX,
            spawnTileY,
            directionX,
            safeFromPlayers,
            usedFallbackSearch);
    }

    internal bool IsSafeSpawnTile(
        int tileX,
        int tileY,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players)
    {
        int left = tileX * 16 + 8 - ScreenWidth1458 / 2 - SafeRangeX1458;
        int top = tileY * 16 + 8 - ScreenHeight1458 / 2 - SafeRangeY1458;
        int width = ScreenWidth1458 + SafeRangeX1458 * 2;
        int height = ScreenHeight1458 + SafeRangeY1458 * 2;
        int right = left + width;
        int bottom = top + height;

        foreach (RuntimeTownPlayerBounds1458 player in players)
        {
            if (!float.IsFinite(player.X) ||
                !float.IsFinite(player.Y) ||
                !float.IsFinite(player.Width) ||
                !float.IsFinite(player.Height) ||
                player.Width <= 0f ||
                player.Height <= 0f)
            {
                return false;
            }

            int playerLeft = (int)player.X;
            int playerTop = (int)player.Y;
            int playerRight = playerLeft + (int)player.Width;
            int playerBottom = playerTop + (int)player.Height;
            if (playerLeft < right && playerRight > left && playerTop < bottom && playerBottom > top)
                return false;
        }

        return true;
    }

    private bool IsActiveSolidFloor(int x, int y)
    {
        WorldTile tile = tiles.Get(x, y);
        return tile.IsActive &&
               !tile.IsActuated &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    }

    private bool HasSolidTiles(int startX, int endX, int startY, int endY)
    {
        WorldDimensions dimensions = tiles.Dimensions;
        if (startX < 0 || endX >= dimensions.WidthTiles || startY < 0 || endY >= dimensions.HeightTiles - 40)
            return true;

        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (tile.IsActive &&
                    !tile.IsActuated &&
                    VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                    !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
