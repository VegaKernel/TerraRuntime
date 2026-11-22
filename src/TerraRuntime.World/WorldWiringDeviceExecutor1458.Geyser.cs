namespace TerraRuntime.World;

public sealed partial class WorldWiringDeviceExecutor1458
{
    internal bool TryHitGeyserSwitch(int x, int y)
    {
        if (fireTrap is null || !TryResolveGeyser(tiles, x, y, out _, out _)) return false;
        // Source MessageBuffer relays packet59 even when the canonical mechanism is still cooling down.
        fireTrap(x, y);
        return true;
    }

    internal static bool TryResolveGeyser(WorldTileStore tiles, int x, int y, out int left, out int direction)
    {
        left = direction = 0;
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles) return false;
        WorldTile contact = tiles.Get(x, y);
        if (!contact.IsActive || contact.Type != GeyserTileType || contact.FrameX < 0 || contact.FrameX % FramePitch != 0)
            return false;

        int style = contact.FrameX / DoubleFrameWidth;
        int anchor = x - contact.FrameX % DoubleFrameWidth / FramePitch;
        if (style > 3 || anchor < 0 || anchor + 1 >= tiles.Dimensions.WidthTiles ||
            tiles.Get(anchor, y) is not { IsActive: true, Type: GeyserTileType, FrameY: 0 } first ||
            tiles.Get(anchor + 1, y) is not { IsActive: true, Type: GeyserTileType, FrameY: 0 } second ||
            first.FrameX != style * DoubleFrameWidth || second.FrameX != first.FrameX + FramePitch)
            return false;
        left = anchor;
        direction = style < 2 ? -1 : 1;
        return true;
    }
}
