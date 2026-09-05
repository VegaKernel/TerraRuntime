using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>TerrariaServer 1.4.5.8 AI_006 solid/deep-liquid overlap query.</summary>
internal sealed class VanillaWormWorldEnvironment : IVanillaWormEnvironment
{
    private const int TileSize = 16;
    private const byte SwimmableLiquidThreshold = 64;

    private readonly WorldTileStore tiles;

    public VanillaWormWorldEnvironment(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool IsDigging(float positionX, float positionY, int width, int height)
    {
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        int left = (int)(positionX / TileSize) - 1;
        int rightExclusive = (int)((positionX + width) / TileSize) + 2;
        int top = (int)(positionY / TileSize) - 1;
        int bottomExclusive = (int)((positionY + height) / TileSize) + 2;
        int startX = Math.Max(0, left);
        int endX = Math.Min(tiles.Dimensions.WidthTiles, rightExclusive);
        int startY = Math.Max(0, top);
        int endY = Math.Min(tiles.Dimensions.HeightTiles, bottomExclusive);

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                bool active = tile.IsActive && !tile.IsActuated;
                bool diggableTile = active &&
                    (VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                     (VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) &&
                      !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type)));
                if (!diggableTile && tile.LiquidAmount <= SwimmableLiquidThreshold)
                    continue;

                float tileX = x * TileSize;
                float tileY = y * TileSize;
                if (positionX + width > tileX &&
                    positionX < tileX + TileSize &&
                    positionY + height > tileY &&
                    positionY < tileY + TileSize)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
