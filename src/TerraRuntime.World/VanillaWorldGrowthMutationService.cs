using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum WorldGrowthMutationKind : byte
{
    Grow = 1,
    Spread = 2
}

public enum WorldGrowthMutationStatus : byte
{
    Applied = 0,
    NoChange = 1,
    OutOfBounds = 2,
    InvalidContent = 3,
    SourceMismatch = 4,
    UnsupportedTile = 5,
    UnsupportedOperation = 6
}

/// <summary>
/// Guarded commit produced after a growth rule has selected an eligible cell. ExpectedTileType makes stale queued
/// work fail closed; ResultTileType remains a typed, catalog-validated vanilla identity.
/// </summary>
public readonly record struct WorldGrowthMutationRequest(
    WorldGrowthMutationKind Kind,
    int X,
    int Y,
    TileTypeId ExpectedTileType,
    TileTypeId ResultTileType);

public readonly record struct WorldGrowthMutationResult(
    WorldGrowthMutationStatus Status,
    WorldTile Before,
    WorldTile After)
{
    public bool Applied => Status == WorldGrowthMutationStatus.Applied;
}

/// <summary>
/// Authoritative commit boundary for already-verified Grow/Spread decisions. Random selection, light/biome/time
/// eligibility and bounded scheduling remain rule/scheduler concerns and cannot bypass this stale-state guard.
/// </summary>
public sealed class VanillaWorldGrowthMutationService
{
    private readonly WorldTileStore _tiles;

    public VanillaWorldGrowthMutationService(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public WorldGrowthMutationResult Apply(in WorldGrowthMutationRequest request)
    {
        if (!Contains(request.X, request.Y))
            return Rejected(WorldGrowthMutationStatus.OutOfBounds);
        if (request.Kind is not (WorldGrowthMutationKind.Grow or WorldGrowthMutationKind.Spread))
            return Rejected(WorldGrowthMutationStatus.UnsupportedOperation);
        if (!VanillaTileDefinitionCatalog.TryGet(request.ExpectedTileType, out VanillaTileDefinition expected) ||
            !VanillaTileDefinitionCatalog.TryGet(request.ResultTileType, out VanillaTileDefinition result))
        {
            return Rejected(WorldGrowthMutationStatus.InvalidContent);
        }

        WorldTile before = _tiles.Get(request.X, request.Y);
        if (!before.IsActive || before.TileType != request.ExpectedTileType)
            return Rejected(WorldGrowthMutationStatus.SourceMismatch, in before);
        if (expected.IsFrameImportant || result.IsFrameImportant ||
            VanillaMultiTileObjectCatalog.TryGet(expected.Type, out _) ||
            VanillaMultiTileObjectCatalog.TryGet(result.Type, out _))
        {
            return Rejected(WorldGrowthMutationStatus.UnsupportedTile, in before);
        }
        if (request.ExpectedTileType == request.ResultTileType)
            return Rejected(WorldGrowthMutationStatus.NoChange, in before);

        WorldTile after = before;
        if (!after.TrySetTileType(request.ResultTileType))
            return Rejected(WorldGrowthMutationStatus.InvalidContent, in before);
        after.FrameX = 0;
        after.FrameY = 0;
        after.Shape = 0;
        _tiles.Set(request.X, request.Y, in after);
        return new WorldGrowthMutationResult(WorldGrowthMutationStatus.Applied, before, after);
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)_tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)_tiles.Dimensions.HeightTiles;

    private static WorldGrowthMutationResult Rejected(WorldGrowthMutationStatus status) =>
        new(status, default, default);

    private static WorldGrowthMutationResult Rejected(WorldGrowthMutationStatus status, in WorldTile before) =>
        new(status, before, before);
}
