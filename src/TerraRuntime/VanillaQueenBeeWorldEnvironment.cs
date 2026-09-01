using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Source-shaped Queen Bee world facts. ZoneJungle mirrors the TerrariaServer 1.4.5.8 SceneMetrics jungle
/// window used elsewhere in the runtime: 169x124 active tiles, 140 jungle threshold and underworld exclusion.
/// </summary>
internal sealed class VanillaQueenBeeWorldEnvironment : IVanillaQueenBeeEnvironment
{
    private const int ScanWidth = 169;
    private const int ScanHeight = 124;
    private readonly WorldTileStore tiles;
    private readonly bool remixWorld;

    public VanillaQueenBeeWorldEnvironment(WorldTileStore tiles, double worldSurfaceTiles, bool remixWorld)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        if (!double.IsFinite(worldSurfaceTiles) || worldSurfaceTiles <= 0d)
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceTiles));
        WorldSurfacePixels = worldSurfaceTiles * 16d;
        WorldCenterX = tiles.Dimensions.WidthTiles * 8f;
        this.remixWorld = remixWorld;
    }

    public double WorldSurfacePixels { get; }

    public float WorldCenterX { get; }

    public bool IsPlayerInJungle(float playerCenterX, float playerCenterY)
    {
        if (!float.IsFinite(playerCenterX) || !float.IsFinite(playerCenterY))
            return false;

        WorldDimensions dimensions = tiles.Dimensions;
        int centerX = Math.Clamp((int)(playerCenterX / 16f), 0, dimensions.WidthTiles - 1);
        int centerY = Math.Clamp((int)(playerCenterY / 16f), 0, dimensions.HeightTiles - 1);
        if (centerY > dimensions.HeightTiles - 200)
            return false;

        int left = Math.Max(0, centerX - ScanWidth / 2);
        int top = Math.Max(0, centerY - ScanHeight / 2);
        int right = Math.Min(dimensions.WidthTiles, centerX + (ScanWidth + 1) / 2);
        int bottom = Math.Min(dimensions.HeightTiles, centerY + ScanHeight / 2);
        int jungle = 0;
        for (int x = left; x < right; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive)
                    continue;
                int type = tile.Type;
                if (type is 60 or 61 or 62 or 74 or 225 || (!remixWorld && type == 226))
                {
                    jungle++;
                    if (jungle >= 140)
                        return true;
                }
            }
        }
        return false;
    }
}
