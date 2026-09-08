namespace TerraRuntime.World;

/// <summary>
/// WorldItem.UpdateItem/MoveInWorld ordinary gravity slice. Flat terrain only until SlopeCollision,
/// conveyors and shimmer are represented. Unknown contexts refuse the update, not approximate movement.
/// </summary>
public static class VanillaOrdinaryWorldItemMotion1458
{
    public static bool TryStep(WorldTileStore tiles, int width, int height,
        float x, float y, float vx, float vy, bool wasWet, bool wasHoney, bool wasLava,
        out float nextX, out float nextY, out float nextVx, out float nextVy,
        out bool wet, out bool honey, out bool lava)
    {
        nextX = x; nextY = y; nextVx = vx; nextVy = vy;
        wet = honey = lava = false;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(vx) || !float.IsFinite(vy) ||
            width <= 0 || height <= 0 || Math.Abs(vx) > 32 || Math.Abs(vy) > 32 ||
            x < 32 || y < 32 || x + width >= (tiles.Dimensions.WidthTiles - 2) * 16f ||
            y + height >= (tiles.Dimensions.HeightTiles - 2) * 16f) return false;
        // Includes the one-update swept body plus slope/conveyor contacts just below it.
        int left = Math.Max(1, (int)((x - 48) / 16)), top = Math.Max(1, (int)((y - 48) / 16));
        int right = Math.Min(tiles.Dimensions.WidthTiles - 2, (int)((x + width + 48) / 16));
        int bottom = Math.Min(tiles.Dimensions.HeightTiles - 2, (int)((y + height + 48) / 16));
        for (int tx = left; tx <= right; tx++)
        for (int ty = top; ty <= bottom; ty++)
        {
            WorldTile tile = tiles.Get(tx, ty);
            if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Shimmer) return false;
            // Until item-specific ignored platforms and conveyor/slope motion are represented, refuse them.
            if (tile.IsActive && !tile.IsActuated && (tile.Shape != 0 || tile.Type is 421 or 422 ||
                VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))) return false;
        }
        float wetMultiplier = wasHoney ? .25f : .5f;
        float wetX = vx * wetMultiplier, wetY = vy * wetMultiplier;
        float gravity = wasHoney ? .05f : wasWet ? .08f : .1f;
        float cap = wasHoney ? 3f : wasWet ? 5f : 7f;
        nextVy = Math.Min(vy + gravity, cap);
        nextVx = vx * .95f;
        if ((double)nextVx > -.1 && (double)nextVx < .1) nextVx = 0;
        var contacts = VanillaWorldCollision.GetLiquidContacts(tiles, x, y, width, height);
        wet = contacts.Wet;
        honey = wet && (wasHoney || contacts.Honey);
        lava = wet && (wasLava || VanillaWorldCollision.LavaCollision(tiles, x, y, width, height));
        var collision = VanillaWorldCollision.TileCollision(tiles, x, y, nextVx, nextVy, width, height, false, false);
        if (collision.VelocityX != nextVx) wetX = collision.VelocityX;
        if (collision.VelocityY != nextVy) wetY = collision.VelocityY;
        nextVx = collision.VelocityX; nextVy = collision.VelocityY;
        nextX = x + (wet ? wetX : nextVx);
        nextY = y + (wet ? wetY : nextVy);
        return true;
    }
}
