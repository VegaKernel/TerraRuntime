namespace TerraRuntime.World;

public enum WorldWireChannel : byte
{
    Red = 1,
    Blue = 2,
    Green = 3,
    Yellow = 4
}

public enum WorldWiringMutationKind : byte
{
    PlaceWire = 1,
    KillWire = 2,
    PlaceActuator = 3,
    KillActuator = 4,
    Actuate = 5,
    Deactuate = 6
}

public enum WorldWiringMutationStatus : byte
{
    Applied = 0,
    NoChange = 1,
    OutOfBounds = 2,
    InvalidChannel = 3,
    MissingTile = 4,
    MissingActuator = 5,
    UnsupportedOperation = 6
}

public readonly record struct WorldWiringMutationRequest(
    WorldWiringMutationKind Kind,
    int X,
    int Y,
    WorldWireChannel Channel = default);

public readonly record struct WorldWiringMutationResult(
    WorldWiringMutationStatus Status,
    WorldTile Before,
    WorldTile After)
{
    public bool Applied => Status == WorldWiringMutationStatus.Applied;
}

/// <summary>
/// Authoritative single-cell wire and actuator state owner. Circuit discovery, pulse ordering and device behavior
/// are separate schedulers; this boundary only commits already-authorized wiring state and section dirtiness.
/// </summary>
public sealed class VanillaWorldWiringMutationService
{
    private readonly WorldTileStore _tiles;

    public VanillaWorldWiringMutationService(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public WorldWiringMutationResult Apply(in WorldWiringMutationRequest request)
    {
        if (!Contains(request.X, request.Y))
            return Rejected(WorldWiringMutationStatus.OutOfBounds);

        WorldTile before = _tiles.Get(request.X, request.Y);
        WorldTile after = before;
        WorldWiringMutationStatus status = request.Kind switch
        {
            WorldWiringMutationKind.PlaceWire => SetWire(ref after, request.Channel, enabled: true),
            WorldWiringMutationKind.KillWire => SetWire(ref after, request.Channel, enabled: false),
            WorldWiringMutationKind.PlaceActuator => SetActuator(ref after, enabled: true),
            WorldWiringMutationKind.KillActuator => SetActuator(ref after, enabled: false),
            WorldWiringMutationKind.Actuate => SetActuated(ref after, enabled: true),
            WorldWiringMutationKind.Deactuate => SetActuated(ref after, enabled: false),
            _ => WorldWiringMutationStatus.UnsupportedOperation
        };

        if (status != WorldWiringMutationStatus.Applied)
            return Rejected(status, in before);

        _tiles.Set(request.X, request.Y, in after);
        return new WorldWiringMutationResult(status, before, after);
    }

    private static WorldWiringMutationStatus SetWire(
        ref WorldTile tile,
        WorldWireChannel channel,
        bool enabled)
    {
        WorldTileFlags flag = channel switch
        {
            WorldWireChannel.Red => WorldTileFlags.WireRed,
            WorldWireChannel.Blue => WorldTileFlags.WireBlue,
            WorldWireChannel.Green => WorldTileFlags.WireGreen,
            WorldWireChannel.Yellow => WorldTileFlags.WireYellow,
            _ => WorldTileFlags.None
        };
        if (flag == WorldTileFlags.None)
            return WorldWiringMutationStatus.InvalidChannel;

        bool alreadySet = (tile.Flags & flag) != 0;
        if (alreadySet == enabled)
            return WorldWiringMutationStatus.NoChange;

        tile.TrySetFlags(flag, enabled);
        return WorldWiringMutationStatus.Applied;
    }

    private static WorldWiringMutationStatus SetActuator(ref WorldTile tile, bool enabled)
    {
        if (!tile.IsActive)
            return WorldWiringMutationStatus.MissingTile;

        if (tile.HasActuator == enabled)
            return WorldWiringMutationStatus.NoChange;

        tile.TrySetFlags(WorldTileFlags.Actuator, enabled);
        if (!enabled)
            tile.TrySetFlags(WorldTileFlags.Inactive, enabled: false);
        return WorldWiringMutationStatus.Applied;
    }

    private static WorldWiringMutationStatus SetActuated(ref WorldTile tile, bool enabled)
    {
        if (!tile.IsActive)
            return WorldWiringMutationStatus.MissingTile;
        if (!tile.HasActuator)
            return WorldWiringMutationStatus.MissingActuator;
        if (tile.IsActuated == enabled)
            return WorldWiringMutationStatus.NoChange;

        tile.TrySetFlags(WorldTileFlags.Inactive, enabled);
        return WorldWiringMutationStatus.Applied;
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)_tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)_tiles.Dimensions.HeightTiles;

    private static WorldWiringMutationResult Rejected(WorldWiringMutationStatus status) =>
        new(status, default, default);

    private static WorldWiringMutationResult Rejected(WorldWiringMutationStatus status, in WorldTile before) =>
        new(status, before, before);
}
