namespace TerraRuntime.World;

public sealed partial class WorldWiringCircuit1458
{
    internal bool CanPulseWithoutActorChanges(int x, int y)
    {
        if (_running || !Contains(x, y)) return false;

        // Conservatively follow all wire colors together. The exact per-color pulse stays in HitSwitch.
        // This proof admits small circuits whose only devices are plates, lights and projectile traps.
        Span<(int X, int Y)> pending = stackalloc (int, int)[128];
        pending[0] = (x, y);
        int count = 1;
        for (int read = 0; read < count; read++)
        {
            var cell = pending[read];
            var tile = _tiles.Get(cell.X, cell.Y);
            if (!WorldWiringDeviceExecutor1458.CanPulseDuringBoulderContact(in tile)) return false;
            for (int direction = 0; direction < 4; direction++)
            {
                var next = direction switch
                {
                    0 => (X: cell.X - 1, cell.Y),
                    1 => (X: cell.X + 1, cell.Y),
                    2 => (cell.X, Y: cell.Y - 1),
                    _ => (cell.X, Y: cell.Y + 1)
                };
                if (!Contains(next.X, next.Y) ||
                    (tile.Flags & _tiles.Get(next.X, next.Y).Flags & WorldTileFlagMasks.Wires) == 0 ||
                    pending[..count].Contains(next)) continue;
                if (count == pending.Length) return false;
                pending[count++] = next;
            }
        }
        return true;
    }
}
