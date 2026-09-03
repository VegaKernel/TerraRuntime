using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// WorldTileStore-backed source queries for TerrariaServer 1.4.5.8 Wall of Flesh AI_027/028 and the Good-World
/// Fire Imp support branch. It exposes only collision/placement facts; presentation state remains client-owned.
/// </summary>
internal sealed class VanillaWallOfFleshWorldEnvironment : IVanillaWallOfFleshEnvironment
{
    private const int TileSize = 16;
    private readonly WorldTileStore tiles;

    public VanillaWallOfFleshWorldEnvironment(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public int WorldWidthTiles => tiles.Dimensions.WidthTiles;
    public int WorldHeightTiles => tiles.Dimensions.HeightTiles;
    public int UnderworldLayerTiles => Math.Max(0, WorldHeightTiles - 200);

    public bool TryResolveCorridor(
        float positionX,
        float positionY,
        int width,
        int height,
        out float topPixels,
        out float bottomPixels)
    {
        topPixels = 0f;
        bottomPixels = 0f;
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) || width <= 0 || height <= 0)
            return false;

        int minBand = UnderworldLayerTiles + 10;
        int maxBand = Math.Min(WorldHeightTiles - 10, minBand + 70);
        if (minBand >= maxBand)
            return false;

        int left = Math.Clamp((int)(positionX / TileSize), 2, Math.Max(2, WorldWidthTiles - 3));
        int right = Math.Clamp((int)((positionX + width) / TileSize), left, Math.Max(left, WorldWidthTiles - 3));
        int centerY = Math.Clamp((int)((positionY + height * .5f) / TileSize), minBand, maxBand);

        int lowerContacts = 0;
        int lower = centerY + 7;
        while (lowerContacts < 15 && lower < WorldHeightTiles - 10)
        {
            lower++;
            if (lower > WorldHeightTiles - 10) { lower = WorldHeightTiles - 10; break; }
            if (lower < minBand) continue;
            for (int x = left; x <= right; x++)
            {
                if (IsSolidOrLiquid(x, lower))
                    lowerContacts++;
            }
        }
        lower += 4;

        int upperContacts = 0;
        int upper = centerY - 7;
        while (upperContacts < 15 && upper < WorldHeightTiles - 10)
        {
            upper--;
            if (upper <= 10) { upper = 10; break; }
            if (upper > maxBand) continue;
            if (upper < minBand) { upper = minBand; break; }
            for (int x = left; x <= right; x++)
            {
                if (IsSolidOrLiquid(x, upper))
                    upperContacts++;
            }
        }
        upper -= 4;

        topPixels = Math.Clamp(upper * (float)TileSize, minBand * (float)TileSize, maxBand * (float)TileSize);
        bottomPixels = Math.Clamp(lower * (float)TileSize, minBand * (float)TileSize, maxBand * (float)TileSize);
        if (topPixels > bottomPixels - 160f)
            topPixels = bottomPixels - 160f;
        if (bottomPixels < topPixels + 160f)
            bottomPixels = topPixels + 160f;
        return true;
    }

    public bool CanHit(
        float sourceX,
        float sourceY,
        int sourceWidth,
        int sourceHeight,
        float targetX,
        float targetY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            targetX,
            targetY,
            targetWidth,
            targetHeight);

    public bool TryFindGroundSpawn(int tileX, int startTileY, out int bottomX, out int bottomY)
    {
        bottomX = 0;
        bottomY = 0;
        if (tileX < 2 || tileX >= WorldWidthTiles - 2)
            return false;

        int y = Math.Clamp(startTileY, 2, WorldHeightTiles - 11);
        while (y < WorldHeightTiles - 10 && !IsFullSolid(tileX, y))
            y++;
        y--;
        if (y < 2 || IsFullSolid(tileX, y) || !IsFullSolid(tileX, y + 1))
            return false;

        bottomX = tileX * TileSize + TileSize / 2;
        bottomY = (y + 1) * TileSize;
        return true;
    }

    public bool TryFindTeleportSpot(
        int targetTileX,
        int targetTileY,
        int npcWidth,
        int npcHeight,
        out int tileX,
        out int tileY)
    {
        tileX = 0;
        tileY = 0;
        int halfWidthTiles = Math.Max(1, (npcWidth + 15) / 32);
        int heightTiles = Math.Max(1, (npcHeight + 15) / 16);
        for (int radius = 8; radius <= 30; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) < radius && radius != 8)
                    continue;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int x = targetTileX + dx;
                    int startY = targetTileY + sign * radius;
                    if (!TryFindGroundSpawn(x, startY, out _, out int bottom))
                        continue;
                    int groundY = bottom / TileSize;
                    int standY = groundY - 1;
                    if (!AreaClear(x, standY, halfWidthTiles, heightTiles))
                        continue;
                    tileX = x;
                    tileY = groundY;
                    return true;
                }
            }
        }
        return false;
    }

    private bool AreaClear(int centerX, int bottomTileY, int halfWidthTiles, int heightTiles)
    {
        int left = centerX - halfWidthTiles;
        int right = centerX + halfWidthTiles;
        int top = bottomTileY - heightTiles + 1;
        if (left < 1 || right >= WorldWidthTiles - 1 || top < 1 || bottomTileY >= WorldHeightTiles - 1)
            return false;
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottomTileY; y++)
                if (IsSolidOrLiquid(x, y))
                    return false;
        return true;
    }

    private bool IsSolidOrLiquid(int x, int y)
    {
        if ((uint)x >= (uint)WorldWidthTiles || (uint)y >= (uint)WorldHeightTiles)
            return true;
        WorldTile tile = tiles.Get(x, y);
        return tile.LiquidAmount > 0 ||
               (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType));
    }

    private bool IsFullSolid(int x, int y)
    {
        if ((uint)x >= (uint)WorldWidthTiles || (uint)y >= (uint)WorldHeightTiles)
            return true;
        WorldTile tile = tiles.Get(x, y);
        return tile.IsActive && !tile.IsActuated &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }
}
