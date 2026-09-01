using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Structural safety validator for non-vanilla-completeness generators. It rejects malformed metadata, tile state,
/// liquid state and generated object footprints, but deliberately does not require canonical vanilla biome content
/// merely because the requested dimensions happen to match Small/Medium/Large Terraria sizes.
/// </summary>
internal static class RuntimeWorldGenerationStructuralValidator
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

    public static VanillaWorldValidationResult Validate(
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeWorldGenerationMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;

        if (width < 2 || height < 2)
            return new(VanillaWorldValidationStatus.InvalidDimensions, $"World dimensions {width}x{height} are below minimum 2x2.");
        if ((uint)metadata.Spawn.X >= (uint)width || (uint)metadata.Spawn.Y >= (uint)height)
            return new(VanillaWorldValidationStatus.InvalidSpawn, $"Spawn {metadata.Spawn} outside {width}x{height}.");
        if ((uint)metadata.Dungeon.X >= (uint)width || (uint)metadata.Dungeon.Y >= (uint)height)
            return new(VanillaWorldValidationStatus.InvalidDungeon, $"Dungeon {metadata.Dungeon} outside {width}x{height}.");
        if (metadata.Layers.WorldSurface <= 0 || metadata.Layers.WorldSurface >= height ||
            metadata.Layers.RockLayer <= metadata.Layers.WorldSurface || metadata.Layers.RockLayer >= height)
        {
            return new(VanillaWorldValidationStatus.InvalidLayers,
                $"Layers surface={metadata.Layers.WorldSurface} rock={metadata.Layers.RockLayer} invalid for height {height}.");
        }

        if (workspace.VanillaDungeonGraph is VanillaDungeonGraph1458 dungeonGraph)
        {
            VanillaWorldValidationResult graph =
                VanillaWorldGenerationValidator1458.ValidateDungeonGraph(dungeonGraph, width, height);
            if (!graph.IsValid)
                return graph;
        }

        if (metadata.VanillaBootstrapState is VanillaWorldGenerationBootstrapState1458 bootstrap)
        {
            if (bootstrap.LeftBeachEnd < 0 || bootstrap.LeftBeachEnd >= width ||
                bootstrap.RightBeachStart <= bootstrap.LeftBeachEnd || bootstrap.RightBeachStart > width)
            {
                return new(VanillaWorldValidationStatus.OceanBoundsViolation,
                    $"Beach bounds left={bootstrap.LeftBeachEnd} right={bootstrap.RightBeachStart} invalid for width {width}.");
            }
            if ((uint)bootstrap.DungeonLocation >= (uint)width)
                return new(VanillaWorldValidationStatus.OceanBoundsViolation,
                    $"DungeonLocation {bootstrap.DungeonLocation} outside width {width}.");
            if (bootstrap.SnowOriginLeft < 0 || bootstrap.SnowOriginRight > width ||
                bootstrap.SnowOriginLeft >= bootstrap.SnowOriginRight)
            {
                return new(VanillaWorldValidationStatus.BiomeMissing,
                    $"Snow band left={bootstrap.SnowOriginLeft} right={bootstrap.SnowOriginRight} invalid.");
            }
            if ((uint)bootstrap.JungleOriginX >= (uint)width)
                return new(VanillaWorldValidationStatus.BiomeMissing,
                    $"JungleOriginX {bootstrap.JungleOriginX} outside width {width}.");
        }

        WorldTileStore store = workspace.TileStore;
        int knownTileCount = VanillaTileIds.Count;
        int knownWallCount = VanillaWallIds.Count;
        var chestPositions = new HashSet<int>(workspace.GeneratedChestCount * 2);
        foreach (WorldChest chest in workspace.CaptureGeneratedChests())
        {
            int key = chest.X * 100000 + chest.Y;
            if (!chestPositions.Add(key))
                return new(VanillaWorldValidationStatus.DuplicateChest, $"Duplicate chest at ({chest.X},{chest.Y}).");
            if ((uint)chest.X >= (uint)(width - 1) || (uint)chest.Y >= (uint)(height - 1))
                return new(VanillaWorldValidationStatus.ObjectOutOfBounds, $"Chest at ({chest.X},{chest.Y}) exceeds {width}x{height}.");
            WorldTile anchor = store.Get(chest.X, chest.Y);
            if (!VanillaTileObjectAnchorCatalog.MatchesChestAnchor(in anchor))
            {
                return new(VanillaWorldValidationStatus.InvalidChestAnchor,
                    $"Chest anchor mismatch at ({chest.X},{chest.Y}) type={anchor.Type} flags={anchor.Flags} frame=({anchor.FrameX},{anchor.FrameY}).");
            }
            if (!IsValidChestFootprint(store, chest.X, chest.Y, width, height))
                return new(VanillaWorldValidationStatus.OrphanFrameImportantObject, $"Chest footprint corrupted at ({chest.X},{chest.Y}).");
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.Reserved != 0)
                    return new(VanillaWorldValidationStatus.ReservedNonZero, $"Reserved non-zero at ({x},{y})={tile.Reserved}.");
                if ((uint)tile.Type >= (uint)knownTileCount)
                    return new(VanillaWorldValidationStatus.InvalidTileType, $"Tile type {tile.Type} at ({x},{y}) >= {knownTileCount}.");
                if ((uint)tile.Wall >= (uint)knownWallCount)
                    return new(VanillaWorldValidationStatus.InvalidWallType, $"Wall type {tile.Wall} at ({x},{y}) >= {knownWallCount}.");
                if ((tile.Flags & ~KnownFlags) != 0)
                    return new(VanillaWorldValidationStatus.InvalidFlags, $"Unknown flags {tile.Flags} at ({x},{y}).");
                if (tile.Shape > 5)
                    return new(VanillaWorldValidationStatus.InvalidShape, $"Shape {tile.Shape} at ({x},{y}) >5.");
                if (!Enum.IsDefined(tile.LiquidKind))
                    return new(VanillaWorldValidationStatus.InvalidLiquid, $"LiquidKind {(byte)tile.LiquidKind} at ({x},{y}) undefined.");
                if (tile.LiquidAmount == 0 && tile.LiquidKind != WorldLiquidKind.Water)
                    return new(VanillaWorldValidationStatus.InvalidLiquid, $"Liquid 0 but kind {tile.LiquidKind} at ({x},{y}).");

                if (tile.IsActive)
                {
                    if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type) &&
                        !IsValidFrameImportantFootprint(store, x, y, width, height))
                    {
                        return new(VanillaWorldValidationStatus.OrphanFrameImportantObject,
                            $"Orphan frame-important tile type {tile.Type} at ({x},{y}) frame ({tile.FrameX},{tile.FrameY}).");
                    }
                }
                else if (tile.FrameX != 0 || tile.FrameY != 0 || tile.Shape != 0)
                {
                    return new(VanillaWorldValidationStatus.InvalidFlags,
                        $"Inactive tile at ({x},{y}) has non-zero frame/shape {tile.FrameX},{tile.FrameY} shape {tile.Shape}.");
                }
            }
        }

        WorldTile spawnTile = store.Get(metadata.Spawn.X, metadata.Spawn.Y);
        if (spawnTile.IsActive && IsSolidForSpawn(spawnTile.Type))
            return new(VanillaWorldValidationStatus.InvalidSpawn,
                $"Spawn tile itself is solid type {spawnTile.Type} at ({metadata.Spawn.X},{metadata.Spawn.Y}).");

        return new(VanillaWorldValidationStatus.Valid);
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
        if (containerType is not (21 or 467) || b.Type != containerType || c.Type != containerType || d.Type != containerType)
            return false;
        if (a.FrameX % 36 != 0 || a.FrameY % 36 != 0) return false;
        if (b.FrameX % 36 != 18 || b.FrameY % 36 != 0) return false;
        if (c.FrameX % 36 != 0 || c.FrameY % 36 != 18) return false;
        if (d.FrameX % 36 != 18 || d.FrameY % 36 != 18) return false;
        if ((a.FrameX / 36) != (c.FrameX / 36) || (b.FrameX / 36) != (d.FrameX / 36)) return false;
        if ((a.FrameY / 36) != (b.FrameY / 36) || (c.FrameY / 36) != (d.FrameY / 36)) return false;
        return true;
    }

    private static bool IsValidFrameImportantFootprint(WorldTileStore store, int x, int y, int width, int height)
    {
        WorldTile tile = store.Get(x, y);
        ushort type = tile.Type;
        if (type is 21 or 467)
        {
            int originX = x - (tile.FrameX / 18) % 2;
            int originY = y - (tile.FrameY / 18) % 2;
            return (uint)originX < (uint)(width - 1) &&
                   (uint)originY < (uint)(height - 1) &&
                   IsValidChestFootprint(store, originX, originY, width, height);
        }

        if (!VanillaMultiTileObjectCatalog.TryGet(new TileTypeId(type), out _))
            return true;
        if (tile.FrameX < 0 || tile.FrameY < 0 || tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0)
            return false;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                WorldTile neighbor = store.Get(nx, ny);
                if (neighbor.IsActive && neighbor.Type == type &&
                    neighbor.FrameX % 18 == 0 && neighbor.FrameY % 18 == 0)
                    return true;
            }
        }

        return true;
    }

    private static bool IsSolidForSpawn(ushort type)
    {
        if (VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type)))
            return true;
        return type is 0 or 1 or 2 or 53 or 147 or 60 or 59;
    }
}
