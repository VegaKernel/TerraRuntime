namespace TerraRuntime.World;

/// <summary>Actor ids passed by TerrariaServer 1.4.5.8 Collision.SwitchTiles.</summary>
public enum WorldPressurePlateActor1458 : byte
{
    Other = 1,
    Npc = 2,
    Projectile = 3,
    ProjectileOrBoulder = 4,
    Player = 5
}

public readonly record struct WorldPressurePlateContact1458(int X, int Y, bool DestroyAfterPulse);

/// <summary>
/// Contact admission for ordinary and projectile pressure plates. Callers own movement and pulses.
/// Weighted plates and mines have separate source behavior.
/// </summary>
public static class WorldPressurePlateContacts1458
{
    // Collision.SwitchTiles 1.4.5.8: type135, each actor/style frame is eighteen pixels apart.
    private const ushort OrdinaryPlateTileType = 135;
    private const ushort ProjectilePlateTileType = 442;
    private const ushort LandMineTileType = 210;
    private const int StyleFrameHeight = 18;

    internal static bool IsPlate(ushort type) => type is OrdinaryPlateTileType or ProjectilePlateTileType;

    internal static bool HasLandMineInContactScan(WorldTileStore tiles, float x, float y, int width, int height)
    {
        var (left, right, top, bottom) = GetScanBounds(tiles, x, y, width, height);
        for (int tileX = left; tileX < right; tileX++)
            for (int tileY = top; tileY < bottom; tileY++)
                if (tiles.Get(tileX, tileY) is { IsActive: true, Type: LandMineTileType }) return true;
        return false;
    }
    public static bool IsEligible(short frameY, WorldPressurePlateActor1458 actor)
    {
        if (actor is < WorldPressurePlateActor1458.Other or > WorldPressurePlateActor1458.Player)
            return false;
        int style = frameY / StyleFrameHeight;
        return style switch
        {
            2 or 3 or 4 or 6 or 7 => actor == WorldPressurePlateActor1458.Player,
            5 => actor is WorldPressurePlateActor1458.Npc or WorldPressurePlateActor1458.Projectile,
            _ => true
        };
    }

    public static bool TryFindContact(
        WorldTileStore tiles,
        float x, float y, int width, int height,
        float oldX, float oldY,
        WorldPressurePlateActor1458 actor,
        out WorldPressurePlateContact1458 contact) =>
        TryFindContact(tiles, x, y, width, height, oldX, oldY, actor, out contact, null);

    internal static bool TryFindContact(WorldTileStore tiles, float x, float y, int width, int height,
        float oldX, float oldY, WorldPressurePlateActor1458 actor, out WorldPressurePlateContact1458 contact,
        Action<int, int>? geyserContact)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        contact = default;
        if (width <= 0 || height <= 0 || !float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(oldX) || !float.IsFinite(oldY))
            return false;

        var (left, right, top, bottom) = GetScanBounds(tiles, x, y, width, height);
        for (int tileX = left; tileX < right; tileX++)
        {
            for (int tileY = top; tileY < bottom; tileY++)
            {
                WorldTile tile = tiles.Get(tileX, tileY);
                if (!tile.IsActive) continue;
                if (tile.Type == ProjectilePlateTileType && actor == WorldPressurePlateActor1458.ProjectileOrBoulder &&
                    TouchesProjectilePad(tileX, tileY, tile.FrameX, x, y, width, height, oldX, oldY))
                {
                    contact = new(tileX, tileY, false);
                    return true;
                }
                bool geyser = tile.Type == WorldWiringDeviceExecutor1458.GeyserTileType;
                if (geyser ? geyserContact is null || actor is not (WorldPressurePlateActor1458.Other or WorldPressurePlateActor1458.Player)
                    : tile.Type != OrdinaryPlateTileType || !IsEligible(tile.FrameY, actor)) continue;
                float plateX = tileX * 16;
                float plateY = tileY * 16 + 12;
                // The asymmetric old/new vertical edges (16.01 versus 4.01) prevent stationary retriggers.
                if (x + width > plateX && x < plateX + 16f && y + height > plateY &&
                    (double)y < (double)plateY + 4.01 &&
                    !(oldX + width > plateX && oldX < plateX + 16f && oldY + height > plateY &&
                        (double)oldY < (double)plateY + 16.01))
                {
                    if (geyser)
                    {
                        geyserContact!(tileX, tileY);
                        continue;
                    }
                    contact = new(tileX, tileY, tile.FrameY / StyleFrameHeight == 7);
                    return true;
                }
            }
        }
        return false;
    }

    // Source Collision.SwitchTiles scans X then Y, excluding the world's bottom forty rows.
    private static (int Left, int Right, int Top, int Bottom) GetScanBounds(
        WorldTileStore tiles, float x, float y, int width, int height) =>
        (ScanEdge(x, -1, tiles.Dimensions.WidthTiles), ScanEdge(x + width, 2, tiles.Dimensions.WidthTiles),
            ScanEdge(y, -1, tiles.Dimensions.HeightTiles), ScanEdge(y + height, 2, Math.Max(0, tiles.Dimensions.HeightTiles - 40)));

    private static bool TouchesProjectilePad(int tileX, int tileY, short frameX,
        float x, float y, int width, int height, float oldX, float oldY)
    {
        int orientation = frameX / 22;
        if ((uint)orientation > 3) return false;
        float left = tileX * 16 + (orientation == 3 ? 6 : 0);
        float top = tileY * 16 + (orientation == 0 ? 6 : 0);
        float right = left + (orientation < 2 ? 16 : 10);
        float bottom = top + (orientation < 2 ? 10 : 16);
        // FloatIntersect accepts touching edges for type442.
        bool Intersects(float bodyX, float bodyY) =>
            bodyX <= right && bodyX + width >= left && bodyY <= bottom && bodyY + height >= top;
        return Intersects(x, y) && !Intersects(oldX, oldY);
    }

    // Clamp before converting to int so finite outlying actor positions cannot overflow scan bounds.
    private static int ScanEdge(float pixels, int padding, int maximum) =>
        (int)Math.Clamp(Math.Truncate(pixels / 16f) + padding, 0, maximum);
}
