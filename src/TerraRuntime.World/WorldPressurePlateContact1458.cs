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
/// Pure contact admission for ordinary type135 plates. The caller owns actor movement, pulse execution
/// and the source style7 destruction. Weighted plates, mines, geysers and projectile pads need other owners.
/// </summary>
public static class WorldPressurePlateContacts1458
{
    // Collision.SwitchTiles 1.4.5.8: type135, each actor/style frame is eighteen pixels apart.
    private const ushort OrdinaryPlateTileType = 135;
    private const int StyleFrameHeight = 18;
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
        out WorldPressurePlateContact1458 contact)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        contact = default;
        if (width <= 0 || height <= 0 || !float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(oldX) || !float.IsFinite(oldY))
            return false;

        // Source Collision.SwitchTiles scans X then Y, excluding the world's bottom forty rows.
        int left = ScanEdge(x, -1, tiles.Dimensions.WidthTiles);
        int right = ScanEdge(x + width, 2, tiles.Dimensions.WidthTiles);
        int top = ScanEdge(y, -1, tiles.Dimensions.HeightTiles);
        int bottom = ScanEdge(y + height, 2, Math.Max(0, tiles.Dimensions.HeightTiles - 40));
        for (int tileX = left; tileX < right; tileX++)
        {
            for (int tileY = top; tileY < bottom; tileY++)
            {
                WorldTile tile = tiles.Get(tileX, tileY);
                if (!tile.IsActive || tile.Type != OrdinaryPlateTileType || !IsEligible(tile.FrameY, actor))
                    continue;
                float plateX = tileX * 16;
                float plateY = tileY * 16 + 12;
                // The asymmetric old/new vertical edges (16.01 versus 4.01) prevent stationary retriggers.
                if (x + width > plateX && x < plateX + 16f && y + height > plateY &&
                    (double)y < (double)plateY + 4.01 &&
                    !(oldX + width > plateX && oldX < plateX + 16f && oldY + height > plateY &&
                        (double)oldY < (double)plateY + 16.01))
                {
                    contact = new(tileX, tileY, tile.FrameY / StyleFrameHeight == 7);
                    return true;
                }
            }
        }
        return false;
    }

    // Clamp before converting to int so finite outlying actor positions cannot overflow scan bounds.
    private static int ScanEdge(float pixels, int padding, int maximum) =>
        (int)Math.Clamp(Math.Truncate(pixels / 16f) + padding, 0, maximum);
}
