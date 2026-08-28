namespace TerraRuntime.World;

/// <summary>
/// Source-backed collision facts from TerrariaServer 1.4.5.8 Main.Initialize_TileAndNPCData2.
/// The packed tables mirror Main.tileSolid and Main.tileSolidTop for TileID.Count == 754.
/// Reference TerrariaServer.exe SHA-256:
/// d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaTileCollisionCatalog
{
    public const int TileTypeCount = 754;

    private static ReadOnlySpan<ulong> SolidWords =>
    [
        0x9F61FBE042C807C7UL, 0x8FF1B8000000185FUL, 0xF0FB87DFFFDE1604UL,
        0xBF008D67E0095DFFUL, 0x0B80000010071FFFUL, 0xB80F80205E0003E6UL,
        0xC0FFCC67839FF01BUL, 0x18F5901EF7001C01UL, 0x004C20043BC0003FUL,
        0x0A1E040000000000UL, 0x001FFFFFFD680002UL, 0x00007FFFDFC4FF90UL
    ];

    private static ReadOnlySpan<ulong> SolidTopWords =>
    [
        0x00000000000D4000UL, 0x0004002001800000UL, 0x0000000000000040UL,
        0x0000800000000000UL, 0x00600F0063F80000UL, 0x11001EC000080000UL,
        0x00F8080060200780UL, 0x0000000000200000UL, 0x0000DEC144300000UL,
        0x0120081FFF800040UL, 0x0000000000000039UL, 0x0000000000000040UL
    ];

    public static bool IsSolid(ushort type) => Test(type, SolidWords);

    public static bool IsSolidTop(ushort type) => Test(type, SolidTopWords);

    private static bool Test(ushort type, ReadOnlySpan<ulong> words)
    {
        if (type >= TileTypeCount)
            return false;

        int word = type >> 6;
        int bit = type & 63;
        return (words[word] & (1UL << bit)) != 0;
    }
}

/// <summary>
/// Protocol-neutral result of the vanilla TileCollision velocity clamp.
/// </summary>
public readonly record struct VanillaTileCollisionResult(
    float VelocityX,
    float VelocityY,
    bool HitCeiling,
    bool HitFloor);

/// <summary>
/// Clean-room port of the collision pieces required by ordinary NPC movement. The broad-phase bounds,
/// full/half/slope tile checks and platform fall-through rules follow TerrariaServer 1.4.5.8
/// Collision.TileCollision. Liquid contact follows Collision.WetCollision.
/// </summary>
public static class VanillaWorldCollision
{
    private const float TileSize = 16f;

    public static VanillaTileCollisionResult TileCollision(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        bool fallThrough,
        bool fall2,
        int gravDir = 1)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (gravDir is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(gravDir));

        float resultX = velocityX;
        float resultY = velocityY;
        float destinationX = positionX + velocityX;
        float destinationY = positionY + velocityY;

        GetScanBounds(
            tiles,
            positionX,
            positionY,
            width,
            height,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY);

        int sideTileX = -1;
        int sideTileY = -1;
        int verticalTileX = -1;
        int verticalTileY = -1;
        float closestFloorY = (maxY + 3) * TileSize;
        bool hitCeiling = false;
        bool hitFloor = false;

        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!IsCollisionActive(in tile))
                    continue;

                bool solidTop = VanillaTileCollisionCatalog.IsSolidTop(tile.Type) && tile.FrameY == 0;
                bool solid = VanillaTileCollisionCatalog.IsSolid(tile.Type) || solidTop;
                if (!solid)
                    continue;

                float tileX = x * TileSize;
                float tileY = y * TileSize;
                int tileHeight = 16;
                if (tile.Shape == 1)
                {
                    tileY += 8f;
                    tileHeight = 8;
                }

                if (destinationX + width <= tileX ||
                    destinationX >= tileX + TileSize ||
                    destinationY + height <= tileY ||
                    destinationY >= tileY + tileHeight)
                {
                    continue;
                }

                int slope = GetSlope(in tile);
                bool downwardSlope = false;
                bool slopePass = false;
                if (slope > 2)
                {
                    if (slope == 3 && positionY + Math.Abs(velocityX) >= tileY && positionX >= tileX)
                        slopePass = true;
                    if (slope == 4 && positionY + Math.Abs(velocityX) >= tileY && positionX + width <= tileX + TileSize)
                        slopePass = true;
                }
                else if (slope > 0)
                {
                    downwardSlope = true;
                    if (slope == 1 && positionY + height - Math.Abs(velocityX) <= tileY + tileHeight && positionX >= tileX)
                        slopePass = true;
                    if (slope == 2 && positionY + height - Math.Abs(velocityX) <= tileY + tileHeight && positionX + width <= tileX + TileSize)
                        slopePass = true;
                }

                if (slopePass)
                    continue;

                if (positionY + height <= tileY)
                {
                    hitFloor = true;
                    if ((!(solidTop && fallThrough) || !(velocityY <= 1f || fall2)) && closestFloorY > tileY)
                    {
                        verticalTileX = x;
                        verticalTileY = y;
                        if (tileHeight < 16)
                            verticalTileY++;

                        if (verticalTileX != sideTileX && !downwardSlope)
                        {
                            resultY = tileY - (positionY + height) + (gravDir == -1 ? -0.01f : 0f);
                            closestFloorY = tileY;
                        }
                    }
                }
                else if (positionX + width <= tileX && !solidTop)
                {
                    int neighborSlope = GetSlopeOrZero(tiles, x - 1, y);
                    if (x < 1 || (neighborSlope != 2 && neighborSlope != 4))
                    {
                        sideTileX = x;
                        sideTileY = y;
                        if (sideTileY != verticalTileY)
                            resultX = tileX - (positionX + width);
                        if (verticalTileX == sideTileX)
                            resultY = velocityY;
                    }
                }
                else if (positionX >= tileX + TileSize && !solidTop)
                {
                    int neighborSlope = GetSlopeOrZero(tiles, x + 1, y);
                    if (neighborSlope != 1 && neighborSlope != 3)
                    {
                        sideTileX = x;
                        sideTileY = y;
                        if (sideTileY != verticalTileY)
                            resultX = tileX + TileSize - positionX;
                        if (verticalTileX == sideTileX)
                            resultY = velocityY;
                    }
                }
                else if (positionY >= tileY + tileHeight && !solidTop)
                {
                    hitCeiling = true;
                    verticalTileX = x;
                    verticalTileY = y;
                    resultY = tileY + tileHeight - positionY + (gravDir == 1 ? 0.01f : 0f);
                    if (verticalTileY == sideTileY)
                        resultX = velocityX;
                }
            }
        }

        return new VanillaTileCollisionResult(resultX, resultY, hitCeiling, hitFloor);
    }

    public static bool TryGetWetContact(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height,
        out WorldLiquidKind liquidKind)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        float probeWidth = Math.Min(10, width);
        float probeHeight = height / 2;
        float probeX = positionX + width / 2f - probeWidth / 2f;
        float probeY = positionY + height / 2f - probeHeight / 2f;

        GetScanBounds(
            tiles,
            positionX,
            positionY,
            width,
            height,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY);

        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (tile.LiquidAmount > 0)
                {
                    float liquidX = x * TileSize;
                    float liquidY = y * TileSize;
                    float empty = (256 - tile.LiquidAmount) / 32f;
                    liquidY += empty * 2f;
                    int liquidHeight = 16 - (int)(empty * 2f);
                    if (Intersects(probeX, probeY, probeWidth, probeHeight, liquidX, liquidY, 16f, liquidHeight))
                    {
                        liquidKind = tile.LiquidKind;
                        return true;
                    }
                }
                else if (tile.IsActive && GetSlope(in tile) != 0 && y > 0)
                {
                    WorldTile above = tiles.Get(x, y - 1);
                    if (above.LiquidAmount <= 0)
                        continue;

                    float tileX = x * TileSize;
                    float tileY = y * TileSize;
                    if (Intersects(probeX, probeY, probeWidth, probeHeight, tileX, tileY, 16f, 16f))
                    {
                        liquidKind = above.LiquidKind;
                        return true;
                    }
                }
            }
        }

        liquidKind = default;
        return false;
    }

    private static bool IsCollisionActive(in WorldTile tile) =>
        tile.IsActive && (tile.Flags & WorldTileFlags.Inactive) == 0;

    private static int GetSlope(in WorldTile tile) => tile.Shape >= 2 ? tile.Shape - 1 : 0;

    private static int GetSlopeOrZero(WorldTileStore tiles, int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            return 0;

        return GetSlope(in tiles.Get(x, y));
    }

    private static void GetScanBounds(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height,
        out int minX,
        out int maxX,
        out int minY,
        out int maxY)
    {
        int maxTileX = tiles.Dimensions.WidthTiles - 1;
        int maxTileY = Math.Max(0, tiles.Dimensions.HeightTiles - 40);
        minX = Math.Clamp((int)(positionX / TileSize) - 1, 0, maxTileX);
        maxX = Math.Clamp((int)((positionX + width) / TileSize) + 2, 0, maxTileX);
        minY = Math.Clamp((int)(positionY / TileSize) - 1, 0, maxTileY);
        maxY = Math.Clamp((int)((positionY + height) / TileSize) + 2, 0, maxTileY);
    }

    private static bool Intersects(
        float leftA,
        float topA,
        float widthA,
        float heightA,
        float leftB,
        float topB,
        float widthB,
        float heightB) =>
        leftA + widthA > leftB &&
        leftA < leftB + widthB &&
        topA + heightA > topB &&
        topA < topB + heightB;
}
