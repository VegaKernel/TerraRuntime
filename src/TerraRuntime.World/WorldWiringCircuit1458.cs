namespace TerraRuntime.World;

/// <summary>
/// Owns one world's transient wire traversal and mechanism clocks. All entry points run on the
/// authoritative thread; tile storage and its section/persistence invalidation remain in WorldTileStore.
/// </summary>
public sealed class WorldWiringCircuit1458
{
    // TerrariaServer 1.4.5.8 Wiring.HitSwitch/UpdateMech and TileObjectData132/411 frame facts.
    private const ushort TimerTile = 144;
    private const ushort MomentaryTile = 411;
    private const short SourceFramePitch = 18;
    private const short SwitchFrameWidth = 36;
    // TerrariaServer 1.4.5.8 Wiring.CheckMech admits exactly 999 simultaneous registrations.
    private const int MechanismCapacity = 999;
    private readonly WorldTileStore _tiles;
    private readonly WorldWiringDeviceExecutor1458 _devices;
    private readonly Action<int, int, byte, byte>? _publish;
    private readonly List<Mechanism> _mechanisms = new(MechanismCapacity);
    private readonly Queue<WireVisit> _pending = new();
    private readonly Dictionary<int, byte> _remainingEntrances = new();
    private readonly HashSet<int> _skip = new();
    private readonly HashSet<long> _routerEntrances = new();
    private readonly Dictionary<int, byte> _pixelTriggers = new();
    private bool _running;

    public WorldWiringCircuit1458(
        WorldTileStore tiles,
        WorldWiringDeviceExecutor1458 devices,
        Action<int, int, byte, byte>? publishTileSquare = null)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _publish = publishTileSquare;
    }

    public int RegisteredMechanismCount => _mechanisms.Count;

    /// <summary>Activates only admitted source trigger types; no client-provided output state is accepted.</summary>
    public bool HitSwitch(int x, int y) => HitSwitchCore(x, y, fromWire: false);

    private bool HitSwitchCore(int x, int y, bool fromWire)
    {
        if ((_running && !fromWire) || !Contains(x, y)) return false;
        WorldTile tile = _tiles.Get(x, y);
        if (!tile.IsActive) return false;
        switch (tile.Type)
        {
            case 135: // Eligibility for actor contact belongs to the caller, not raw packet59.
            case 314:
            case 423:
            case 428:
            case 442:
            case 476:
                return TripWire(x, y, 1, 1);
            case 136:
                tile.FrameY = tile.FrameY == 0 ? SourceFramePitch : (short)0;
                _tiles.Set(x, y, in tile);
                TripWire(x, y, 1, 1);
                return true;
            case TimerTile:
                tile.FrameY = tile.FrameY == 0 ? SourceFramePitch : (short)0;
                _tiles.Set(x, y, in tile);
                if (tile.FrameY != 0) Register(x, y, 18000);
                return true;
            case 132:
            case MomentaryTile:
            {
                int left = x - tile.FrameX / SourceFramePitch % 2;
                int top = y - tile.FrameY / SourceFramePitch;
                if (!ValidSwitchFootprint(left, top, tile.Type)) return false;
                short delta = tile.FrameX / SourceFramePitch % 4 >= 2 ? (short)-SwitchFrameWidth : SwitchFrameWidth;
                if (tile.Type == MomentaryTile) Register(left, top, 60);
                for (int column = left; column < left + 2; column++)
                for (int row = top; row < top + 2; row++)
                {
                    WorldTile cell = _tiles.Get(column, row);
                    if (cell.Type is not (132 or MomentaryTile)) continue;
                    cell.FrameX = (short)(cell.FrameX + delta);
                    _tiles.Set(column, row, in cell);
                }
                TripWire(left, top, 2, 2);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Runs a complete pulse in source red/blue/green/yellow order. Source Wiring.HitWire uses a FIFO,
    /// column-major seeds and down/up/right/left neighbors; source cells are skipped as devices.
    /// </summary>
    public bool TripWire(int left, int top, int width, int height)
    {
        if (_running || width <= 0 || height <= 0 || !Contains(left, top) ||
            (long)left + width > _tiles.Dimensions.WidthTiles ||
            (long)top + height > _tiles.Dimensions.HeightTiles) return false;
        _running = true;
        try
        {
            for (int color = 1; color <= 4; color++)
            {
                WorldWireChannel channel = (WorldWireChannel)color;
                for (int x = left; x < left + width; x++)
                for (int y = top; y < top + height; y++)
                {
                    if (!HasWire(_tiles.Get(x, y), channel)) continue;
                    int index = Index(x, y);
                    _skip.Add(index);
                    _remainingEntrances.Add(index, 4);
                    _pending.Enqueue(new WireVisit(x, y, 0));
                }
                Traverse(channel);
                _skip.Clear();
                _remainingEntrances.Clear();
                _routerEntrances.Clear();
            }
            // Wiring.PixelBoxPass runs once, after all four colors, and requires both axes.
            foreach ((int index, byte axes) in _pixelTriggers)
            {
                if (axes != 3) continue;
                int x = index / _tiles.Dimensions.HeightTiles;
                int y = index % _tiles.Dimensions.HeightTiles;
                WorldTile tile = _tiles.Get(x, y);
                tile.FrameX = tile.FrameX == SourceFramePitch ? (short)0 : SourceFramePitch;
                _tiles.Set(x, y, in tile);
                _publish?.Invoke(x, y, 1, 1);
            }
            return true;
        }
        finally
        {
            _pending.Clear();
            _remainingEntrances.Clear();
            _skip.Clear();
            _routerEntrances.Clear();
            _pixelTriggers.Clear();
            _running = false;
        }
    }

    /// <summary>Runs Wiring.UpdateMech's decrement-before-fire order and reverse registration order.</summary>
    public void Tick()
    {
        for (int i = _mechanisms.Count - 1; i >= 0; i--)
        {
            Mechanism mechanism = _mechanisms[i];
            mechanism.Time--;
            if (!ContainsMechanism(mechanism.X, mechanism.Y))
            {
                // Wiring.UpdateMech checks WorldGen.InWorld(..., 1) after decrementing. Its
                // invalid-coordinate branch truncates the registration table's final entry,
                // even when the invalid coordinate belongs to an earlier entry in that table.
                _mechanisms[i] = mechanism;
                _mechanisms.RemoveAt(_mechanisms.Count - 1);
                continue;
            }
            WorldTile tile = _tiles.Get(mechanism.X, mechanism.Y);
            if (tile.IsActive && tile.Type == TimerTile)
            {
                if (tile.FrameY == 0) mechanism.Time = 0;
                else
                {
                    int interval = (tile.FrameX / SourceFramePitch) switch
                    {
                        0 => 60,
                        1 => 180,
                        2 => 300,
                        3 => 30,
                        4 => 15,
                        int other => other
                    };
                    if (interval != 0 && mechanism.Time % interval == 0)
                    {
                        mechanism.Time = 18000;
                        // Publish the updated countdown before a pulse can toggle this same timer.
                        _mechanisms[i] = mechanism;
                        TripWire(mechanism.X, mechanism.Y, 1, 1);
                    }
                }
            }
            _mechanisms[i] = mechanism;
            if (mechanism.Time > 0) continue;
            if (tile.IsActive && tile.Type == TimerTile)
            {
                tile = _tiles.Get(mechanism.X, mechanism.Y);
                tile.FrameY = 0;
                _tiles.Set(mechanism.X, mechanism.Y, in tile);
                _publish?.Invoke(mechanism.X, mechanism.Y, 1, 1);
            }
            else if (tile.IsActive && tile.Type == MomentaryTile)
                ResetMomentary(mechanism.X, mechanism.Y);
            _mechanisms.RemoveAt(i);
        }
    }

    /// <summary>World replacement/reset drops clocks, traversal and all references to old devices.</summary>
    public void Clear()
    {
        if (_running) throw new InvalidOperationException("Cannot reset wiring during an active pulse.");
        _mechanisms.Clear();
        _pending.Clear();
        _remainingEntrances.Clear();
        _skip.Clear();
        _routerEntrances.Clear();
        _pixelTriggers.Clear();
    }

    private void Traverse(WorldWireChannel channel)
    {
        while (_pending.TryDequeue(out WireVisit visit))
        {
            int index = Index(visit.X, visit.Y);
            if (!_skip.Contains(index))
                _devices.Hit(visit.X, visit.Y, channel, SkipWire, HitSwitchFromWire);
            WorldTile current = _tiles.Get(visit.X, visit.Y);
            for (byte direction = 0; direction < 4; direction++)
            {
                if (!Routes(current, visit.Direction, direction)) continue;
                int x = visit.X + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                int y = visit.Y + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                if (x < 2 || y < 2 || x >= _tiles.Dimensions.WidthTiles - 2 ||
                    y >= _tiles.Dimensions.HeightTiles - 2) continue;
                WorldTile next = _tiles.Get(x, y);
                if (current.Type == 445)
                {
                    _pixelTriggers.TryGetValue(index, out byte axes);
                    _pixelTriggers[index] = (byte)(axes | (direction < 2 ? 2 : 1));
                }
                if (!HasWire(next, channel)) continue;
                int nextIndex = Index(x, y);
                if (_remainingEntrances.TryGetValue(nextIndex, out byte remaining))
                {
                    if (remaining == 1) _remainingEntrances.Remove(nextIndex);
                    else _remainingEntrances[nextIndex] = (byte)(remaining - 1);
                    continue;
                }
                if (next.Type is 424 or 445)
                {
                    // Each router has four directed entrances. The set places a world-size bound on
                    // repeated directed visits while retaining independent crossing directions.
                    // Suppression of a repeated entrance is a safety policy, not a wire-length cutoff.
                    if (!_routerEntrances.Add((long)nextIndex * 4 + direction)) continue;
                }
                else _remainingEntrances.Add(nextIndex, 3);
                _pending.Enqueue(new WireVisit(x, y, direction));
            }
        }
    }

    private static bool Routes(WorldTile tile, byte entered, byte exit) => tile.Type switch
    {
        445 => entered == exit,
        // TileObjectData424 supplies exactly three frames. Malformed world frames fail closed;
        // treating an unknown frame as an unrestricted fan-out defeats the four-entrance bound.
        424 => tile.FrameX switch
        {
            0 => entered == exit,
            SourceFramePitch => (entered, exit) is (0, 3) or (3, 0) or (1, 2) or (2, 1),
            SwitchFrameWidth => (entered, exit) is (0, 2) or (2, 0) or (1, 3) or (3, 1),
            _ => false
        },
        _ => true
    };

    private void HitSwitchFromWire(int x, int y)
    {
        // Direct interactions replicate through packet59, whose client handler toggles the source
        // frame locally. Publishing its final frame first would make that handler toggle it twice.
        // Wiring.HitWireSingle144 instead publishes packet20 after the internal HitSwitch call.
        if (HitSwitchCore(x, y, fromWire: true)) _publish?.Invoke(x, y, 1, 1);
    }
    private void SkipWire(int x, int y)
    {
        if (Contains(x, y)) _skip.Add(Index(x, y));
    }

    private bool Register(int x, int y, int time)
    {
        foreach (Mechanism mechanism in _mechanisms)
            if (mechanism.X == x && mechanism.Y == y) return false;
        if (_mechanisms.Count == MechanismCapacity) return false;
        _mechanisms.Add(new Mechanism(x, y, time));
        return true;
    }

    private void ResetMomentary(int left, int top)
    {
        if (!ValidSwitchFootprint(left, top, MomentaryTile)) return;
        WorldTile anchor = _tiles.Get(left, top);
        int delta = anchor.FrameX >= SwitchFrameWidth ? -SwitchFrameWidth : SwitchFrameWidth;
        for (int x = left; x < left + 2; x++)
        for (int y = top; y < top + 2; y++)
        {
            if (!Contains(x, y)) continue;
            WorldTile tile = _tiles.Get(x, y);
            tile.FrameX = (short)(tile.FrameX + delta);
            _tiles.Set(x, y, in tile);
        }
        _publish?.Invoke(left, top, 2, 2);
    }

    private bool ValidSwitchFootprint(int left, int top, ushort type)
    {
        if (!Contains(left, top) || !Contains(left + 1, top + 1)) return false;
        WorldTile anchor = _tiles.Get(left, top);
        if (anchor.FrameX < 0 || anchor.FrameX % SwitchFrameWidth != 0 || anchor.FrameY != 0) return false;
        // Source-valid TileObjectData132/411 objects contain four matching frames. Reject a damaged
        // or forged footprint before committing any cell, rather than repairing unrelated neighbors.
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile cell = _tiles.Get(left + dx, top + dy);
            if (!cell.IsActive || cell.Type != type || cell.FrameX != anchor.FrameX + dx * SourceFramePitch ||
                cell.FrameY != dy * SourceFramePitch) return false;
        }
        return true;
    }

    private static bool HasWire(WorldTile tile, WorldWireChannel channel) =>
        (tile.Flags & (WorldTileFlags)(1 << (int)channel)) != 0;
    private int Index(int x, int y) => x * _tiles.Dimensions.HeightTiles + y;
    private bool Contains(int x, int y) => (uint)x < (uint)_tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)_tiles.Dimensions.HeightTiles;
    private bool ContainsMechanism(int x, int y) => x >= 1 && y >= 1 &&
        x < _tiles.Dimensions.WidthTiles - 1 && y < _tiles.Dimensions.HeightTiles - 1;
    private readonly record struct WireVisit(int X, int Y, byte Direction);
    private record struct Mechanism(int X, int Y, int Time);
}
