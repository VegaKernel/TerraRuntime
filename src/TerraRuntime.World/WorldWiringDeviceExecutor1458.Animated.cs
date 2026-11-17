namespace TerraRuntime.World;

public sealed partial class WorldWiringDeviceExecutor1458
{
    private bool ToggleAnimatedDevice(int x, int y, in WorldTile tile, Action<int, int> skipWire)
    {
        byte width = tile.Type == SmallVolcano ? (byte)1 : (byte)2;
        byte height = tile.Type == MushroomStatue ? (byte)3 : width;
        int stride = tile.Type == MushroomStatue ? 216 : width * FramePitch;
        if (tile.FrameX % FramePitch != 0 || tile.FrameY % FramePitch != 0 ||
            tile.FrameX >= stride + width * FramePitch || tile.FrameY >= height * FramePitch) return false;
        int left = x - tile.FrameX % (width * FramePitch) / FramePitch;
        int top = y - tile.FrameY / FramePitch;
        int before = tile.FrameX >= stride ? stride : 0;
        int after = before == 0 ? stride : 0;
        short animation = tile.Type == MushroomStatue ? after == 0 ? (short)1 : (short)0 :
            after == 0 ? (short)3 : (short)4;
        return ChangeAnimatedFootprint(left, top, width, height, tile.Type, before, tile.Type, after,
            animation, false, tile.Type != MushroomStatue, skipWire);
    }

    // Wiring.HitWireSingle(105/349/593/594), 1.4.5.8. Animation.Update only changes presentation
    // frames read by TileDrawing; the server retains material state and emits the source animation events.
    private bool ChangeAnimatedFootprint(int left, int top, byte width, byte height, ushort beforeType,
        int beforeFrame, ushort afterType, int afterFrame, short animation, bool animationFirst,
        bool duplicateAnimation, Action<int, int> skipWire)
    {
        // Packet77 has signed16-bit coordinates. Refuse an unrepresentable custom-world location
        // before material mutation rather than silently publishing wrapped animation coordinates.
        if ((uint)left > short.MaxValue || (uint)top > short.MaxValue || !Contains(left, top) ||
            !Contains(left + width - 1, top + height - 1)) return false;
        Span<WorldTile> before = stackalloc WorldTile[6];
        Span<long> versions = stackalloc long[6];
        int count = width * height;
        for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
        {
            int index = dx * height + dy;
            var cell = tiles.Get(left + dx, top + dy);
            if (!cell.IsActive || cell.Type != beforeType || cell.HasActuator || cell.IsActuated || cell.LiquidAmount != 0 ||
                cell.FrameX != beforeFrame + dx * FramePitch || cell.FrameY != dy * FramePitch) return false;
            before[index] = cell;
            versions[index] = tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, left + dx, top + dy));
        }
        for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++) skipWire(left + dx, top + dy);
        // Skip callbacks belong to circuit bookkeeping. A substituted/reentrant callback must not
        // let a validated footprint overwrite another mutation, even after an ABA restoration.
        for (int dx = 0; dx < width; dx++) for (int dy = 0; dy < height; dy++)
        {
            int index = dx * height + dy;
            if (!tiles.Get(left + dx, top + dy).Equals(before[index]) || versions[index] !=
                tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, left + dx, top + dy))) return false;
        }
        for (int index = 0; index < count; index++)
        {
            int dx = index / height, dy = index % height;
            var cell = before[index];
            cell.Type = afterType;
            cell.FrameX = (short)(afterFrame + dx * FramePitch);
            tiles.Set(left + dx, top + dy, in cell);
        }
        if (animationFirst) publishTemporaryAnimation?.Invoke(animation, afterType, left, top);
        Publish(left, top, width, height);
        if (!animationFirst) publishTemporaryAnimation?.Invoke(animation, afterType, left, top);
        // Both volcano cases explicitly send a second77 after NewTemporaryAnimation's own send.
        if (duplicateAnimation) publishTemporaryAnimation?.Invoke(animation, afterType, left, top);
        return true;
    }
}
