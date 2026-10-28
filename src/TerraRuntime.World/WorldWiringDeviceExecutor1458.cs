using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>Semantic door transitions emitted by Terraria1458 Wiring.HitWireSingle.</summary>
public enum WorldWiringDoorAction1458 : byte
{
    OpenDoor = 0,
    CloseDoor = 1,
    OpenTrapdoor = 2,
    CloseTrapdoor = 3,
    OpenTallGate = 4,
    CloseTallGate = 5
}

public readonly record struct WorldWiringDoorTransition1458(
    WorldWiringDoorAction1458 Action, int X, int Y, int Direction);

/// <summary>
/// Source-pinned wire device mutations over the authoritative tile store. Circuit traversal owns the
/// per-color skip set; this owner only supplies the object footprints for devices that vanilla skips.
/// </summary>
public sealed class WorldWiringDeviceExecutor1458
{
    // Terraria1458 Wiring/TileID identities and source frame units, not configurable runtime policy.
    private const ushort Torch = 4, ClosedDoor = 10, OpenDoor = 11, Chest = 21, Altar = 26;
    private const ushort Candle = 33, Chandelier = 34, HangingLantern = 42, WaterCandle = 49;
    private const ushort Hellforge = 77, Dresser = 88, LampPost = 92, Lamp = 93, ChineseLantern = 95;
    private const ushort Candelabra = 100, DiscoBall = 126, Timer = 144, HolidayLight = 149;
    private const ushort PlatinumCandelabra = 173, PlatinumCandle = 174, Campfire = 215, TempleBrick = 226;
    private const ushort TempleAltar = 237, Track = 314, PeaceCandle = 372, Bubble = 379;
    private const ushort OpenTrapdoor = 386, ClosedTrapdoor = 387, ClosedTallGate = 388, OpenTallGate = 389;
    private const ushort Fireplace = 405, MomentarySwitch = 411, FakeChest = 441, SecondChest = 467;
    private const ushort SecondFakeChest = 468, DisplayDoll = 470, HatRack = 475, GolfHole = 476;
    private const ushort PlasmaLamp = 564, Pylon = 597, ShadowCandle = 646;
    private const int FramePitch = 18, DoubleFrameWidth = 36, TripleFrameWidth = 54;
    private const int ChandelierStyleWidth = 108, TorchStateStride = 66;

    private readonly WorldTileStore tiles;
    private readonly VanillaWorldWiringMutationService wiring;
    private readonly VanillaWorldGroundFighterDoorOpeningService? doors;
    private readonly Action<int, int, byte, byte>? publishTileSquare;
    private readonly Func<int>? nextDoorDirection;
    private readonly Func<bool>? plantBossDowned;
    private readonly Func<int, int, bool>? canKillTile;
    private readonly Action<WorldWiringDoorTransition1458>? publishDoorToggle;
    private readonly Func<int, int, bool>? applyActuatorTransaction;
    private readonly double worldSurface;

    public WorldWiringDeviceExecutor1458(WorldTileStore tiles,
        VanillaWorldGroundFighterDoorOpeningService? doors = null,
        Action<int, int, byte, byte>? publishTileSquare = null,
        Func<int>? nextDoorDirection = null,
        Func<bool>? plantBossDowned = null,
        double worldSurface = 0,
        Func<int, int, bool>? canKillTile = null,
        Action<WorldWiringDoorTransition1458>? publishDoorToggle = null,
        Func<int, int, bool>? applyActuatorTransaction = null)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        wiring = new VanillaWorldWiringMutationService(tiles);
        this.doors = doors;
        this.publishTileSquare = publishTileSquare;
        this.nextDoorDirection = nextDoorDirection;
        this.plantBossDowned = plantBossDowned;
        this.worldSurface = tiles.WorldSurfaceTiles ?? worldSurface;
        this.canKillTile = canKillTile;
        this.publishDoorToggle = publishDoorToggle;
        this.applyActuatorTransaction = applyActuatorTransaction;
    }

    /// <summary>
    /// Executes one reached cell in Terraria1458 Wiring.HitWireSingle order: actuator first, then device.
    /// Timer144 delegates to the circuit's existing switch/timer owner.
    /// </summary>
    public bool Hit(int x, int y, WorldWireChannel channel, Action<int, int> skipWire,
        Action<int, int>? hitSwitch = null)
    {
        ArgumentNullException.ThrowIfNull(skipWire);
        if (!Contains(x, y) || channel is < WorldWireChannel.Red or > WorldWireChannel.Yellow) return false;
        WorldTile tile = tiles.Get(x, y);
        bool changed = tile.HasActuator && ToggleActuator(x, y, in tile);
        if (!tile.IsActive) return changed;

        if (tile.Type == Timer)
        {
            hitSwitch?.Invoke(x, y);
            return changed || hitSwitch is not null;
        }
        if (tile.FrameX < 0 || tile.FrameY < 0) return changed;

        // The delegated WorldGen door operations own their source-specific SkipWire geometry and
        // timing. In particular a rejected occupied gate still skips its validated footprint.
        if (tile.Type is ClosedDoor or OpenDoor or OpenTrapdoor or ClosedTrapdoor or ClosedTallGate or OpenTallGate)
            return ToggleDoor(x, y, in tile, skipWire) || changed;

        return tile.Type switch
        {
            Torch => ShiftFrames(x, y, 1, 1, tile.Type, tile.FrameX >= TorchStateStride ? -TorchStateStride : TorchStateStride, false, null) || changed,
            Candle or WaterCandle or PlatinumCandle or PeaceCandle or ShadowCandle =>
                ShiftFrames(x, y, 1, 1, tile.Type, tile.FrameX > 0 ? -FramePitch : FramePitch, false, null) || changed,
            HolidayLight => ShiftFrames(x, y, 1, 1, tile.Type, tile.FrameX >= TripleFrameWidth ? -TripleFrameWidth : TripleFrameWidth, false, null) || changed,
            HangingLantern => ToggleVerticalLight(x, y, in tile, 2, skipWire) || changed,
            Lamp => ToggleVerticalLight(x, y, in tile, 3, skipWire) || changed,
            LampPost => ShiftFrames(x, y - tile.FrameY / FramePitch, 1, 6, tile.Type,
                tile.FrameX > 0 ? -FramePitch : FramePitch, false, skipWire) || changed,
            ChineseLantern or Candelabra or DiscoBall or PlatinumCandelabra or PlasmaLamp => ToggleSquareLight(x, y, in tile, 2, skipWire) || changed,
            Chandelier => ToggleSquareLight(x, y, in tile, 3, skipWire) || changed,
            Campfire => ToggleCampfire(x, y, in tile, skipWire) || changed,
            Fireplace => ToggleFireplace(x, y, in tile, skipWire) || changed,
            MomentarySwitch => ToggleMomentarySwitch(x, y, in tile, skipWire) || changed,
            _ => changed
        };
    }

    private bool ToggleActuator(int x, int y, in WorldTile tile)
    {
        if (!tile.IsActive) return false;
        if (!tile.IsActuated)
        {
            // Wiring.DeActive: progression is Plantera, not Golem. NotReallySolid has 10,387,388;
            // the remaining exclusions are the explicit source switch, not solid-top heuristics.
            if (tile.Type == TempleBrick && y > worldSurface && plantBossDowned?.Invoke() != true) return false;
            if (!VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                tile.Type is ClosedDoor or Track or Bubble or OpenTrapdoor or ClosedTrapdoor or ClosedTallGate or OpenTallGate or GolfHole || y == 0) return false;
            WorldTile above = tiles.Get(x, y - 1);
            if (above.IsActive && (PreventsActuationUnder(above.Type) || canKillTile?.Invoke(x, y) != true))
                return false;
        }
        // Production composition prepares framing, drops and the existing RNG together. An unsupported
        // frame rejects that transaction; it must never fall back to an already-visible raw bit change.
        if (applyActuatorTransaction is not null) return applyActuatorTransaction(x, y);
        var request = new WorldWiringMutationRequest(tile.IsActuated
            ? WorldWiringMutationKind.Deactuate : WorldWiringMutationKind.Actuate, x, y);
        if (!wiring.Apply(in request).Applied) return false;
        // Store.Set updates section versions, persistence and the falling wake owner. Collision
        // reads the same IsActuated bit directly; no stale parallel collision image exists.
        Publish(x, y, 1, 1);
        return true;
    }

    private bool ToggleVerticalLight(int x, int y, in WorldTile tile, byte height, Action<int, int> skipWire) =>
        ShiftFrames(x, y - tile.FrameY / FramePitch % height, 1, height, tile.Type,
            tile.FrameX > 0 ? -FramePitch : FramePitch, false, skipWire);

    private bool ToggleSquareLight(int x, int y, in WorldTile tile, byte size, Action<int, int> skipWire)
    {
        // The chandelier repeats horizontal styles every108; the admitted2x2 source frame band is72.
        int left = x - tile.FrameX % (size * DoubleFrameWidth) / FramePitch % size;
        int top = y - tile.FrameY / FramePitch % size;
        if (!Contains(left, top)) return false;
        WorldTile anchor = tiles.Get(left, top);
        bool off = size == 3 ? anchor.FrameX % ChandelierStyleWidth > 0 : anchor.FrameX > 0;
        return ShiftFrames(left, top, size, size, tile.Type, off ? -size * FramePitch : size * FramePitch, false, skipWire);
    }

    private bool ToggleCampfire(int x, int y, in WorldTile tile, Action<int, int> skipWire)
    {
        int left = x - tile.FrameX % TripleFrameWidth / FramePitch;
        int top = y - tile.FrameY % DoubleFrameWidth / FramePitch;
        if (!Contains(left, top)) return false;
        int delta = tiles.Get(left, top).FrameY >= DoubleFrameWidth ? -DoubleFrameWidth : DoubleFrameWidth;
        return ShiftFrames(left, top, 3, 2, tile.Type, delta, true, skipWire);
    }

    private bool ToggleFireplace(int x, int y, in WorldTile tile, Action<int, int> skipWire)
    {
        int left = x - tile.FrameX % TripleFrameWidth / FramePitch;
        int top = y - tile.FrameY % DoubleFrameWidth / FramePitch;
        if (!Contains(left, top)) return false;
        int delta = tiles.Get(left, top).FrameX >= TripleFrameWidth ? -TripleFrameWidth : TripleFrameWidth;
        return ShiftFrames(left, top, 3, 2, tile.Type, delta, false, skipWire);
    }

    private bool ToggleMomentarySwitch(int x, int y, in WorldTile tile, Action<int, int> skipWire)
    {
        int left = x - tile.FrameX % DoubleFrameWidth / FramePitch;
        int top = y - tile.FrameY % DoubleFrameWidth / FramePitch;
        if (!Contains(left, top)) return false;
        int delta = tiles.Get(left, top).FrameX >= DoubleFrameWidth ? -DoubleFrameWidth : DoubleFrameWidth;
        // A wire changes only411's frames. HitSwitch's clock registration and nested pulse are absent.
        return ShiftFrames(left, top, 2, 2, tile.Type, delta, false, skipWire);
    }

    private bool ShiftFrames(int left, int top, byte width, byte height, ushort type,
        int delta, bool vertical, Action<int, int>? skipWire)
    {
        if (!Contains(left, top) || !Contains(left + width - 1, top + height - 1)) return false;
        // Malformed/partially destroyed footprints cannot be repaired by mutating neighboring content.
        // Validate the complete footprint and frame arithmetic before the first authoritative write.
        for (int col = 0; col < width; col++)
        for (int row = 0; row < height; row++)
        {
            WorldTile cell = tiles.Get(left + col, top + row);
            int frame = (vertical ? cell.FrameY : cell.FrameX) + delta;
            if (!cell.IsActive || cell.Type != type || frame is < short.MinValue or > short.MaxValue) return false;
        }
        for (int col = 0; col < width; col++)
        for (int row = 0; row < height; row++)
        {
            int x = left + col, y = top + row;
            WorldTile cell = tiles.Get(x, y);
            if (vertical) cell.FrameY = (short)(cell.FrameY + delta);
            else cell.FrameX = (short)(cell.FrameX + delta);
            tiles.Set(x, y, in cell);
            skipWire?.Invoke(x, y);
        }
        Publish(left, top, width, height);
        return true;
    }

    private bool ToggleDoor(int x, int y, in WorldTile tile, Action<int, int> skipWire)
    {
        if (doors is null) return false;
        bool applied;
        int direction = -1; // Source packet19's omitted/default direction encodes false, decoded as-1.
        WorldWiringDoorAction1458 action;
        if (tile.Type == ClosedDoor)
        {
            // Wiring uses Main.rand once even if both opening directions fail. The injected callback
            // consumes that actual shared stream; no private RNG is created here.
            direction = nextDoorDirection?.Invoke() ?? 0;
            if (direction is not (-1 or 1)) return false;
            var intent = new VanillaGroundFighterDoorOpeningIntent(x, y, direction, VanillaTileIds.ClosedDoor);
            applied = doors.TryOpen(in intent, out _, skipWire);
            if (!applied)
            {
                direction = -direction;
                intent = intent with { DirectionX = direction };
                applied = doors.TryOpen(in intent, out _, skipWire);
            }
            action = WorldWiringDoorAction1458.OpenDoor;
        }
        else if (tile.Type == OpenDoor)
        {
            applied = doors.TryCloseDoor(x, y, forced: true, out _, skipWire);
            action = WorldWiringDoorAction1458.CloseDoor;
        }
        else if (tile.Type is ClosedTallGate or OpenTallGate)
        {
            applied = doors.TryShiftTallGate(x, y, closing: tile.Type == OpenTallGate, forced: false, out _, skipWire);
            action = tile.Type == OpenTallGate ? WorldWiringDoorAction1458.CloseTallGate : WorldWiringDoorAction1458.OpenTallGate;
        }
        else
        {
            applied = doors.TryShiftTrapdoor(x, y, playerAbove: true, opening: tile.Type == ClosedTrapdoor, out _, skipWire);
            if (!applied) applied = doors.TryShiftTrapdoor(x, y, playerAbove: false, opening: tile.Type == ClosedTrapdoor, out _, skipWire);
            else direction = 1;
            action = tile.Type == ClosedTrapdoor ? WorldWiringDoorAction1458.OpenTrapdoor : WorldWiringDoorAction1458.CloseTrapdoor;
        }
        // Publication uses a clipped box around the touched row. Gate transforms can reach four rows
        // either side; ordinary doors and trapdoors fit within the same bounded region.
        // Wiring emits a door action before the enclosing switch59 relay. Tall gates emit it even
        // when the unforced server operation was rejected. Clients apply that action as forced, so
        // follow it with the authoritative tile image to converge after occupancy disagreement.
        if (applied || tile.Type is ClosedTallGate or OpenTallGate)
        {
            publishDoorToggle?.Invoke(new(action, x, y, direction));
            int left = Math.Max(0, x - 1), top = Math.Max(0, y - 4);
            Publish(left, top, (byte)Math.Min(3, tiles.Dimensions.WidthTiles - left),
                (byte)Math.Min(9, tiles.Dimensions.HeightTiles - top));
        }
        return applied;
    }

    private static bool PreventsActuationUnder(ushort type) =>
        type is Chest or SecondChest or Altar or Hellforge or Dresser or DisplayDoll or HatRack or TempleAltar or Pylon or FakeChest or SecondFakeChest;

    private void Publish(int x, int y, byte width, byte height) => publishTileSquare?.Invoke(x, y, width, height);

    private bool Contains(int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles && (uint)y < (uint)tiles.Dimensions.HeightTiles;
}
