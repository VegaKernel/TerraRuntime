namespace TerraRuntime.World;

/// <summary>
/// Clean-room port of TerrariaServer 1.4.5.8 ShimmerHelper.FindSpotWithoutShimmer plus the AI_007 town-NPC
/// search ordering. The returned coordinates are NPC top-left world pixels, matching NPC.position.
/// </summary>
public static class VanillaWorldShimmerLanding1458
{
    private const int TileSize = 16;
    private const int NearSearchExclusive = 30;
    private const int FarSearchExclusive = 60;
    private const int GroundProbeHeight = 100;

    public static bool TryFind(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height,
        bool homeless,
        int homeTileX,
        int homeTileY,
        out float landingX,
        out float landingY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        landingX = default;
        landingY = default;
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return false;

        int topTileX = (int)((positionX + width * 0.5f) / TileSize);
        int topTileY = (int)(positionY / TileSize);
        bool allowSolidTop = homeless && (homeTileX == -1 || homeTileY == -1);

        for (int expand = 1; expand < NearSearchExclusive; expand += 2)
        {
            if (TryRing(tiles, topTileX, topTileY, expand, allowSolidTop, width, height, out landingX, out landingY))
                return true;
        }

        if (homeTileX != -1 && homeTileY != -1)
        {
            for (int expand = 1; expand < NearSearchExclusive; expand += 2)
            {
                if (TryRing(tiles, homeTileX, homeTileY, expand, allowSolidTop, width, height, out landingX, out landingY))
                    return true;
            }
        }

        int farStart = allowSolidTop ? 30 : 0;
        for (int expand = farStart; expand < FarSearchExclusive; expand += 2)
        {
            if (TryRing(tiles, topTileX, topTileY, expand, allowSolidTop: true, width, height, out landingX, out landingY))
                return true;
        }

        if (homeTileX != -1 && homeTileY != -1)
        {
            for (int expand = 30; expand < FarSearchExclusive; expand += 2)
            {
                if (TryRing(tiles, homeTileX, homeTileY, expand, allowSolidTop: true, width, height, out landingX, out landingY))
                    return true;
            }
        }

        return false;
    }

    private static bool TryRing(
        WorldTileStore tiles,
        int startX,
        int startY,
        int expand,
        bool allowSolidTop,
        int width,
        int height,
        out float landingX,
        out float landingY)
    {
        for (int i = 0; i < expand; i++)
        {
            if (TryCandidate(tiles, startX - i, startY - expand, allowSolidTop, width, height, out landingX, out landingY) ||
                TryCandidate(tiles, startX + i, startY - expand, allowSolidTop, width, height, out landingX, out landingY) ||
                TryCandidate(tiles, startX - i, startY + expand, allowSolidTop, width, height, out landingX, out landingY) ||
                TryCandidate(tiles, startX + i, startY + expand, allowSolidTop, width, height, out landingX, out landingY))
            {
                return true;
            }
        }

        for (int j = 0; j < expand; j++)
        {
            if (TryCandidate(tiles, startX - expand, startY - j, allowSolidTop, width, height, out landingX, out landingY) ||
                TryCandidate(tiles, startX + expand, startY - j, allowSolidTop, width, height, out landingX, out landingY) ||
                TryCandidate(tiles, startX - expand, startY + j, allowSolidTop, width, height, out landingX, out landingY) ||
                TryCandidate(tiles, startX + expand, startY + j, allowSolidTop, width, height, out landingX, out landingY))
            {
                return true;
            }
        }

        landingX = default;
        landingY = default;
        return false;
    }

    private static bool TryCandidate(
        WorldTileStore tiles,
        int tileX,
        int tileY,
        bool allowSolidTop,
        int width,
        int height,
        out float landingX,
        out float landingY)
    {
        landingX = tileX * TileSize - width * 0.5f;
        landingY = tileY * TileSize - height;
        int worldWidthPixels = checked(tiles.Dimensions.WidthTiles * TileSize);
        int worldHeightPixels = checked(tiles.Dimensions.HeightTiles * TileSize);
        if (landingX < 0f || landingY < 0f ||
            landingX + width > worldWidthPixels || landingY + height + GroundProbeHeight > worldHeightPixels)
        {
            return false;
        }

        if (VanillaWorldSolidCollision.Intersects(tiles, landingX, landingY, width, height))
            return false;
        if (!IntersectsGround(tiles, landingX, landingY + height, width, GroundProbeHeight, allowSolidTop))
            return false;

        VanillaLiquidContactState liquid = VanillaWorldCollision.GetLiquidContacts(
            tiles,
            landingX,
            landingY,
            width,
            height + GroundProbeHeight);
        return !liquid.Shimmer;
    }

    private static bool IntersectsGround(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height,
        bool allowSolidTop)
    {
        const float tileSize = 16f;
        int minX = Math.Clamp((int)(positionX / tileSize) - 1, 0, tiles.Dimensions.WidthTiles - 1);
        int maxX = Math.Clamp((int)((positionX + width) / tileSize) + 2, 0, tiles.Dimensions.WidthTiles - 1);
        int minY = Math.Clamp((int)(positionY / tileSize) - 1, 0, Math.Max(0, tiles.Dimensions.HeightTiles - 40));
        int maxY = Math.Clamp((int)((positionY + height) / tileSize) + 2, 0, Math.Max(0, tiles.Dimensions.HeightTiles - 40));

        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || tile.IsActuated)
                    continue;
                bool solidTop = VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.FrameY == 0;
                if (!VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !(allowSolidTop && solidTop))
                    continue;
                if (solidTop && !allowSolidTop)
                    continue;

                float tileX = x * tileSize;
                float tileY = y * tileSize;
                int tileHeight = 16;
                if (tile.Shape == 1)
                {
                    tileY += 8f;
                    tileHeight = 8;
                }
                if (positionX + width > tileX && positionX < tileX + tileSize &&
                    positionY + height > tileY && positionY < tileY + tileHeight)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
