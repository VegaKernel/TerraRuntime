using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum VanillaTileObjectMetadataKind : byte
{
    Chest = 1,
    Sign = 2,
    TileEntity = 3
}

/// <summary>
/// Source-backed base-style geometry for one TerrariaServer 1.4.5.8 multi-tile object used by section metadata.
/// Alternate placement styles can change the player-facing origin, but not this object's width and height.
/// </summary>
public readonly record struct VanillaMultiTileObjectDefinition(
    TileTypeId TileType,
    byte Width,
    byte Height,
    byte PlacementOriginColumn,
    byte PlacementOriginRow,
    short FrameXPeriod,
    short FrameYPeriod,
    bool RequireFrameYZero,
    VanillaTileObjectMetadataKind MetadataKind,
    WorldTileEntityKind? TileEntityKind = null)
{
    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        PlacementOriginColumn < Width &&
        PlacementOriginRow < Height &&
        FrameXPeriod > 0 &&
        (RequireFrameYZero || FrameYPeriod > 0) &&
        Enum.IsDefined(MetadataKind) &&
        (MetadataKind == VanillaTileObjectMetadataKind.TileEntity) == TileEntityKind.HasValue &&
        (!TileEntityKind.HasValue || Enum.IsDefined(TileEntityKind.GetValueOrDefault()));

    public bool MatchesAnchor(in WorldTile tile)
    {
        if (!IsValid || !tile.IsActive || tile.TileType != TileType)
            return false;

        if (tile.FrameX % FrameXPeriod != 0)
            return false;

        return RequireFrameYZero
            ? tile.FrameY == 0
            : tile.FrameY % FrameYPeriod == 0;
    }
}

/// <summary>
/// Sparse typed TileObjectData catalog for every vanilla multi-tile object currently materialized into section
/// metadata. Missing types remain unsupported rather than receiving inferred geometry.
/// </summary>
public static class VanillaMultiTileObjectCatalog
{
    private static readonly VanillaMultiTileObjectDefinition[] Definitions =
    [
        Chest(VanillaTileIds.Containers, width: 2, height: 2, originColumn: 0, originRow: 1),
        Chest(VanillaTileIds.Containers2, width: 2, height: 2, originColumn: 0, originRow: 1),
        Chest(VanillaTileIds.Dressers, width: 3, height: 2, originColumn: 1, originRow: 1),
        Sign(VanillaTileIds.Signs),
        Sign(VanillaTileIds.Tombstones),
        Sign(VanillaTileIds.AnnouncementBox),
        Sign(VanillaTileIds.TatteredWoodSign),
        TileEntity(VanillaTileIds.TargetDummy, 2, 3, 1, 2, WorldTileEntityKind.TrainingDummy),
        TileEntity(VanillaTileIds.ItemFrame, 2, 2, 0, 1, WorldTileEntityKind.ItemFrame),
        TileEntity(VanillaTileIds.DeadCellsDisplayJar, 1, 2, 0, 0, WorldTileEntityKind.DeadCellsDisplayJar),
        TileEntity(VanillaTileIds.FoodPlatter, 1, 1, 0, 0, WorldTileEntityKind.FoodPlatter),
        TileEntity(VanillaTileIds.WeaponsRack2, 3, 3, 1, 1, WorldTileEntityKind.WeaponsRack),
        TileEntity(VanillaTileIds.DisplayDoll, 2, 3, 0, 2, WorldTileEntityKind.DisplayDoll),
        TileEntity(VanillaTileIds.HatRack, 3, 4, 1, 3, WorldTileEntityKind.HatRack),
        new VanillaMultiTileObjectDefinition(
            VanillaTileIds.TeleportationPylon,
            Width: 3,
            Height: 4,
            PlacementOriginColumn: 1,
            PlacementOriginRow: 3,
            FrameXPeriod: 54,
            FrameYPeriod: 72,
            RequireFrameYZero: false,
            VanillaTileObjectMetadataKind.TileEntity,
            WorldTileEntityKind.TeleportationPylon)
    ];

    public static ReadOnlySpan<VanillaMultiTileObjectDefinition> All => Definitions;

    public static bool TryGet(TileTypeId type, out VanillaMultiTileObjectDefinition definition)
    {
        foreach (VanillaMultiTileObjectDefinition candidate in Definitions)
        {
            if (candidate.TileType == type)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool TryGet(
        WorldTileEntityKind kind,
        out VanillaMultiTileObjectDefinition definition)
    {
        foreach (VanillaMultiTileObjectDefinition candidate in Definitions)
        {
            if (candidate.MetadataKind == VanillaTileObjectMetadataKind.TileEntity &&
                candidate.TileEntityKind.GetValueOrDefault() == kind)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    private const int FrameCoordinateUnit = 18;

    public static bool MatchesChestAnchor(in WorldTile tile) =>
        MatchesAnchor(tile, VanillaTileObjectMetadataKind.Chest);

    public static bool MatchesSignAnchor(in WorldTile tile) =>
        MatchesAnchor(tile, VanillaTileObjectMetadataKind.Sign);

    public static bool TryResolveSignOriginOffset(in WorldTile tile, out int offsetX, out int offsetY) =>
        TryResolveOriginOffset(tile, VanillaTileObjectMetadataKind.Sign, out offsetX, out offsetY);

    public static bool MatchesTileEntityAnchor(WorldTileEntityKind kind, in WorldTile tile) =>
        TryGet(kind, out VanillaMultiTileObjectDefinition definition) && definition.MatchesAnchor(tile);

    private static bool MatchesAnchor(in WorldTile tile, VanillaTileObjectMetadataKind metadataKind) =>
        TryGet(tile.TileType, out VanillaMultiTileObjectDefinition definition) &&
        definition.MetadataKind == metadataKind &&
        definition.MatchesAnchor(tile);

    private static bool TryResolveOriginOffset(
        in WorldTile tile,
        VanillaTileObjectMetadataKind metadataKind,
        out int offsetX,
        out int offsetY)
    {
        if (tile.FrameX < 0 ||
            tile.FrameY < 0 ||
            !TryGet(tile.TileType, out VanillaMultiTileObjectDefinition definition) ||
            definition.MetadataKind != metadataKind)
        {
            offsetX = 0;
            offsetY = 0;
            return false;
        }

        offsetX = (tile.FrameX / FrameCoordinateUnit) % definition.Width;
        // Sign.ReadSign removes the complete vertical frame row; unlike the horizontal coordinate it does not
        // apply a modulo. Keeping this source-backed asymmetry here prevents packet handlers from reimplementing it.
        offsetY = tile.FrameY / FrameCoordinateUnit;
        return true;
    }

    private static VanillaMultiTileObjectDefinition Chest(
        TileTypeId type,
        byte width,
        byte height,
        byte originColumn,
        byte originRow) =>
        new(
            type,
            width,
            height,
            originColumn,
            originRow,
            FrameXPeriod: checked((short)(width * 18)),
            FrameYPeriod: checked((short)(height * 18)),
            RequireFrameYZero: false,
            VanillaTileObjectMetadataKind.Chest);

    private static VanillaMultiTileObjectDefinition Sign(TileTypeId type) =>
        new(
            type,
            Width: 2,
            Height: 2,
            PlacementOriginColumn: 0,
            PlacementOriginRow: 1,
            FrameXPeriod: 36,
            FrameYPeriod: 36,
            RequireFrameYZero: false,
            VanillaTileObjectMetadataKind.Sign);

    private static VanillaMultiTileObjectDefinition TileEntity(
        TileTypeId type,
        byte width,
        byte height,
        byte originColumn,
        byte originRow,
        WorldTileEntityKind kind) =>
        new(
            type,
            width,
            height,
            originColumn,
            originRow,
            FrameXPeriod: checked((short)(width * 18)),
            FrameYPeriod: 0,
            RequireFrameYZero: true,
            VanillaTileObjectMetadataKind.TileEntity,
            kind);
}
