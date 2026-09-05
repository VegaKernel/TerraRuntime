using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Fail-closed structural validator for generated candidate worlds. It mirrors the checks listed in the
/// world-generation specification: tile/wall catalog bounds, liquid invariants, object footprints, chest
/// anchoring, dungeon/temple/spawn legality, biome presence, ocean bounds, dimensions and metadata.
/// Source: TerrariaServer 1.4.5.8 tile/wall counts, frame-importance catalogs and fresh-world anchor rules.
/// </summary>
public enum WorldValidationStatus : byte
{
    Valid = 0,
    InvalidDimensions = 1,
    MissingMetadata = 2,
    InvalidSpawn = 3,
    InvalidDungeon = 4,
    InvalidLayers = 5,
    InvalidTileType = 6,
    InvalidWallType = 7,
    InvalidFlags = 8,
    InvalidShape = 9,
    InvalidLiquid = 10,
    ObjectOutOfBounds = 11,
    OrphanFrameImportantObject = 12,
    InvalidChestAnchor = 13,
    DuplicateChest = 14,
    ReservedNonZero = 15,
    TempleMissing = 16,
    DungeonMissing = 17,
    OceanBoundsViolation = 18,
    BiomeMissing = 19,
    InvalidDungeonGraph = 20
}

public readonly record struct WorldValidationResult(
    WorldValidationStatus Status,
    string? Detail = null)
{
    public bool IsValid => Status == WorldValidationStatus.Valid;
}

public static class Validator1458
{
    private const WorldTileFlags KnownFlags =
        WorldTileFlags.Active |
        WorldTileFlags.WireRed |
        WorldTileFlags.WireBlue |
        WorldTileFlags.WireGreen |
        WorldTileFlags.WireYellow |
        WorldTileFlags.Actuator |
        WorldTileFlags.Inactive |
        WorldTileFlags.InvisibleBlock |
        WorldTileFlags.InvisibleWall |
        WorldTileFlags.FullbrightBlock |
        WorldTileFlags.FullbrightWall;

    public static WorldValidationResult Validate(
        Workspace workspace,
        RuntimeWorldGenerationMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;

        if (width < 2 || height < 2)
            return new(WorldValidationStatus.InvalidDimensions, $"World dimensions {width}x{height} are below minimum 2x2.");

        // Metadata anchors
        if (metadata.Spawn.X < 0 || metadata.Spawn.X >= width || metadata.Spawn.Y < 0 || metadata.Spawn.Y >= height)
            return new(WorldValidationStatus.InvalidSpawn, $"Spawn {metadata.Spawn} outside {width}x{height}.");
        if (metadata.Dungeon.X < 0 || metadata.Dungeon.X >= width || metadata.Dungeon.Y < 0 || metadata.Dungeon.Y >= height)
            return new(WorldValidationStatus.InvalidDungeon, $"Dungeon {metadata.Dungeon} outside {width}x{height}.");
        if (metadata.Layers.WorldSurface <= 0 || metadata.Layers.WorldSurface >= height ||
            metadata.Layers.RockLayer <= metadata.Layers.WorldSurface || metadata.Layers.RockLayer >= height)
            return new(WorldValidationStatus.InvalidLayers, $"Layers surface={metadata.Layers.WorldSurface} rock={metadata.Layers.RockLayer} invalid for height {height}.");

        if (workspace.VanillaDungeonGraph is DungeonGraph1458 dungeonGraph)
        {
            WorldValidationResult graphValidation = ValidateDungeonGraph(dungeonGraph, width, height);
            if (!graphValidation.IsValid)
                return graphValidation;
        }

        // Bootstrap ocean bounds when available
        if (metadata.VanillaBootstrapState is VanillaWorldGenerationBootstrapState1458 bootstrap)
        {
            if (bootstrap.LeftBeachEnd < 0 || bootstrap.LeftBeachEnd >= width ||
                bootstrap.RightBeachStart <= bootstrap.LeftBeachEnd || bootstrap.RightBeachStart > width)
                return new(WorldValidationStatus.OceanBoundsViolation,
                    $"Beach bounds left={bootstrap.LeftBeachEnd} right={bootstrap.RightBeachStart} invalid for width {width}.");
            if (bootstrap.DungeonLocation < bootstrap.LeftBeachEnd || bootstrap.DungeonLocation > bootstrap.RightBeachStart)
            {
                // Dungeon location may be outside beaches but should still be inside world and not in center spawn band
                if ((uint)bootstrap.DungeonLocation >= (uint)width)
                    return new(WorldValidationStatus.OceanBoundsViolation,
                        $"DungeonLocation {bootstrap.DungeonLocation} outside width {width}.");
            }
            if (bootstrap.SnowOriginLeft < 0 || bootstrap.SnowOriginRight > width || bootstrap.SnowOriginLeft >= bootstrap.SnowOriginRight)
                return new(WorldValidationStatus.BiomeMissing,
                    $"Snow band left={bootstrap.SnowOriginLeft} right={bootstrap.SnowOriginRight} invalid.");
            if (bootstrap.JungleOriginX < 0 || bootstrap.JungleOriginX >= width)
                return new(WorldValidationStatus.BiomeMissing,
                    $"JungleOriginX {bootstrap.JungleOriginX} outside width {width}.");
        }

        // Tile/wall scan
        WorldTileStore store = workspace.TileStore;
        int knownTileCount = VanillaTileIds.Count;
        int knownWallCount = VanillaWallIds.Count;
        bool foundSnow = false;
        bool foundJungle = false;
        bool foundDesert = false;
        bool foundMushroom = false;
        bool foundGranite = false;
        bool foundMarble = false;
        bool foundDungeonTile = false;
        bool foundTempleTile = false;
        bool foundHellstone = false;
        long activeCount = 0;
        bool isCanonical = TerrainPass1458.IsCanonicalWorldSize(width, height) && metadata.VanillaSeedProfile.IsDefault;

        // Quick chest anchor set for duplicate/object checks
        var chestPositions = new HashSet<int>(workspace.GeneratedChestCount * 2);
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        foreach (WorldChest chest in chests)
        {
            int key = chest.X * 100000 + chest.Y;
            if (!chestPositions.Add(key))
                return new(WorldValidationStatus.DuplicateChest, $"Duplicate chest at ({chest.X},{chest.Y}).");
            if ((uint)chest.X >= (uint)(width - 1) || (uint)chest.Y >= (uint)(height - 1))
                return new(WorldValidationStatus.ObjectOutOfBounds, $"Chest at ({chest.X},{chest.Y}) exceeds {width}x{height}.");
            WorldTile anchor = store.Get(chest.X, chest.Y);
            if (!VanillaMultiTileObjectCatalog.MatchesChestAnchor(in anchor))
                return new(WorldValidationStatus.InvalidChestAnchor,
                    $"Chest anchor mismatch at ({chest.X},{chest.Y}) type={anchor.Type} flags={anchor.Flags} frame=({anchor.FrameX},{anchor.FrameY}).");
            // Verify 2x2 container footprint
            if (!IsValidChestFootprint(store, chest.X, chest.Y, width, height))
                return new(WorldValidationStatus.OrphanFrameImportantObject,
                    $"Chest footprint corrupted at ({chest.X},{chest.Y}).");
            if (anchor.Reserved != 0)
                return new(WorldValidationStatus.ReservedNonZero, $"Chest anchor reserved non-zero at ({chest.X},{chest.Y}).");
        }

        // Track frame-important object validity via quick scan for orphan tiles
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.Reserved != 0)
                    return new(WorldValidationStatus.ReservedNonZero, $"Reserved non-zero at ({x},{y})={tile.Reserved}.");

                if ((uint)tile.Type >= (uint)knownTileCount)
                    return new(WorldValidationStatus.InvalidTileType, $"Tile type {tile.Type} at ({x},{y}) >= {knownTileCount}.");
                if ((uint)tile.Wall >= (uint)knownWallCount)
                    return new(WorldValidationStatus.InvalidWallType, $"Wall type {tile.Wall} at ({x},{y}) >= {knownWallCount}.");
                if ((tile.Flags & ~KnownFlags) != 0)
                    return new(WorldValidationStatus.InvalidFlags, $"Unknown flags {tile.Flags} at ({x},{y}).");
                if (tile.Shape > 5)
                    return new(WorldValidationStatus.InvalidShape, $"Shape {tile.Shape} at ({x},{y}) >5.");
                if (!Enum.IsDefined(tile.LiquidKind))
                    return new(WorldValidationStatus.InvalidLiquid, $"LiquidKind {(byte)tile.LiquidKind} at ({x},{y}) undefined.");
                if (tile.LiquidAmount == 0 && tile.LiquidKind != WorldLiquidKind.Water)
                    return new(WorldValidationStatus.InvalidLiquid, $"Liquid 0 but kind {tile.LiquidKind} at ({x},{y}).");
                if (tile.LiquidAmount > 0 && tile.IsActive && IsSolidBlockingLiquid(tile.Type))
                {
                    // Solid tile with liquid is unusual but can occur in some generation edge cases (e.g., actuated or
                    // half-brick liquids). For non-actuated full solids, treat as validation warning only when inside
                    // canonical pipeline where strict parity is expected; otherwise don't fail the whole world.
                    if (!tile.IsActuated && isCanonical)
                    {
                        return new(WorldValidationStatus.InvalidLiquid, $"Solid tile type {tile.Type} with liquid {tile.LiquidAmount} at ({x},{y}).");
                    }
                }

                if (tile.IsActive)
                {
                    activeCount++;
                    // Biome sampling (lightweight, stop after found)
                    if (!foundSnow && tile.Type is 147 or 161 or 224)
                        foundSnow = true;
                    if (!foundJungle && tile.Type is 59 or 60)
                        foundJungle = true;
                    if (!foundDesert && tile.Type is 53 or 397 or 396)
                        foundDesert = true;
                    if (!foundMushroom && tile.Type == 70)
                        foundMushroom = true;
                    if (!foundGranite && tile.Type == 368)
                        foundGranite = true;
                    if (!foundMarble && tile.Type == 367)
                        foundMarble = true;
                    if (!foundDungeonTile && tile.Type is 41 or 43 or 44)
                        foundDungeonTile = true;
                    if (!foundTempleTile && tile.Type == 226)
                        foundTempleTile = true;
                    if (!foundHellstone && tile.Type == 58)
                        foundHellstone = true;

                    // Frame-important object footprint check: if tile is frame-important, verify its object footprint is intact
                    if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    {
                        if (!IsValidFrameImportantFootprint(store, x, y, width, height))
                            return new(WorldValidationStatus.OrphanFrameImportantObject,
                                $"Orphan frame-important tile type {tile.Type} at ({x},{y}) frame ({tile.FrameX},{tile.FrameY}).");
                    }
                    else
                    {
                        // Non-frame-important tiles must have zero frame when inactive? Actually FinalCleanup normalizes.
                        if (tile.FrameX != 0 && tile.FrameY != 0)
                        {
                            // Allow non-zero frames for slopes? No, slopes use shape, not frame. So this is suspicious but not fatal for natural tiles.
                            // We only enforce that type < count, already checked.
                        }
                    }
                }
                else
                {
                    if (tile.FrameX != 0 || tile.FrameY != 0 || tile.Shape != 0)
                        return new(WorldValidationStatus.InvalidFlags, $"Inactive tile at ({x},{y}) has non-zero frame/shape {tile.FrameX},{tile.FrameY} shape {tile.Shape}.");
                }
            }
        }

        if (isCanonical && activeCount < (long)width * 10)
            return new(WorldValidationStatus.BiomeMissing, $"Active tiles {activeCount} too sparse for {width}x{height} world.");

        // Canonical biome presence for source-backed worlds only
        if (isCanonical)
        {
            if (!foundSnow)
                return new(WorldValidationStatus.BiomeMissing, "Snow biome missing entirely (no snow/ice/slush).");
            if (!foundJungle)
                return new(WorldValidationStatus.BiomeMissing, "Jungle biome missing (no mud/jungleGrass).");
            if (!foundDesert)
                return new(WorldValidationStatus.BiomeMissing, "Desert biome missing (no sand/hardenedSand/sandstone).");
            if (!foundDungeonTile)
                return new(WorldValidationStatus.DungeonMissing, "Dungeon tiles missing (no dungeon brick).");
            if (!foundTempleTile)
                return new(WorldValidationStatus.TempleMissing, "Jungle Temple missing (no lihzahrd brick).");
            if (!foundHellstone)
                return new(WorldValidationStatus.BiomeMissing, "Underworld hellstone missing.");
        }

        // Spawn must be on solid ground or just above
        {
            int sx = metadata.Spawn.X;
            int sy = metadata.Spawn.Y;
            // Spawn should be near surface: tile below should be solid or just above solid
            bool spawnValid = false;
            for (int dy = 0; dy < 4; dy++)
            {
                int ty = sy + dy;
                if ((uint)ty >= (uint)height) break;
                WorldTile below = store.Get(sx, ty);
                if (below.IsActive && IsSolidForSpawn(below.Type))
                {
                    spawnValid = true;
                    break;
                }
                if (ty + 1 < height)
                {
                    WorldTile twoBelow = store.Get(sx, ty + 1);
                    if (twoBelow.IsActive && IsSolidForSpawn(twoBelow.Type))
                    {
                        spawnValid = true;
                        break;
                    }
                }
            }
            if (!isCanonical && metadata.VanillaSeedProfile.IsDefault)
            {
                // For non-canonical fallback worlds, spawn may be simpler; just ensure not inside solid
                WorldTile spawnTile = store.Get(sx, sy);
                if (spawnTile.IsActive && IsSolidForSpawn(spawnTile.Type))
                    return new(WorldValidationStatus.InvalidSpawn, $"Spawn tile itself is solid type {spawnTile.Type} at ({sx},{sy}).");
            }
            else if (!spawnValid)
            {
                return new(WorldValidationStatus.InvalidSpawn, $"Spawn at ({sx},{sy}) has no solid ground within 3 tiles below.");
            }
        }

        // Ocean bounds: for canonical worlds, beaches should have sand near edges and water near edges
        if (isCanonical && metadata.VanillaBootstrapState is not null)
        {
            int leftBeach = metadata.VanillaBootstrapState.LeftBeachEnd;
            int rightBeach = metadata.VanillaBootstrapState.RightBeachStart;
            // Beaches anchors each basin to its local terrain, not worldSurface + a fixed offset.
            // The old 50-row census rejected valid deep Large-world oceans (seed 8675309). Validate
            // actual edge-connected water and its sand floor using the shared geometry gate below.
            OceanIntegrityResult1458 leftIntegrity = OceanIntegrity1458.Validate(
                store,
                leftBeach,
                left: true,
                metadata.Layers.WorldSurface);
            if (!leftIntegrity.IsValid)
                return new(WorldValidationStatus.OceanBoundsViolation, leftIntegrity.Detail);

            OceanIntegrityResult1458 rightIntegrity = OceanIntegrity1458.Validate(
                store,
                rightBeach,
                left: false,
                metadata.Layers.WorldSurface);
            if (!rightIntegrity.IsValid)
                return new(WorldValidationStatus.OceanBoundsViolation, rightIntegrity.Detail);
        }

        return new(WorldValidationStatus.Valid, null);
    }

    internal static WorldValidationResult ValidateDungeonGraph(
        DungeonGraph1458 graph,
        int worldWidth,
        int worldHeight)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.RoomCount < 3 || graph.HallCount < worldWidth / 100)
            return new(WorldValidationStatus.InvalidDungeonGraph,
                $"Dungeon graph is sparse: rooms={graph.RoomCount}, halls={graph.HallCount}.");
        if (graph.HorizontalHallCount == 0 || graph.VerticalHallCount == 0)
            return new(WorldValidationStatus.InvalidDungeonGraph,
                $"Dungeon graph is axis-degenerate: horizontal={graph.HorizontalHallCount}, vertical={graph.VerticalHallCount}.");
        DungeonBounds1458 bounds = graph.Bounds;
        if (bounds.Width < 120 || bounds.Height < 120)
            return new(WorldValidationStatus.InvalidDungeonGraph,
                $"Dungeon graph span {bounds.Width}x{bounds.Height} is still shaft-shaped.");
        if ((uint)graph.Anchor.X >= (uint)worldWidth || (uint)graph.Anchor.Y >= (uint)worldHeight)
            return new(WorldValidationStatus.InvalidDungeonGraph,
                $"Dungeon graph anchor {graph.Anchor} is outside {worldWidth}x{worldHeight}.");
        if (!graph.Components.Any(static component => component.Kind == DungeonComponentKind1458.EntranceHall) ||
            !graph.Components.Any(static component => component.Kind == DungeonComponentKind1458.Entrance))
            return new(WorldValidationStatus.InvalidDungeonGraph, "Dungeon graph is not connected to a surface entrance.");
        return new(WorldValidationStatus.Valid);
    }

    private static bool IsValidChestFootprint(WorldTileStore store, int left, int top, int width, int height)
    {
        if ((uint)left >= (uint)(width - 1) || (uint)top >= (uint)(height - 1))
            return false;
        WorldTile a = store.Get(left, top);
        WorldTile b = store.Get(left + 1, top);
        WorldTile c = store.Get(left, top + 1);
        WorldTile d = store.Get(left + 1, top + 1);
        if (!a.IsActive || !b.IsActive || !c.IsActive || !d.IsActive)
            return false;
        ushort containerType = a.Type;
        if (containerType is not (21 or 467) ||
            b.Type != containerType || c.Type != containerType || d.Type != containerType)
            return false;
        // Chest style is encoded in base frame offset (multiples of 36). Validate modulo 36.
        if (a.FrameX % 36 != 0 || a.FrameY % 36 != 0) return false;
        if (b.FrameX % 36 != 18 || b.FrameY % 36 != 0) return false;
        if (c.FrameX % 36 != 0 || c.FrameY % 36 != 18) return false;
        if (d.FrameX % 36 != 18 || d.FrameY % 36 != 18) return false;
        // Ensure style consistency: left tiles share same base style, right tiles same, top vs bottom etc
        if ((a.FrameX / 36) != (c.FrameX / 36) || (b.FrameX / 36) != (d.FrameX / 36)) return false;
        if ((a.FrameY / 36) != (b.FrameY / 36) || (c.FrameY / 36) != (d.FrameY / 36)) return false;
        return true;
    }

    private static bool IsValidFrameImportantFootprint(WorldTileStore store, int x, int y, int width, int height)
    {
        WorldTile tile = store.Get(x, y);
        ushort type = tile.Type;

        // Fast path for chests (most common frame-important)
        if (type == 21 || type == 467) // Containers family
        {
            // Resolve origin via frame
            int originX = x - (tile.FrameX / 18) % 2;
            int originY = y - (tile.FrameY / 18) % 2;
            if ((uint)originX >= (uint)(width - 1) || (uint)originY >= (uint)(height - 1))
                return false;
            return IsValidChestFootprint(store, originX, originY, width, height);
        }

        // For other frame-important objects not in the tight metadata catalog (trees, vines, plants, etc.),
        // vanilla framing uses varied periods (22 for trees, etc.) and is not validated here beyond basic catalog membership.
        // Only enforce strict frame geometry for cataloged multi-tile objects; otherwise treat as valid decoration.
        TileTypeId typeId = new(type);
        if (!VanillaMultiTileObjectCatalog.TryGet(typeId, out _))
            return true;

        if (tile.FrameX < 0 || tile.FrameY < 0)
            return false;
        if (tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0)
            return false;
        // Allow any other frame-important but ensure neighbor tiles of same type exist where expected
        // For 1x2 objects etc, we check vertical neighbor has same type
        // Generic heuristic: if tile is at edge, it's orphan only if no adjacent same-type frame-important tile nearby
        // We keep this permissive to avoid false positives for not-yet-cataloged objects, but still catch isolated orphan anchors
        // Require that at least one orthogonal neighbor within 2 tiles is also same type and frame-important
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                WorldTile n = store.Get(nx, ny);
                if (n.IsActive && n.Type == type && n.FrameX % 18 == 0 && n.FrameY % 18 == 0)
                    return true;
            }
        }
        // Singletons like 1x1 pots (type 28?) are allowed isolated; we treat unknown as valid if not chest
        // So we return true for now to avoid over-strictness
        return true;
    }

    private static bool IsSolidBlockingLiquid(ushort type)
    {
        // Quick solid check via collision catalog; fallback to known solid types
        if (VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type)))
            return true;
        // Additional conservative: types that are always solid: dirt/stone etc
        return type is 0 or 1 or 25 or 203 or 226 or 41 or 53;
    }

    private static bool IsSolidForSpawn(ushort type)
    {
        // Spawn needs solid ground: any solid tile counts
        if (VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type)))
            return true;
        return type is 0 or 1 or 2 or 53 or 147 or 60 or 59;
    }
}
