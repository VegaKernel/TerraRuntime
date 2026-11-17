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

    internal bool TripWireWithoutPlayerTeleportation(int x, int y, int width, int height)
    {
        if (_running) return false;
        blockPlayerTeleportation = true;
        try { return TripWire(x, y, width, height); }
        finally { blockPlayerTeleportation = false; }
    }

    private void RegisterTeleporter(int left, int row, bool halfBrick)
    {
        var point = new WorldWiringTeleporter1458(left, row, halfBrick);
        if (firstTeleporter is not { } first) firstTeleporter = point;
        else if (first.Left != left || first.Row != row || first.HalfBrick) secondTeleporter = point;
    }
}
