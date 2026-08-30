using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed frame-anchor rule used when the runtime identifies a persisted/section object from a tile.
/// This is intentionally narrower than a complete TileObjectData replacement: dimensions, placement support,
/// style calculation and mutation semantics stay separate until those contracts are independently verified.
/// </summary>
public readonly record struct VanillaTileObjectAnchorDefinition(
    TileTypeId TileType,
    short FrameXPeriod,
    short FrameYPeriod,
    bool RequireFrameYZero)
{
    public bool IsValid =>
        FrameXPeriod > 0 &&
        (RequireFrameYZero || FrameYPeriod > 0);

    public bool Matches(in WorldTile tile)
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
/// TerrariaServer 1.4.5.8 section-object anchor facts currently consumed by chest, sign and tile-entity
/// metadata discovery. Frame periods live here so unrelated encoders do not duplicate vanilla frame arithmetic.
/// </summary>
public static class VanillaTileObjectAnchorCatalog
{
    private static readonly VanillaTileObjectAnchorDefinition[] ChestAnchors =
    [
        new(VanillaTileIds.Containers, 36, 36, RequireFrameYZero: false),
        new(VanillaTileIds.Containers2, 36, 36, RequireFrameYZero: false),
        new(VanillaTileIds.Dressers, 54, 36, RequireFrameYZero: false)
    ];

    private static readonly VanillaTileObjectAnchorDefinition[] SignAnchors =
    [
        new(VanillaTileIds.Signs, 36, 36, RequireFrameYZero: false),
        new(VanillaTileIds.Tombstones, 36, 36, RequireFrameYZero: false),
        new(VanillaTileIds.AnnouncementBox, 36, 36, RequireFrameYZero: false),
        new(VanillaTileIds.TatteredWoodSign, 36, 36, RequireFrameYZero: false)
    ];

    private static readonly VanillaTileObjectAnchorDefinition TrainingDummyAnchor =
        new(VanillaTileIds.TargetDummy, 36, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition ItemFrameAnchor =
        new(VanillaTileIds.ItemFrame, 36, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition DeadCellsDisplayJarAnchor =
        new(VanillaTileIds.DeadCellsDisplayJar, 18, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition FoodPlatterAnchor =
        new(VanillaTileIds.FoodPlatter, 18, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition WeaponsRackAnchor =
        new(VanillaTileIds.WeaponsRack2, 54, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition DisplayDollAnchor =
        new(VanillaTileIds.DisplayDoll, 36, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition HatRackAnchor =
        new(VanillaTileIds.HatRack, 54, 0, RequireFrameYZero: true);

    private static readonly VanillaTileObjectAnchorDefinition TeleportationPylonAnchor =
        new(VanillaTileIds.TeleportationPylon, 54, 72, RequireFrameYZero: false);

    public static bool MatchesChestAnchor(in WorldTile tile) => MatchesAny(ChestAnchors, tile);

    public static bool MatchesSignAnchor(in WorldTile tile) => MatchesAny(SignAnchors, tile);

    public static bool MatchesTileEntityAnchor(WorldTileEntityKind kind, in WorldTile tile) =>
        TryGetTileEntityAnchorDefinition(kind, out VanillaTileObjectAnchorDefinition definition) &&
        definition.Matches(tile);

    public static bool TryGetTileEntityAnchorDefinition(
        WorldTileEntityKind kind,
        out VanillaTileObjectAnchorDefinition definition)
    {
        definition = kind switch
        {
            WorldTileEntityKind.TrainingDummy => TrainingDummyAnchor,
            WorldTileEntityKind.ItemFrame => ItemFrameAnchor,
            WorldTileEntityKind.DeadCellsDisplayJar => DeadCellsDisplayJarAnchor,
            WorldTileEntityKind.FoodPlatter => FoodPlatterAnchor,
            WorldTileEntityKind.WeaponsRack => WeaponsRackAnchor,
            WorldTileEntityKind.DisplayDoll => DisplayDollAnchor,
            WorldTileEntityKind.HatRack => HatRackAnchor,
            WorldTileEntityKind.TeleportationPylon => TeleportationPylonAnchor,
            _ => default
        };

        return definition.IsValid;
    }

    private static bool MatchesAny(
        ReadOnlySpan<VanillaTileObjectAnchorDefinition> definitions,
        in WorldTile tile)
    {
        foreach (VanillaTileObjectAnchorDefinition definition in definitions)
        {
            if (definition.Matches(tile))
                return true;
        }

        return false;
    }
}
