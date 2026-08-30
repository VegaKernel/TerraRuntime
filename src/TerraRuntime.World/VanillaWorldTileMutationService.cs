using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Semantic authoritative world-mutation operations for the single-tile vanilla subset. Packet/file encodings are
/// deliberately absent: callers cross into this boundary only after identity, inventory/tool and policy validation.
/// Frame-important/multi-tile content is rejected until its object-specific placement/break rules are implemented.
/// </summary>
public enum WorldTileMutationKind : byte
{
    PlaceTile = 1,
    KillTile = 2,
    PlaceWall = 3,
    KillWall = 4,
    SetShape = 5
}

public enum WorldTileMutationStatus : byte
{
    Applied = 0,
    NoChange = 1,
    OutOfBounds = 2,
    InvalidContent = 3,
    Occupied = 4,
    Empty = 5,
    FrameImportantUnsupported = 6,
    InvalidShape = 7,
    UnsupportedState = 8
}

/// <summary>
/// Typed request. Only the identity relevant to <see cref="Kind"/> is consumed. Shape uses the normalized
/// WorldTile ABI: 0 = full, 1 = half-brick, 2..5 = vanilla slopes plus one.
/// </summary>
public readonly record struct WorldTileMutationRequest(
    WorldTileMutationKind Kind,
    int X,
    int Y,
    TileTypeId TileType = default,
    WallTypeId WallType = default,
    byte Shape = 0);

/// <summary>
/// Result of one authoritative mutation. Frame bounds describe the bounded neighborhood whose simple-tile frame
/// state was canonicalized together with the mutation. The bounds are inclusive and always clipped to the world.
/// </summary>
public readonly record struct WorldTileMutationResult(
    WorldTileMutationStatus Status,
    WorldTile Before,
    WorldTile After,
    int FrameMinX,
    int FrameMinY,
    int FrameMaxX,
    int FrameMaxY,
    int ChangedTiles)
{
    public bool Applied => Status == WorldTileMutationStatus.Applied;
}

/// <summary>
/// Runtime-owned single-writer mutation engine for ordinary non-frame-important vanilla tiles and walls.
/// The service centralizes type validation, shape semantics, preservation/clearing of independent wire/wall/liquid
/// state, section dirtiness and canonical simple framing. Complex TileObjectData placement stays out rather than
/// being approximated by raw frame arithmetic.
/// </summary>
public sealed class VanillaWorldTileMutationService
{
    private const byte MaximumShape = 5;

    private readonly WorldTileStore _tiles;

    public VanillaWorldTileMutationService(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public WorldTileMutationResult Apply(in WorldTileMutationRequest request)
    {
        if (!Contains(request.X, request.Y))
            return Rejected(WorldTileMutationStatus.OutOfBounds);

        WorldTile before = _tiles.Get(request.X, request.Y);
        return request.Kind switch
        {
            WorldTileMutationKind.PlaceTile => PlaceTile(in request, in before),
            WorldTileMutationKind.KillTile => KillTile(in request, in before),
            WorldTileMutationKind.PlaceWall => PlaceWall(in request, in before),
            WorldTileMutationKind.KillWall => KillWall(in request, in before),
            WorldTileMutationKind.SetShape => SetShape(in request, in before),
            _ => Rejected(WorldTileMutationStatus.UnsupportedState, in before)
        };
    }

    private WorldTileMutationResult PlaceTile(in WorldTileMutationRequest request, in WorldTile before)
    {
        if (!VanillaTileDefinitionCatalog.TryGet(request.TileType, out VanillaTileDefinition definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        if (definition.IsFrameImportant || VanillaMultiTileObjectCatalog.TryGet(request.TileType, out _))
            return Rejected(WorldTileMutationStatus.FrameImportantUnsupported, in before);
        if (before.IsActive)
            return Rejected(WorldTileMutationStatus.Occupied, in before);

        WorldTile after = before;
        if (!after.TrySetTileType(request.TileType))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);

        after.Flags &= ~(WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        after.Flags |= WorldTileFlags.Active;
        after.TileColor = 0;
        after.Shape = 0;
        after.FrameX = 0;
        after.FrameY = 0;
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false);
    }

    private WorldTileMutationResult KillTile(in WorldTileMutationRequest request, in WorldTile before)
    {
        if (!before.IsActive)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out VanillaTileDefinition definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        if (definition.IsFrameImportant || VanillaMultiTileObjectCatalog.TryGet(before.TileType, out _))
            return Rejected(WorldTileMutationStatus.FrameImportantUnsupported, in before);

        WorldTile after = before;
        after.Type = 0;
        after.FrameX = 0;
        after.FrameY = 0;
        after.TileColor = 0;
        after.Shape = 0;
        after.Flags &= ~(
            WorldTileFlags.Active |
            WorldTileFlags.Actuator |
            WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock |
            WorldTileFlags.FullbrightBlock);
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false);
    }

    private WorldTileMutationResult PlaceWall(in WorldTileMutationRequest request, in WorldTile before)
    {
        if (request.WallType == VanillaWallIds.None ||
            !VanillaWallDefinitionCatalog.TryGet(request.WallType, out VanillaWallDefinition definition) ||
            !definition.IsPresent)
        {
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        }

        if (before.WallType != VanillaWallIds.None)
            return Rejected(WorldTileMutationStatus.Occupied, in before);

        WorldTile after = before;
        if (!after.TrySetWallType(request.WallType))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        after.WallColor = 0;
        after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: false, wallFrame: true);
    }

    private WorldTileMutationResult KillWall(in WorldTileMutationRequest request, in WorldTile before)
    {
        if (before.WallType == VanillaWallIds.None)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaWallDefinitionCatalog.TryGet(before.WallType, out _))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);

        WorldTile after = before;
        after.Wall = checked((ushort)VanillaWallIds.None.Value);
        after.WallColor = 0;
        after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: false, wallFrame: true);
    }

    private WorldTileMutationResult SetShape(in WorldTileMutationRequest request, in WorldTile before)
    {
        if (request.Shape > MaximumShape)
            return Rejected(WorldTileMutationStatus.InvalidShape, in before);
        if (!before.IsActive)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out VanillaTileDefinition definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        if (definition.IsFrameImportant || definition.IsSolidTop || !definition.IsSolid)
            return Rejected(WorldTileMutationStatus.UnsupportedState, in before);
        if (before.Shape == request.Shape)
            return Rejected(WorldTileMutationStatus.NoChange, in before);

        WorldTile after = before;
        after.Shape = request.Shape;
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false);
    }

    private WorldTileMutationResult CommitAndFrame(
        int x,
        int y,
        in WorldTile before,
        in WorldTile after,
        bool tileFrame,
        bool wallFrame)
    {
        _tiles.Set(x, y, in after);
        int minX = Math.Max(0, x - 1);
        int maxX = Math.Min(_tiles.Dimensions.WidthTiles - 1, x + 1);
        int minY = Math.Max(0, y - 1);
        int maxY = Math.Min(_tiles.Dimensions.HeightTiles - 1, y + 1);
        int changed = 1;

        // Non-frame-important tile frames are not persisted by the vanilla world format. TerraRuntime therefore
        // canonicalizes the local 3x3 neighborhood to zero instead of inventing client sprite-frame arithmetic.
        // Wall framing is likewise represented by section dirtiness; there is no wall-frame field in WorldTile.
        if (tileFrame)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                for (int ty = minY; ty <= maxY; ty++)
                {
                    if (tx == x && ty == y)
                        continue;

                    WorldTile neighbor = _tiles.Get(tx, ty);
                    if (!neighbor.IsActive ||
                        !VanillaTileDefinitionCatalog.TryGet(neighbor.TileType, out VanillaTileDefinition neighborDefinition) ||
                        neighborDefinition.IsFrameImportant ||
                        (neighbor.FrameX == 0 && neighbor.FrameY == 0))
                    {
                        continue;
                    }

                    neighbor.FrameX = 0;
                    neighbor.FrameY = 0;
                    _tiles.Set(tx, ty, in neighbor);
                    changed++;
                }
            }
        }

        if (wallFrame)
        {
            MarkFrameSectionsDirty(minX, minY, maxX, maxY);
        }

        return new WorldTileMutationResult(
            WorldTileMutationStatus.Applied,
            before,
            _tiles.Get(x, y),
            minX,
            minY,
            maxX,
            maxY,
            changed);
    }

    private void MarkFrameSectionsDirty(int minX, int minY, int maxX, int maxY)
    {
        WorldSectionId first = TerrariaSectionGeometry.FromTile(_tiles.Dimensions, minX, minY);
        WorldSectionId last = TerrariaSectionGeometry.FromTile(_tiles.Dimensions, maxX, maxY);
        for (int sectionX = first.X; sectionX <= last.X; sectionX++)
        {
            for (int sectionY = first.Y; sectionY <= last.Y; sectionY++)
            {
                var section = new WorldSectionId(sectionX, sectionY);
                _tiles.DirtySections.MarkDirty(section);
                _tiles.PersistenceDirtySections.MarkDirty(section);
            }
        }
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)_tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)_tiles.Dimensions.HeightTiles;

    private static WorldTileMutationResult Rejected(WorldTileMutationStatus status) =>
        new(status, default, default, 0, 0, 0, 0, 0);

    private static WorldTileMutationResult Rejected(WorldTileMutationStatus status, in WorldTile before) =>
        new(status, before, before, 0, 0, 0, 0, 0);
}
