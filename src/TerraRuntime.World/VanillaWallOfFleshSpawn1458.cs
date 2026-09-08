using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.World;

/// <summary>NPC.SpawnWOF placement, distinct from SpawnOnPlayer. No NPC/world mutation.</summary>
public static class VanillaWallOfFleshSpawn1458
{
    public static bool TryFind(WorldTileStore tiles, float x, float y,
        ReadOnlySpan<PlayerStateSnapshot> players, out int bottomX, out int bottomY)
    {
        bottomX = bottomY = 0;
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x >= width * 16f ||
            y / 16f < height - 205 || y >= height * 16f || width < 42 || height < 210)
            return false;
        int direction = x / 16f > width / 2 ? -1 : 1;
        if (players.Length > 255) return false;
        int previousSlot = -1;
        foreach (PlayerStateSnapshot player in players)
        {
            if (!player.Player.IsAssigned || player.Player.Slot.Value <= previousSlot ||
                player.Player.Slot.Value == 255 || !float.IsFinite(player.PositionX)) return false;
            previousSlot = player.Player.Slot.Value;
        }
        int spawnX = (int)x;
        // Every move is monotonic by16; this dimension-derived bound contains the source search.
        bool done = false;
        for (int iteration = 0; iteration <= width + 1; iteration++)
        {
            bool shifted = false;
            foreach (PlayerStateSnapshot player in players)
            {
                // Source checks active only, including dead players, using top-left X.
                if (player.PositionX > spawnX - 1200f && player.PositionX < spawnX + 1200f)
                {
                    spawnX -= direction * 16;
                    shifted = true;
                }
            }
            if (!shifted || spawnX / 16 < 20 || spawnX / 16 > width - 20) { done = true; break; }
        }
        if (!done) return false;
        int tileX = spawnX / 16, tileY = (int)y / 16;
        if (!IsOpen(tiles, tileX, tileY))
        {
            for (int offset = 0; offset < 999; offset++)
            {
                if (IsOpen(tiles, tileX, tileY - offset)) { tileY -= offset; break; }
                if (IsOpen(tiles, tileX, tileY + offset)) { tileY += offset; break; }
            }
        }
        bottomX = spawnX;
        bottomY = Math.Clamp(tileY, height - 190, height - 120) * 16;
        return true;
    }

    private static bool IsOpen(WorldTileStore tiles, int x, int y)
    {
        if (x < 2 || y < 2 || x >= tiles.Dimensions.WidthTiles - 2 || y >= tiles.Dimensions.HeightTiles - 2)
            return false;
        WorldTile tile = tiles.Get(x, y);
        // WorldGen.SolidTile excludes platforms, slopes and half bricks.
        bool solid = tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        return !solid && tile.LiquidAmount < 100;
    }
}
