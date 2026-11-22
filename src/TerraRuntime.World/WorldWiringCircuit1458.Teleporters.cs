namespace TerraRuntime.World;

public readonly record struct WorldWiringTeleporter1458(int Left, int Row, bool HalfBrick)
{
    public int PixelX => Left * 16;
    public int PixelY => Row * 16 + (HalfBrick ? 8 : 0);
}

internal readonly record struct WorldWiringTeleporterPair1458(WorldWiringTeleporter1458? First, WorldWiringTeleporter1458? Second);

public sealed partial class WorldWiringCircuit1458
{
    private readonly Action<WorldWiringTeleporter1458, WorldWiringTeleporter1458, bool>? teleport;
    private WorldWiringTeleporter1458? firstTeleporter, secondTeleporter;
    private bool blockPlayerTeleportation;
    private int teleportCallbackDepth;
    private bool traversingWires;
    internal bool PlayerTeleportationBlocked => blockPlayerTeleportation;

    internal bool TripWireWithoutPlayerTeleportation(int x, int y, int width, int height)
    {
        if (_running) return false;
        blockPlayerTeleportation = true;
        try { return TripWire(x, y, width, height); }
        finally { blockPlayerTeleportation = false; }
    }

    internal bool HitSwitchWithoutPlayerTeleportation(int x, int y)
    {
        if (!_running)
        {
            blockPlayerTeleportation = true;
            return HitSwitch(x, y);
        }
        // Original TripWire ends traversal before Teleport; only concrete source contacts
        // may enter here. Public/observer reentry remains blocked for the complete pulse.
        if (traversingWires || teleportCallbackDepth is <= 0 or >= 64 || !Contains(x, y)) return false;
        var tile = _tiles.Get(x, y);
        if (tile.Type is not (423 or 428)) return false;
        blockPlayerTeleportation = true;
        RunWirePulse(x, y, 1, 1);
        if (!drainingLogic)
        {
            _gatesDone.Clear();
            DrainLogicGates();
            _gatesDone.Clear();
            blockPlayerTeleportation = false;
        }
        return true;
    }

    internal bool HitLogicSensorSwitch(int x, int y, bool blockPlayers)
    {
        if (_running) return false;
        blockPlayerTeleportation = blockPlayers;
        return HitSwitch(x, y);
    }

    internal void EndLogicSensorUpdate() => blockPlayerTeleportation = false;

    private void RegisterTeleporter(int left, int row, bool halfBrick)
    {
        var point = new WorldWiringTeleporter1458(left, row, halfBrick);
        if (firstTeleporter is not { } first) firstTeleporter = point;
        else if (first.Left != left || first.Row != row || first.HalfBrick) secondTeleporter = point;
    }
}
