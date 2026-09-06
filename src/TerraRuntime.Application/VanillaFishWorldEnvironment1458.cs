using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Production tile/LOS projection for TerrariaServer 1.4.5.8 ordinary AI_016.</summary>
internal sealed class VanillaFishWorldEnvironment1458 : IVanillaFishEnvironment1458
{
    private const int TileSize = 16;
    private readonly WorldTileStore tiles;

    public VanillaFishWorldEnvironment1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourcePositionX,
            sourcePositionY,
            sourceWidth,
            sourceHeight,
            targetPositionX,
            targetPositionY,
            targetWidth,
            targetHeight);

    public VanillaFishBottomSlope1458 GetBottomSlope(
        float positionX,
        float positionY,
        int width,
        int height)
    {
        if (!HasValidGeometry(positionX, positionY, width, height))
            return VanillaFishBottomSlope1458.None;

        int x = (int)(positionX + width * 0.5f) / TileSize;
        int y = (int)(positionY + height) / TileSize;
        if (TryGet(x, y, out WorldTile tile) && TryResolveTopSlope(in tile, out VanillaFishBottomSlope1458 slope))
            return slope;
        if (TryGet(x, y + 1, out tile) && TryResolveTopSlope(in tile, out slope))
            return slope;
        return VanillaFishBottomSlope1458.None;
    }

    public bool HasDeepLiquidAboveAndActiveTileBelow(
        float positionX,
        float positionY,
        int width,
        int height)
    {
        if (!HasValidGeometry(positionX, positionY, width, height))
            return false;

        int x = (int)(positionX + width * 0.5f) / TileSize;
        int y = (int)(positionY + height * 0.5f) / TileSize;
        return TryGet(x, y - 1, out WorldTile above) &&
               above.LiquidAmount > 128 &&
               ((TryGet(x, y + 1, out WorldTile below) && below.IsActive) ||
                (TryGet(x, y + 2, out below) && below.IsActive));
    }

    public bool CanHitLineStraightAbove(
        float positionX,
        float positionY,
        int width,
        int height,
        float distance)
    {
        if (!HasValidGeometry(positionX, positionY, width, height) ||
            !float.IsFinite(distance) ||
            distance < 0f)
        {
            return false;
        }

        float centerX = positionX + width * 0.5f;
        float centerY = positionY + height * 0.5f;
        return VanillaWorldLineOfSight.CanHitLine(
            tiles,
            centerX,
            centerY,
            centerX,
            centerY - distance);
    }

    public bool TryGetWaterLineAtTop(
        float positionX,
        float positionY,
        int width,
        out float waterLineHeight)
    {
        waterLineHeight = 0f;
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) || width <= 0)
            return false;

        int x = (int)((positionX + width * 0.5f) / TileSize);
        int y = (int)(positionY / TileSize);
        if (x < 10 || y < 10 ||
            x >= tiles.Dimensions.WidthTiles - 10 ||
            y >= tiles.Dimensions.HeightTiles - 10)
        {
            return false;
        }

        WorldTile upper = tiles.Get(x, y - 2);
        if (upper.LiquidAmount > 0)
            return false;

        WorldTile candidate = tiles.Get(x, y - 1);
        if (candidate.LiquidAmount > 0)
        {
            waterLineHeight = y * TileSize - candidate.LiquidAmount / 16;
            return true;
        }

        candidate = tiles.Get(x, y);
        if (candidate.LiquidAmount > 0)
        {
            waterLineHeight = (y + 1) * TileSize - candidate.LiquidAmount / 16;
            return true;
        }

        candidate = tiles.Get(x, y + 1);
        if (candidate.LiquidAmount <= 0)
            return false;

        waterLineHeight = (y + 2) * TileSize - candidate.LiquidAmount / 16;
        return true;
    }

    private static bool TryResolveTopSlope(
        in WorldTile tile,
        out VanillaFishBottomSlope1458 slope)
    {
        // Runtime Shape is vanilla slope + 1: top slopes 1/2 become 2/3, and slope 2 is left-facing.
        slope = tile.Shape switch
        {
            3 => VanillaFishBottomSlope1458.FaceLeft,
            2 => VanillaFishBottomSlope1458.FaceRight,
            _ => VanillaFishBottomSlope1458.None
        };
        return slope != VanillaFishBottomSlope1458.None;
    }

    private bool TryGet(int x, int y, out WorldTile tile)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)y >= (uint)tiles.Dimensions.HeightTiles)
        {
            tile = default;
            return false;
        }

        tile = tiles.Get(x, y);
        return true;
    }

    private static bool HasValidGeometry(float x, float y, int width, int height) =>
        float.IsFinite(x) && float.IsFinite(y) && width > 0 && height > 0;
}
