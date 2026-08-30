using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum VanillaMultiTileObjectPlacementSupportKind : byte
{
    Unsupported = 0,
    FullBottomRowSolidOrSolidTop = 1
}

public enum VanillaMultiTileObjectMutationStatus : byte
{
    Applied = 0,
    OutOfBounds = 1,
    UnknownObject = 2,
    UnsupportedPlacementRules = 3,
    Occupied = 4,
    MissingSupport = 5,
    InvalidObjectState = 6,
    MetadataRejected = 7,
    NotObject = 8
}

/// <summary>
/// Semantic description shared by the tile transaction and the metadata owner. Coordinates identify the
/// normalized top-left metadata anchor used by chests/signs/tile entities; OriginX/OriginY preserve the placement
/// origin from the source-backed object definition.
/// </summary>
public readonly record struct VanillaMultiTileObjectMutationDescriptor(
    VanillaMultiTileObjectDefinition Definition,
    WorldTileBounds Bounds,
    int OriginX,
    int OriginY)
{
    public int TopLeftX => Bounds.X;
    public int TopLeftY => Bounds.Y;
    public VanillaTileObjectMetadataKind MetadataKind => Definition.MetadataKind;
    public WorldTileEntityKind? TileEntityKind => Definition.TileEntityKind;
}

/// <summary>
/// Metadata side of an authoritative multi-tile transaction. CanCreate/CanRemove are side-effect-free preflights.
/// CommitCreate/CommitRemove run on the same single-writer thread after every tile/support check has succeeded and
/// must not throw. This lets capacity, non-empty chest, ownership and tile-entity policy veto the mutation before
/// any WorldTile is changed without coupling TerraRuntime.World to runtime-specific stores.
/// </summary>
public interface IVanillaMultiTileObjectMetadataLifecycle
{
    bool CanCreate(in VanillaMultiTileObjectMutationDescriptor descriptor);
    bool CanRemove(in VanillaMultiTileObjectMutationDescriptor descriptor);
    void CommitCreate(in VanillaMultiTileObjectMutationDescriptor descriptor);
    void CommitRemove(in VanillaMultiTileObjectMutationDescriptor descriptor);
}

public readonly record struct VanillaMultiTileObjectMutationResult(
    VanillaMultiTileObjectMutationStatus Status,
    VanillaMultiTileObjectMutationDescriptor Descriptor,
    int ChangedTiles)
{
    public bool Applied => Status == VanillaMultiTileObjectMutationStatus.Applied;
}

/// <summary>
/// Source-backed support policy for the first authoritative base-style placement slice. Container, Containers2 and
/// Dressers all use their complete bottom footprint; other catalogued objects remain fail-closed until their exact
/// TileObjectData anchor/liquid/alternate-placement rules are independently pinned.
/// </summary>
public static class VanillaMultiTileObjectPlacementSupportCatalog
{
    public static bool TryGet(
        TileTypeId type,
        out VanillaMultiTileObjectPlacementSupportKind support)
    {
        if (VanillaTileIds.IsChestAnchor(type))
        {
            support = VanillaMultiTileObjectPlacementSupportKind.FullBottomRowSolidOrSolidTop;
            return true;
        }

        support = VanillaMultiTileObjectPlacementSupportKind.Unsupported;
        return false;
    }
}

/// <summary>
/// Authoritative base-style multi-tile placement/break engine. Placement currently supports the verified floor-backed
/// container families; break supports any coherent object already described by VanillaMultiTileObjectCatalog.
/// Frame-important cells are committed as one preflighted transaction and metadata is vetoed/committed through the
/// lifecycle boundary so tile state cannot be knowingly separated from chest/sign/tile-entity identity.
/// </summary>
public sealed class VanillaMultiTileObjectMutationService
{
    public const short FrameCellSize = 18;

    private readonly WorldTileStore _tiles;

    public VanillaMultiTileObjectMutationService(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public VanillaMultiTileObjectMutationResult TryPlaceAtOrigin(
        TileTypeId type,
        int originX,
        int originY,
        IVanillaMultiTileObjectMetadataLifecycle metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (!VanillaMultiTileObjectCatalog.TryGet(type, out VanillaMultiTileObjectDefinition definition))
            return Rejected(VanillaMultiTileObjectMutationStatus.UnknownObject);
        if (!VanillaMultiTileObjectPlacementSupportCatalog.TryGet(type, out VanillaMultiTileObjectPlacementSupportKind support))
            return Rejected(VanillaMultiTileObjectMutationStatus.UnsupportedPlacementRules);

        long topLeftX64 = (long)originX - definition.PlacementOriginColumn;
        long topLeftY64 = (long)originY - definition.PlacementOriginRow;
        if (topLeftX64 < int.MinValue || topLeftX64 > int.MaxValue ||
            topLeftY64 < int.MinValue || topLeftY64 > int.MaxValue)
        {
            return Rejected(VanillaMultiTileObjectMutationStatus.OutOfBounds);
        }

        int topLeftX = (int)topLeftX64;
        int topLeftY = (int)topLeftY64;
        if (!TryCreateDescriptor(definition, topLeftX, topLeftY, out VanillaMultiTileObjectMutationDescriptor descriptor))
            return Rejected(VanillaMultiTileObjectMutationStatus.OutOfBounds);

        if (!FootprintIsEmpty(in descriptor))
            return Rejected(VanillaMultiTileObjectMutationStatus.Occupied, in descriptor);
        if (!HasRequiredSupport(in descriptor, support))
            return Rejected(VanillaMultiTileObjectMutationStatus.MissingSupport, in descriptor);
        if (!metadata.CanCreate(in descriptor))
            return Rejected(VanillaMultiTileObjectMutationStatus.MetadataRejected, in descriptor);

        // Metadata commits first after the complete preflight. The contract is non-throwing; following tile writes
        // are deterministic, in-bounds single-writer operations and therefore cannot fail through gameplay policy.
        metadata.CommitCreate(in descriptor);
        int changed = CommitPlacement(in descriptor);
        MarkFrameNeighborhoodDirty(in descriptor.Bounds);
        return new VanillaMultiTileObjectMutationResult(
            VanillaMultiTileObjectMutationStatus.Applied,
            descriptor,
            changed);
    }

    public VanillaMultiTileObjectMutationResult TryBreakAt(
        int tileX,
        int tileY,
        IVanillaMultiTileObjectMetadataLifecycle metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        VanillaMultiTileObjectMutationStatus resolved = TryResolveObjectAt(
            tileX,
            tileY,
            out VanillaMultiTileObjectMutationDescriptor descriptor);
        if (resolved != VanillaMultiTileObjectMutationStatus.Applied)
            return Rejected(resolved);
        if (!metadata.CanRemove(in descriptor))
            return Rejected(VanillaMultiTileObjectMutationStatus.MetadataRejected, in descriptor);

        metadata.CommitRemove(in descriptor);
        int changed = CommitBreak(in descriptor);
        MarkFrameNeighborhoodDirty(in descriptor.Bounds);
        return new VanillaMultiTileObjectMutationResult(
            VanillaMultiTileObjectMutationStatus.Applied,
            descriptor,
            changed);
    }

    /// <summary>
    /// Resolves an arbitrary cell of a coherent catalogued object to the top-left metadata anchor. Style offsets are
    /// accepted through modulo-width/height frame cells; every footprint cell is then verified before a descriptor is
    /// returned, preventing one corrupted frame from turning a partial structure into an authoritative object.
    /// </summary>
    public VanillaMultiTileObjectMutationStatus TryResolveObjectAt(
        int tileX,
        int tileY,
        out VanillaMultiTileObjectMutationDescriptor descriptor)
    {
        descriptor = default;
        if (!Contains(tileX, tileY))
            return VanillaMultiTileObjectMutationStatus.OutOfBounds;

        WorldTile clicked = _tiles.Get(tileX, tileY);
        if (!clicked.IsActive ||
            !VanillaMultiTileObjectCatalog.TryGet(clicked.TileType, out VanillaMultiTileObjectDefinition definition))
        {
            return VanillaMultiTileObjectMutationStatus.NotObject;
        }

        if (!TryResolveCellOffset(in clicked, definition, out int column, out int row))
            return VanillaMultiTileObjectMutationStatus.InvalidObjectState;

        int topLeftX = tileX - column;
        int topLeftY = tileY - row;
        if (!TryCreateDescriptor(definition, topLeftX, topLeftY, out descriptor))
            return VanillaMultiTileObjectMutationStatus.InvalidObjectState;

        for (int objectY = 0; objectY < definition.Height; objectY++)
        {
            for (int objectX = 0; objectX < definition.Width; objectX++)
            {
                WorldTile cell = _tiles.Get(topLeftX + objectX, topLeftY + objectY);
                if (!cell.IsActive ||
                    cell.TileType != definition.TileType ||
                    !TryResolveCellOffset(in cell, definition, out int actualX, out int actualY) ||
                    actualX != objectX ||
                    actualY != objectY)
                {
                    descriptor = default;
                    return VanillaMultiTileObjectMutationStatus.InvalidObjectState;
                }
            }
        }

        return VanillaMultiTileObjectMutationStatus.Applied;
    }

    private bool TryCreateDescriptor(
        VanillaMultiTileObjectDefinition definition,
        int topLeftX,
        int topLeftY,
        out VanillaMultiTileObjectMutationDescriptor descriptor)
    {
        if (topLeftX < 0 || topLeftY < 0 ||
            (long)topLeftX + definition.Width > _tiles.Dimensions.WidthTiles ||
            (long)topLeftY + definition.Height > _tiles.Dimensions.HeightTiles)
        {
            descriptor = default;
            return false;
        }

        int originX = checked(topLeftX + definition.PlacementOriginColumn);
        int originY = checked(topLeftY + definition.PlacementOriginRow);
        descriptor = new VanillaMultiTileObjectMutationDescriptor(
            definition,
            new WorldTileBounds(topLeftX, topLeftY, definition.Width, definition.Height),
            originX,
            originY);
        return true;
    }

    private bool FootprintIsEmpty(in VanillaMultiTileObjectMutationDescriptor descriptor)
    {
        for (int y = descriptor.Bounds.Y; y < descriptor.Bounds.ExclusiveBottom; y++)
        {
            for (int x = descriptor.Bounds.X; x < descriptor.Bounds.ExclusiveRight; x++)
            {
                if (_tiles.Get(x, y).IsActive)
                    return false;
            }
        }

        return true;
    }

    private bool HasRequiredSupport(
        in VanillaMultiTileObjectMutationDescriptor descriptor,
        VanillaMultiTileObjectPlacementSupportKind support)
    {
        if (support != VanillaMultiTileObjectPlacementSupportKind.FullBottomRowSolidOrSolidTop)
            return false;

        int supportY = descriptor.Bounds.ExclusiveBottom;
        if (supportY >= _tiles.Dimensions.HeightTiles)
            return false;

        for (int x = descriptor.Bounds.X; x < descriptor.Bounds.ExclusiveRight; x++)
        {
            WorldTile tile = _tiles.Get(x, supportY);
            if (!tile.IsActive || tile.IsActuated ||
                !VanillaTileDefinitionCatalog.TryGet(tile.TileType, out VanillaTileDefinition definition) ||
                (!definition.IsSolid && !definition.IsSolidTop))
            {
                return false;
            }
        }

        return true;
    }

    private int CommitPlacement(in VanillaMultiTileObjectMutationDescriptor descriptor)
    {
        VanillaMultiTileObjectDefinition definition = descriptor.Definition;
        int changed = 0;
        for (int objectY = 0; objectY < definition.Height; objectY++)
        {
            for (int objectX = 0; objectX < definition.Width; objectX++)
            {
                int x = descriptor.Bounds.X + objectX;
                int y = descriptor.Bounds.Y + objectY;
                WorldTile placed = _tiles.Get(x, y);
                if (!placed.TrySetTileType(definition.TileType))
                    throw new InvalidOperationException("Verified multi-tile object id no longer fits the runtime tile ABI.");

                placed.FrameX = checked((short)(objectX * FrameCellSize));
                placed.FrameY = checked((short)(objectY * FrameCellSize));
                placed.TileColor = 0;
                placed.Shape = 0;
                placed.Flags &= ~(
                    WorldTileFlags.Actuator |
                    WorldTileFlags.Inactive |
                    WorldTileFlags.InvisibleBlock |
                    WorldTileFlags.FullbrightBlock);
                placed.Flags |= WorldTileFlags.Active;
                _tiles.Set(x, y, in placed);
                changed++;
            }
        }

        return changed;
    }

    private int CommitBreak(in VanillaMultiTileObjectMutationDescriptor descriptor)
    {
        int changed = 0;
        for (int y = descriptor.Bounds.Y; y < descriptor.Bounds.ExclusiveBottom; y++)
        {
            for (int x = descriptor.Bounds.X; x < descriptor.Bounds.ExclusiveRight; x++)
            {
                WorldTile cleared = _tiles.Get(x, y);
                cleared.Type = 0;
                cleared.FrameX = 0;
                cleared.FrameY = 0;
                cleared.TileColor = 0;
                cleared.Shape = 0;
                cleared.Flags &= ~(
                    WorldTileFlags.Active |
                    WorldTileFlags.Actuator |
                    WorldTileFlags.Inactive |
                    WorldTileFlags.InvisibleBlock |
                    WorldTileFlags.FullbrightBlock);
                _tiles.Set(x, y, in cleared);
                changed++;
            }
        }

        return changed;
    }

    private void MarkFrameNeighborhoodDirty(in WorldTileBounds bounds)
    {
        int minX = Math.Max(0, bounds.X - 1);
        int minY = Math.Max(0, bounds.Y - 1);
        int maxX = Math.Min(_tiles.Dimensions.WidthTiles - 1, bounds.ExclusiveRight);
        int maxY = Math.Min(_tiles.Dimensions.HeightTiles - 1, bounds.ExclusiveBottom);
        WorldSectionId first = TerrariaSectionGeometry.FromTile(_tiles.Dimensions, minX, minY);
        WorldSectionId last = TerrariaSectionGeometry.FromTile(_tiles.Dimensions, maxX, maxY);

        for (int sectionY = first.Y; sectionY <= last.Y; sectionY++)
        {
            for (int sectionX = first.X; sectionX <= last.X; sectionX++)
                _tiles.DirtySections.MarkDirty(new WorldSectionId(sectionX, sectionY));
        }
    }

    private static bool TryResolveCellOffset(
        in WorldTile tile,
        VanillaMultiTileObjectDefinition definition,
        out int column,
        out int row)
    {
        column = 0;
        row = 0;
        if (tile.FrameX < 0 || tile.FrameY < 0 ||
            tile.FrameX % FrameCellSize != 0 ||
            tile.FrameY % FrameCellSize != 0)
        {
            return false;
        }

        int frameColumn = tile.FrameX / FrameCellSize;
        int frameRow = tile.FrameY / FrameCellSize;
        column = frameColumn % definition.Width;
        row = frameRow % definition.Height;
        return true;
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)_tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)_tiles.Dimensions.HeightTiles;

    private static VanillaMultiTileObjectMutationResult Rejected(VanillaMultiTileObjectMutationStatus status) =>
        new(status, default, 0);

    private static VanillaMultiTileObjectMutationResult Rejected(
        VanillaMultiTileObjectMutationStatus status,
        in VanillaMultiTileObjectMutationDescriptor descriptor) =>
        new(status, descriptor, 0);
}
