using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// WorldTileStore-backed queries for TerrariaServer 1.4.5.8 Deerclops AI_123. ZoneSnow uses the same
/// 169x124 SceneMetrics window and 1500/300 normal/Skyblock threshold as the pinned server.
/// </summary>
internal sealed class VanillaDeerclopsWorldEnvironment : IVanillaDeerclopsEnvironment
{
    private const int ScanWidth = 169;
    private const int ScanHeight = 124;
    private readonly WorldTileStore tiles;
    private readonly int snowThreshold;

    public VanillaDeerclopsWorldEnvironment(WorldTileStore tiles, bool skyblockLowTiles)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        snowThreshold = skyblockLowTiles
            ? VanillaSkyblockRuntimePolicy1458.LowTileBiomeThreshold
            : VanillaSkyblockRuntimePolicy1458.DefaultBiomeThreshold;
    }

    public int WorldHeightTiles => tiles.Dimensions.HeightTiles;

    public bool IsPlayerInSnow(float playerCenterX, float playerCenterY)
    {
        if (!float.IsFinite(playerCenterX) || !float.IsFinite(playerCenterY))
            return false;

        WorldDimensions dimensions = tiles.Dimensions;
        int centerX = Math.Clamp((int)(playerCenterX / 16f), 0, dimensions.WidthTiles - 1);
        int centerY = Math.Clamp((int)(playerCenterY / 16f), 0, dimensions.HeightTiles - 1);
        int left = Math.Max(0, centerX - ScanWidth / 2);
        int top = Math.Max(0, centerY - ScanHeight / 2);
        int right = Math.Min(dimensions.WidthTiles, centerX + (ScanWidth + 1) / 2);
        int bottom = Math.Min(dimensions.HeightTiles, centerY + ScanHeight / 2);
        int snow = 0;
        for (int x = left; x < right; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || tile.IsActuated)
                    continue;
                if (tile.Type is 147 or 148 or 161 or 162 or 163 or 164 or 200)
                {
                    snow++;
                    if (snow >= snowThreshold)
                        return true;
                }
            }
        }
        return false;
    }

    public bool IsWalkableTile(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)tileY >= (uint)tiles.Dimensions.HeightTiles)
        {
            return false;
        }

        WorldTile tile = tiles.Get(tileX, tileY);
        return tile.IsActive &&
               !tile.IsActuated &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    }

    public bool IsSolidTile(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)tileY >= (uint)tiles.Dimensions.HeightTiles)
        {
            return false;
        }

        WorldTile tile = tiles.Get(tileX, tileY);
        return tile.IsActive &&
               !tile.IsActuated &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    public bool SolidCollision(
        float positionX,
        float positionY,
        int width,
        int height,
        bool acceptTopSurfaces)
    {
        if (!acceptTopSurfaces)
            return VanillaWorldSolidCollision.Intersects(tiles, positionX, positionY, width, height);

        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) || width <= 0 || height <= 0)
            return false;

        int maxTileX = tiles.Dimensions.WidthTiles - 1;
        int maxTileY = Math.Max(0, tiles.Dimensions.HeightTiles - 40);
        int minX = Math.Clamp((int)(positionX / 16f) - 1, 0, maxTileX);
        int maxX = Math.Clamp((int)((positionX + width) / 16f) + 2, 0, maxTileX);
        int minY = Math.Clamp((int)(positionY / 16f) - 1, 0, maxTileY);
        int maxY = Math.Clamp((int)((positionY + height) / 16f) + 2, 0, maxTileY);
        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || tile.IsActuated)
                    continue;
                bool fullSolid = VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                                 !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
                bool solidTop = VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.FrameY == 0;
                if (!fullSolid && !solidTop)
                    continue;

                float tileX = x * 16f;
                float tileY = y * 16f;
                int tileHeight = 16;
                if (tile.Shape == 1)
                {
                    tileY += 8f;
                    tileHeight = 8;
                }

                if (positionX + width > tileX &&
                    positionX < tileX + 16f &&
                    positionY + height > tileY &&
                    positionY < tileY + tileHeight)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
