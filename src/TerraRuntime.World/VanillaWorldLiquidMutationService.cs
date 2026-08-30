namespace TerraRuntime.World;

public enum WorldLiquidMutationKind : byte
{
    SetLiquid = 1,
    ClearLiquid = 2
}

public enum WorldLiquidMutationStatus : byte
{
    Applied = 0,
    NoChange = 1,
    OutOfBounds = 2,
    InvalidLiquid = 3,
    UnsupportedOperation = 4
}

public readonly record struct WorldLiquidMutationRequest(
    WorldLiquidMutationKind Kind,
    int X,
    int Y,
    byte Amount = 0,
    WorldLiquidKind LiquidKind = WorldLiquidKind.Water);

public readonly record struct WorldLiquidMutationResult(
    WorldLiquidMutationStatus Status,
    WorldTile Before,
    WorldTile After,
    int ScheduledCells)
{
    public bool Applied => Status == WorldLiquidMutationStatus.Applied;
}

/// <summary>
/// Authoritative liquid material mutation boundary. Material commits and liquid-simulation scheduling are kept
/// together, while flow/reaction rules and per-tick budgets remain owned by the liquid simulation subsystem.
/// </summary>
public sealed class VanillaWorldLiquidMutationService
{
    private readonly WorldTileStore _tiles;

    public VanillaWorldLiquidMutationService(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public WorldLiquidMutationResult Apply(in WorldLiquidMutationRequest request)
    {
        if (!Contains(request.X, request.Y))
            return Rejected(WorldLiquidMutationStatus.OutOfBounds);

        WorldTile before = _tiles.Get(request.X, request.Y);
        WorldTile after = before;
        switch (request.Kind)
        {
            case WorldLiquidMutationKind.SetLiquid:
                if (request.Amount == 0 || !Enum.IsDefined(request.LiquidKind))
                    return Rejected(WorldLiquidMutationStatus.InvalidLiquid, in before);
                if (before.LiquidAmount == request.Amount && before.LiquidKind == request.LiquidKind)
                    return Rejected(WorldLiquidMutationStatus.NoChange, in before);
                after.LiquidAmount = request.Amount;
                after.LiquidKind = request.LiquidKind;
                break;

            case WorldLiquidMutationKind.ClearLiquid:
                if (before.LiquidAmount == 0 && before.LiquidKind == WorldLiquidKind.Water)
                    return Rejected(WorldLiquidMutationStatus.NoChange, in before);
                after.LiquidAmount = 0;
                after.LiquidKind = WorldLiquidKind.Water;
                break;

            default:
                return Rejected(WorldLiquidMutationStatus.UnsupportedOperation, in before);
        }

        _tiles.Set(request.X, request.Y, in after);
        int scheduled = ScheduleAffectedCells(request.X, request.Y);
        return new WorldLiquidMutationResult(
            WorldLiquidMutationStatus.Applied,
            before,
            after,
            scheduled);
    }

    private int ScheduleAffectedCells(int x, int y)
    {
        int scheduled = 0;
        if (_tiles.LiquidUpdates.TryEnqueue(x, y)) scheduled++;
        if (_tiles.LiquidUpdates.TryEnqueue(x - 1, y)) scheduled++;
        if (_tiles.LiquidUpdates.TryEnqueue(x + 1, y)) scheduled++;
        if (_tiles.LiquidUpdates.TryEnqueue(x, y - 1)) scheduled++;
        if (_tiles.LiquidUpdates.TryEnqueue(x, y + 1)) scheduled++;
        return scheduled;
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)_tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)_tiles.Dimensions.HeightTiles;

    private static WorldLiquidMutationResult Rejected(WorldLiquidMutationStatus status) =>
        new(status, default, default, 0);

    private static WorldLiquidMutationResult Rejected(WorldLiquidMutationStatus status, in WorldTile before) =>
        new(status, before, before, 0);
}
