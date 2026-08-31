using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.World;
public static class VanillaWorldUnbreakableWallScan
{
    public const int ScanDistance = 250;
    public const ushort UnbreakableWallType = 350;
    public const byte MinimumWallColor = 16;
    private const int RequiredConsecutiveDirectionCount = 5;
    private const int DirectionRingSize = 8;
    private const int RequiredDirectionMask = (1 << RequiredConsecutiveDirectionCount) - 1;
    private const int DirectionRingMask = (1 << DirectionRingSize) - 1;
    private const int DirectionRingLastBitShift = DirectionRingSize - 1;
    private static readonly (int Dx, int Dy)[] Directions =
    [
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (-1, -1),
        (0, -1),
        (1, -1)
    ];
    public static bool IsInsideUnbreakableWalls(WorldTileStore tiles, float centerX, float centerY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY))
            return false;
        int tileX = (int)MathF.Floor(centerX / 16f);
        int tileY = (int)MathF.Floor(centerY / 16f);
        int hitMask = 0;
        for (int i = 0; i < Directions.Length; i++)
        {
            if (LineScan(tiles, tileX, tileY, Directions[i].Dx, Directions[i].Dy))
                hitMask |= 1 << i;
        }
        int mask = hitMask;
        for (int shift = 0; shift < Directions.Length; shift++)
        {
            if ((mask & RequiredDirectionMask) == 0)
                return false;
            mask = ((mask << 1) & DirectionRingMask) | (mask >> DirectionRingLastBitShift);
        }
        return true;
    }
    public static bool IsInsideUnbreakableWalls(WorldTileStore tiles, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles || (uint)tileY >= (uint)tiles.Dimensions.HeightTiles)
            return false;
        return IsInsideUnbreakableWalls(tiles, tileX * 16f + 8f, tileY * 16f + 8f);
    }
    private static bool LineScan(WorldTileStore tiles, int startX, int startY, int dx, int dy)
    {
        int x = startX;
        int y = startY;
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        for (int step = 0; step < ScanDistance; step++)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                return false;
            WorldTile tile = tiles.Get(x, y);
            if (tile.Wall == UnbreakableWallType && tile.WallColor >= MinimumWallColor)
                return true;
            x += dx;
            y += dy;
        }
        return false;
    }
}
