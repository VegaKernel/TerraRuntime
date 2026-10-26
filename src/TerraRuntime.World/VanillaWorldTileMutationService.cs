using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Semantic authoritative world-mutation operations for the single-tile vanilla subset. Packet/file encodings are
/// deliberately absent: callers cross into this boundary only after identity, inventory/tool and policy validation.
/// Frame-important/multi-tile content and tiles without fully modelled generic removal semantics are rejected until
/// their object-specific or mining-specific rules are implemented.
/// </summary>
public enum WorldTileMutationKind : byte
{
    PlaceTile = 1,
    KillTile = 2,
    PlaceWall = 3,
    KillWall = 4,
    SetShape = 5,
    TransformTile = 6,
    ClearTile = 7,
    SetToolShape = 8
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
    byte Shape = 0)
{
    public bool HasSourceShape { get; init; }
    public bool SourceHalfBrick { get; init; }
    public byte SourceSlope { get; init; }
}

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
/// Runtime-owned single-writer mutation engine for the admitted ordinary single-tile vanilla subset.
/// The service centralizes type validation, shape semantics, preservation/clearing of independent wire/wall/liquid
/// state, section dirtiness and canonical simple framing. Complex TileObjectData placement and vanilla removal
/// families with transform, support, container, progression or world-position rules stay out rather than being
/// approximated by raw cell clearing.
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
        return Evaluate(in request, in before, preparation: null);
    }

    /// <summary>
    /// Validates and stages the existing simple-cell operation without changing tiles,
    /// dirty sections or liquid queues. The authoritative caller can then adopt player
    /// clocks, source hit progress and reserved drops before publishing any observers.
    /// </summary>
    public bool TryPrepare(in WorldTileMutationRequest request,
        out WorldTileMutationPreparation? preparation, out WorldTileMutationResult result) =>
        TryPrepare(in request, default, out preparation, out result);

    public bool TryPrepare(in WorldTileMutationRequest request,
        ReadOnlySpan<(int X, int Y, WorldTile Tile)> virtualBefore,
        out WorldTileMutationPreparation? preparation, out WorldTileMutationResult result) =>
        TryPrepareCore(in request, virtualBefore, sourceFraming: false, out preparation, out result);

    /// <summary>
    /// Stages the qualified tool primitive only. The enclosing ordered source
    /// cascade owns framing; generic neighbor canonicalization must not run first.
    /// </summary>
    public bool TryPrepareToolPrimitive(in WorldTileMutationRequest request,
        ReadOnlySpan<(int X, int Y, WorldTile Tile)> virtualBefore,
        out WorldTileMutationPreparation? preparation, out WorldTileMutationResult result) =>
        TryPrepareCore(in request, virtualBefore, sourceFraming: true, out preparation, out result);

    private bool TryPrepareCore(in WorldTileMutationRequest request,
        ReadOnlySpan<(int X, int Y, WorldTile Tile)> virtualBefore, bool sourceFraming,
        out WorldTileMutationPreparation? preparation, out WorldTileMutationResult result)
    {
        preparation = null;
        if (!Contains(request.X, request.Y))
        {
            result = Rejected(WorldTileMutationStatus.OutOfBounds);
            return false;
        }

        if (virtualBefore.Length > 9)
        {
            result = Rejected(WorldTileMutationStatus.UnsupportedState);
            return false;
        }
        for (int index = 0; index < virtualBefore.Length; index++)
        {
            var cell = virtualBefore[index];
            if (!Contains(cell.X, cell.Y) || Math.Abs((long)cell.X - request.X) > 1 || Math.Abs((long)cell.Y - request.Y) > 1)
            {
                result = Rejected(WorldTileMutationStatus.UnsupportedState);
                return false;
            }
            for (int earlier = 0; earlier < index; earlier++)
                if (virtualBefore[earlier].X == cell.X && virtualBefore[earlier].Y == cell.Y)
                {
                    result = Rejected(WorldTileMutationStatus.UnsupportedState);
                    return false;
                }
        }
        var candidate = new WorldTileMutationPreparation(_tiles, request.X, request.Y, virtualBefore, sourceFraming);
        WorldTile before = candidate.ReadBefore(request.X, request.Y);
        result = Evaluate(in request, in before, candidate);
        if (!result.Applied)
            return false;
        if (!candidate.IsCurrent)
        {
            result = Rejected(WorldTileMutationStatus.UnsupportedState, in before);
            return false;
        }
        candidate.SetResult(in result);
        preparation = candidate;
        return true;
    }

    private WorldTileMutationResult Evaluate(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
    {
        return request.Kind switch
        {
            WorldTileMutationKind.PlaceTile => PlaceTile(in request, in before, preparation),
            WorldTileMutationKind.KillTile => KillTile(in request, in before, preparation),
            WorldTileMutationKind.PlaceWall => PlaceWall(in request, in before, preparation),
            WorldTileMutationKind.KillWall => KillWall(in request, in before, preparation),
            WorldTileMutationKind.SetShape => SetShape(in request, in before, preparation, sourceTool: false),
            WorldTileMutationKind.SetToolShape => SetShape(in request, in before, preparation, sourceTool: true),
            WorldTileMutationKind.TransformTile => TransformTile(in request, in before, preparation),
            WorldTileMutationKind.ClearTile => ClearTile(in request, in before, preparation),
            _ => Rejected(WorldTileMutationStatus.UnsupportedState, in before)
        };
    }

    private WorldTileMutationResult PlaceTile(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
    {
        if (!VanillaTileDefinitionCatalog.TryGet(request.TileType, out VanillaTileDefinition definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        if (definition.BreakPath != VanillaTileBreakPath.SimpleCell)
            return Rejected(WorldTileMutationStatus.FrameImportantUnsupported, in before);
        if (before.IsActive)
            return Rejected(WorldTileMutationStatus.Occupied, in before);

        WorldTile after = before;
        if (!after.TrySetTileType(request.TileType))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);

        after.Flags &= ~(WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        after.Flags |= WorldTileFlags.Active;
        after.TileColor = 0;
        after.ClearSourceShape();
        after.FrameX = 0;
        after.FrameY = 0;
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false, preparation);
    }

    private WorldTileMutationResult ClearTile(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
    {
        if (!before.IsActive) return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out var definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        if (definition.BreakPath != VanillaTileBreakPath.SimpleCell)
            return Rejected(WorldTileMutationStatus.FrameImportantUnsupported, in before);
        // Terraria1.4.5.8 Tile.ClearTile (e.g. a falling-block detachment), NOT KillTile/ClearTileAndPaint.
        // Shape/active/inActive change; paint, actuator, wire, wall and liquid remain in the source cell.
        WorldTile after = before;
        after.ClearSourceShape();
        after.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false, preparation);
    }

    private WorldTileMutationResult KillTile(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
    {
        if (!before.IsActive)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out VanillaTileDefinition definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        bool simpleCell = definition.BreakPath == VanillaTileBreakPath.SimpleCell;
        bool frameImportantSingleCell = definition.BreakPath == VanillaTileBreakPath.FrameImportantSingleCell;
        if (!simpleCell && !frameImportantSingleCell)
            return Rejected(WorldTileMutationStatus.FrameImportantUnsupported, in before);

        WorldTile after = before;
        after.Type = 0;
        after.FrameX = 0;
        after.FrameY = 0;
        after.TileColor = 0;
        after.ClearSourceShape();
        after.Flags &= ~(
            WorldTileFlags.Active |
            WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock |
            WorldTileFlags.FullbrightBlock);
        return CommitAndFrame(
            request.X,
            request.Y,
            in before,
            in after,
            tileFrame: simpleCell,
            wallFrame: false,
            preparation);
    }

    private WorldTileMutationResult TransformTile(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
    {
        if (!before.IsActive)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out VanillaTileDefinition sourceDefinition) ||
            sourceDefinition.BreakPath != VanillaTileBreakPath.SimpleCell ||
            !VanillaTileDefinitionCatalog.TryGet(request.TileType, out VanillaTileDefinition targetDefinition) ||
            targetDefinition.BreakPath != VanillaTileBreakPath.SimpleCell)
        {
            return Rejected(WorldTileMutationStatus.UnsupportedState, in before);
        }
        if (before.TileType == request.TileType)
            return Rejected(WorldTileMutationStatus.NoChange, in before);

        WorldTile after = before;
        if (!after.TrySetTileType(request.TileType))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);

        // Vanilla failed-pick transforms preserve the cell and independent state; SquareTileFrame then recomputes
        // sprite framing. TerraRuntime canonicalizes simple frames to zero and keeps paint/shape/wires/liquid intact.
        after.FrameX = 0;
        after.FrameY = 0;
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false, preparation);
    }

    private WorldTileMutationResult PlaceWall(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
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
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: false, wallFrame: true, preparation);
    }

    private WorldTileMutationResult KillWall(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation)
    {
        if (before.WallType == VanillaWallIds.None)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaWallDefinitionCatalog.TryGet(before.WallType, out _))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);

        WorldTile after = before;
        after.Wall = checked((ushort)VanillaWallIds.None.Value);
        after.WallColor = 0;
        after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: false, wallFrame: true, preparation);
    }

    private WorldTileMutationResult SetShape(in WorldTileMutationRequest request, in WorldTile before,
        WorldTileMutationPreparation? preparation, bool sourceTool)
    {
        if ((!sourceTool || !request.HasSourceShape) && request.Shape > MaximumShape)
            return Rejected(WorldTileMutationStatus.InvalidShape, in before);
        if (!before.IsActive)
            return Rejected(WorldTileMutationStatus.Empty, in before);
        if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out VanillaTileDefinition definition))
            return Rejected(WorldTileMutationStatus.InvalidContent, in before);
        if (!sourceTool && (definition.IsFrameImportant || definition.IsSolidTop || !definition.IsSolid))
            return Rejected(WorldTileMutationStatus.UnsupportedState, in before);
        WorldTile after = before;
        bool half = sourceTool && request.HasSourceShape ? request.SourceHalfBrick : request.Shape == 1;
        byte slope = sourceTool && request.HasSourceShape ? request.SourceSlope :
            request.Shape >= 2 ? checked((byte)(request.Shape - 1)) : (byte)0;
        if (!after.TrySetSourceShape(half, slope))
            return Rejected(WorldTileMutationStatus.InvalidShape, in before);
        if (before.IsHalfBrick == after.IsHalfBrick && before.SourceSlope == after.SourceSlope)
            return Rejected(WorldTileMutationStatus.NoChange, in before);
        return CommitAndFrame(request.X, request.Y, in before, in after, tileFrame: true, wallFrame: false, preparation);
    }

    private WorldTileMutationResult CommitAndFrame(
        int x,
        int y,
        in WorldTile before,
        in WorldTile after,
        bool tileFrame,
        bool wallFrame,
        WorldTileMutationPreparation? preparation)
    {
        if (preparation is null)
        {
            _tiles.Set(x, y, in after);
            if (tileFrame)
                VanillaWorldLiquidWakeup1458.WakeCellAndNeighbours(_tiles, x, y);
        }
        else
        {
            preparation.Stage(x, y, in after);
            preparation.SetFraming(tileFrame, wallFrame);
        }
        int minX = Math.Max(0, x - 1);
        int maxX = Math.Min(_tiles.Dimensions.WidthTiles - 1, x + 1);
        int minY = Math.Max(0, y - 1);
        int maxY = Math.Min(_tiles.Dimensions.HeightTiles - 1, y + 1);
        int changed = 1;

        // Non-frame-important tile frames are not persisted by the vanilla world format. TerraRuntime therefore
        // canonicalizes the local 3x3 neighborhood to zero instead of inventing client sprite-frame arithmetic.
        // Wall framing is likewise represented by section dirtiness; there is no wall-frame field in WorldTile.
        if (tileFrame && preparation?.SourceFraming != true)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                for (int ty = minY; ty <= maxY; ty++)
                {
                    if (tx == x && ty == y)
                        continue;

                    WorldTile neighbor = preparation is null ? _tiles.Get(tx, ty) : preparation.ReadBefore(tx, ty);
                    if (!neighbor.IsActive ||
                        !VanillaTileDefinitionCatalog.TryGet(neighbor.TileType, out VanillaTileDefinition neighborDefinition) ||
                        neighborDefinition.IsFrameImportant ||
                        (neighbor.FrameX == 0 && neighbor.FrameY == 0))
                    {
                        continue;
                    }

                    neighbor.FrameX = 0;
                    neighbor.FrameY = 0;
                    if (preparation is null)
                        _tiles.Set(tx, ty, in neighbor);
                    else
                        preparation.Stage(tx, ty, in neighbor);
                    changed++;
                }
            }
        }

        if (wallFrame && preparation is null)
            MarkFrameSectionsDirty(minX, minY, maxX, maxY);

        return new WorldTileMutationResult(
            WorldTileMutationStatus.Applied,
            before,
            after,
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
