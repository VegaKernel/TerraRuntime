using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal enum TileShape1458 : byte
{
    Full = 0,
    HalfBrick = 1,
    SlopeDownRight = 2,
    SlopeDownLeft = 3,
    SlopeUpRight = 4,
    SlopeUpLeft = 5
}

[Flags]
internal enum SlopeNeighborMask1458 : byte
{
    None = 0,
    SolidRight = 1 << 0,
    SolidLeft = 1 << 1,
    SolidBelow = 1 << 2,
    OccupiedAbove = 1 << 3
}

internal readonly record struct WorldSmoothingResult1458(
    long SlopedTiles,
    long HalfBricks,
    long RemovedTiles,
    long FilledTiles);

/// <summary>
/// Clean-room TerrariaServer 1.4.5.8 <c>WorldGen</c> Smooth World implementation for ordinary generation. It owns
/// both ordered topology scans, exact shared-RNG decision points, sand normalization and orphan-slope correction.
/// </summary>
internal static class WorldSmoother1458
{
    private const int Border = 20;

    public static WorldSmoothingResult1458 Apply(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);

        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(481), true);
            workspace.SetVanillaGenerationSolid(new TileTypeId(482), true);
            workspace.SetVanillaGenerationSolid(new TileTypeId(483), true);
        }
        var grid = new Grid(workspace.TileStore, new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true), workspace);
        long sloped = 0;
        long halfBricks = 0;
        long removed = 0;
        long filled = 0;

        for (int x = Border; x < grid.Width - Border; x++)
        {
            if ((x & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            for (int y = Border; y < grid.Height - Border; y++)
            {
                ApplyTopologyCell(grid, random, x, y, ref sloped, ref halfBricks, ref removed, ref filled);
            }

        }

        for (int x = Border; x < grid.Width - Border; x++)
        {
            if ((x & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            for (int y = Border; y < grid.Height - Border; y++)
                ApplyFinishCell(grid, random, x, y, ref sloped, ref halfBricks);
        }

        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(137), true);
        }
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(190), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(192), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(481), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(482), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(483), false);
        }
        return new WorldSmoothingResult1458(sloped, halfBricks, removed, filled);
    }

    private static void ApplyTopologyCell(
        Grid grid,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        ref long sloped,
        ref long halfBricks,
        ref long removed,
        ref long filled)
    {
        ref WorldTile tile = ref grid.At(x, y);
        WorldTile above = grid.At(x, y - 1);
        if ((tile.IsActive && WorldSmoothingCatalog1458.PreventsSlopesDuringGeneration(tile.TileType)) ||
            (above.IsActive && WorldSmoothingCatalog1458.PreventsSlopesDuringGeneration(above.TileType)))
            return;

        bool topOpen = !above.IsActive;
        bool pressurePlateBeside =
            (grid.At(x - 1, y).IsActive &&
             WorldSmoothingCatalog1458.IsPressurePlateWire(grid.At(x - 1, y).TileType)) ||
            (grid.At(x + 1, y).IsActive &&
             WorldSmoothingCatalog1458.IsPressurePlateWire(grid.At(x + 1, y).TileType));
        if (topOpen && !pressurePlateBeside)
        {
            if (IsFullSolid(grid, tile) && WorldSmoothingCatalog1458.CanBeClearedDuringGeneration(tile.TileType))
            {
                if ((!grid.At(x - 1, y).IsActive || IsFull(grid.At(x - 1, y))) &&
                    (!grid.At(x + 1, y).IsActive || IsFull(grid.At(x + 1, y))))
                {
                    ShapeExposedTop(grid, random, x, y, ref sloped, ref halfBricks, ref removed);
                }
            }
            else if (!tile.IsActive && IsFullSolid(grid, grid.At(x, y + 1)) &&
                     WorldSmoothingCatalog1458.SupportsGapFillBase(grid.At(x, y + 1).TileType))
            {
                FillDiagonalGap(grid, random, x, y, ref sloped, ref halfBricks, ref filled);
            }
        }
        else if (!grid.At(x, y + 1).IsActive && random.Next(2) == 0 && IsFullSolid(grid, tile) &&
                 IsFullSolid(grid, grid.At(x, y - 1)) &&
                 (!grid.At(x + 1, y).IsActive || IsFull(grid.At(x + 1, y))) &&
                 (!grid.At(x - 1, y).IsActive || IsFull(grid.At(x - 1, y))))
        {
            if (IsFullSolid(grid, grid.At(x - 1, y)) && !IsFullSolid(grid, grid.At(x + 1, y)) &&
                IsFullSolid(grid, grid.At(x - 1, y - 1)) && CanPound(grid, x, y))
            {
                SetSlope(grid, x, y, ref tile, sourceSlope: 3, ref sloped);
            }
            else if (IsFullSolid(grid, grid.At(x + 1, y)) && !IsFullSolid(grid, grid.At(x - 1, y)) &&
                     IsFullSolid(grid, grid.At(x + 1, y - 1)) && CanPound(grid, x, y))
            {
                SetSlope(grid, x, y, ref tile, sourceSlope: 4, ref sloped);
            }
        }
    }

    private static void ShapeExposedTop(
        Grid grid,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        ref long sloped,
        ref long halfBricks,
        ref long removed)
    {
        ref WorldTile tile = ref grid.At(x, y);
        if (IsFullSolid(grid, grid.At(x, y + 1)))
        {
            if (!IsFullSolid(grid, grid.At(x - 1, y)) && !IsHalfBrick(grid.At(x - 1, y + 1)) &&
                IsFullSolid(grid, grid.At(x - 1, y + 1)) && IsFullSolid(grid, grid.At(x + 1, y)) &&
                !grid.At(x + 1, y - 1).IsActive)
            {
                ShapeSlopeOrHalf(grid, x, y, ref tile, random, sourceSlope: 2, ref sloped, ref halfBricks);
            }
            else if (!IsFullSolid(grid, grid.At(x + 1, y)) && !IsHalfBrick(grid.At(x + 1, y + 1)) &&
                     IsFullSolid(grid, grid.At(x + 1, y + 1)) && IsFullSolid(grid, grid.At(x - 1, y)) &&
                     !grid.At(x - 1, y - 1).IsActive)
            {
                ShapeSlopeOrHalf(grid, x, y, ref tile, random, sourceSlope: 1, ref sloped, ref halfBricks);
            }
            else if (IsFullSolid(grid, grid.At(x + 1, y + 1)) && IsFullSolid(grid, grid.At(x - 1, y + 1)) &&
                     !grid.At(x + 1, y).IsActive && !grid.At(x - 1, y).IsActive)
            {
                Pound(grid, x, y, ref tile, ref halfBricks);
            }

            if (IsFullSolid(grid, tile))
            {
                if (IsFullSolid(grid, grid.At(x - 1, y)) && IsFullSolid(grid, grid.At(x + 1, y + 2)) &&
                    !grid.At(x + 1, y).IsActive && !grid.At(x + 1, y + 1).IsActive &&
                    !grid.At(x - 1, y - 1).IsActive)
                {
                    Kill(grid, x, y, ref removed);
                }
                else if (IsFullSolid(grid, grid.At(x + 1, y)) && IsFullSolid(grid, grid.At(x - 1, y + 2)) &&
                         !grid.At(x - 1, y).IsActive && !grid.At(x - 1, y + 1).IsActive &&
                         !grid.At(x + 1, y - 1).IsActive)
                {
                    Kill(grid, x, y, ref removed);
                }
                else if (!grid.At(x - 1, y + 1).IsActive && !grid.At(x - 1, y).IsActive &&
                         IsFullSolid(grid, grid.At(x + 1, y)) && IsFullSolid(grid, grid.At(x, y + 2)))
                {
                    ShapeErodedEdge(grid, x, y, ref tile, random, sourceSlope: 2, ref sloped, ref halfBricks, ref removed);
                }
                else if (!grid.At(x + 1, y + 1).IsActive && !grid.At(x + 1, y).IsActive &&
                         IsFullSolid(grid, grid.At(x - 1, y)) && IsFullSolid(grid, grid.At(x, y + 2)))
                {
                    ShapeErodedEdge(grid, x, y, ref tile, random, sourceSlope: 1, ref sloped, ref halfBricks, ref removed);
                }
            }
        }

        if (IsFullSolid(grid, tile) && !grid.At(x - 1, y).IsActive && !grid.At(x + 1, y).IsActive)
            Kill(grid, x, y, ref removed);
    }

    private static void FillDiagonalGap(
        Grid grid,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        ref long sloped,
        ref long halfBricks,
        ref long filled)
    {
        ref WorldTile tile = ref grid.At(x, y);

        WorldTile right = grid.At(x + 1, y);
        if (WorldSmoothingCatalog1458.SupportsGapFillNeighbor(right.TileType) &&
            IsFullSolid(grid, grid.At(x - 1, y + 1)) && IsFullSolid(grid, right) && !grid.At(x - 1, y).IsActive &&
            !grid.At(x + 1, y - 1).IsActive)
        {
            PlaceGapTile(grid, random, x, y, WorldSmoothingCatalog1458.UsesNeighborIdentityForGapFill(right.TileType)
                ? right.TileType
                : grid.At(x, y + 1).TileType);
            filled++;
            ShapeSlopeOrHalf(grid, x, y, ref tile, random, sourceSlope: 2, ref sloped, ref halfBricks);
        }

        WorldTile left = grid.At(x - 1, y);
        if (WorldSmoothingCatalog1458.SupportsGapFillNeighbor(left.TileType) &&
            IsFullSolid(grid, grid.At(x + 1, y + 1)) && IsFullSolid(grid, left) && !grid.At(x + 1, y).IsActive &&
            !grid.At(x - 1, y - 1).IsActive)
        {
            PlaceGapTile(grid, random, x, y, WorldSmoothingCatalog1458.UsesNeighborIdentityForGapFill(left.TileType)
                ? left.TileType
                : grid.At(x, y + 1).TileType);
            filled++;
            ShapeSlopeOrHalf(grid, x, y, ref tile, random, sourceSlope: 1, ref sloped, ref halfBricks);
        }
    }

    private static void ApplyFinishCell(
        Grid grid,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        ref long sloped,
        ref long halfBricks)
    {
        ref WorldTile tile = ref grid.At(x, y);
        if (random.Next(2) == 0 && !grid.At(x, y - 1).IsActive &&
            WorldSmoothingCatalog1458.IsSecondPhaseCandidate(tile.TileType) && IsFullSolid(grid, tile) &&
            (!grid.At(x - 1, y).IsActive || !WorldSmoothingCatalog1458.IsTrap(grid.At(x - 1, y).TileType)) &&
            (grid.At(x + 1, y).IsActive || !WorldSmoothingCatalog1458.IsTrap(grid.At(x + 1, y).TileType)))
        {
            if (IsFullSolid(grid, grid.At(x, y + 1)) && IsFullSolid(grid, grid.At(x + 1, y)) && !grid.At(x - 1, y).IsActive)
                SetSlope(grid, x, y, ref tile, sourceSlope: 2, ref sloped);
            if (IsFullSolid(grid, grid.At(x, y + 1)) && IsFullSolid(grid, grid.At(x - 1, y)) && !grid.At(x + 1, y).IsActive)
                SetSlope(grid, x, y, ref tile, sourceSlope: 1, ref sloped);
        }

        if (tile.IsActive && WorldSmoothingCatalog1458.IsSandConversion(tile.TileType))
            SmoothSandSlope(grid, x, y, ref sloped, ref halfBricks);

        int sourceSlope = GetSourceSlope(tile.Shape);
        if (sourceSlope == 1 && !IsFullSolid(grid, grid.At(x - 1, y)) && CanPound(grid, x, y))
        {
            ClearShape(ref tile);
            Pound(grid, x, y, ref tile, ref halfBricks);
        }
        if (GetSourceSlope(tile.Shape) == 2 && !IsFullSolid(grid, grid.At(x + 1, y)) && CanPound(grid, x, y))
        {
            ClearShape(ref tile);
            Pound(grid, x, y, ref tile, ref halfBricks);
        }
    }

    private static void SmoothSandSlope(Grid grid, int x, int y, ref long sloped, ref long halfBricks)
    {
        ref WorldTile tile = ref grid.At(x, y);
        if (!CanPound(grid, x, y) || !IsSolidOrSloped(grid, tile))
            return;

        bool occupiedAbove = !IsTileEmpty(grid.At(x, y - 1));
        bool nonSolidAbove = occupiedAbove && !IsSolidOrSloped(grid, grid.At(x, y - 1));
        bool solidBelow = IsSolidOrSloped(grid, grid.At(x, y + 1));
        bool solidLeft = IsSolidOrSloped(grid, grid.At(x - 1, y));
        bool solidRight = IsSolidOrSloped(grid, grid.At(x + 1, y));
        SlopeNeighborMask1458 mask =
            (occupiedAbove ? SlopeNeighborMask1458.OccupiedAbove : SlopeNeighborMask1458.None) |
            (solidBelow ? SlopeNeighborMask1458.SolidBelow : SlopeNeighborMask1458.None) |
            (solidLeft ? SlopeNeighborMask1458.SolidLeft : SlopeNeighborMask1458.None) |
            (solidRight ? SlopeNeighborMask1458.SolidRight : SlopeNeighborMask1458.None);

        switch (mask)
        {
            case SlopeNeighborMask1458.OccupiedAbove | SlopeNeighborMask1458.SolidLeft
                when !nonSolidAbove:
                SetSlope(grid, x, y, ref tile, sourceSlope: 3, ref sloped);
                break;
            case SlopeNeighborMask1458.OccupiedAbove | SlopeNeighborMask1458.SolidRight
                when !nonSolidAbove:
                SetSlope(grid, x, y, ref tile, sourceSlope: 4, ref sloped);
                break;
            case SlopeNeighborMask1458.SolidBelow | SlopeNeighborMask1458.SolidLeft:
                SetSlope(grid, x, y, ref tile, sourceSlope: 1, ref sloped);
                break;
            case SlopeNeighborMask1458.SolidBelow | SlopeNeighborMask1458.SolidRight:
                SetSlope(grid, x, y, ref tile, sourceSlope: 2, ref sloped);
                break;
            case SlopeNeighborMask1458.SolidBelow:
                ClearShape(ref tile);
                Pound(grid, x, y, ref tile, ref halfBricks);
                break;
            default:
                ClearShape(ref tile);
                break;
        }
    }

    private static void ShapeSlopeOrHalf(
        Grid grid, int x, int y, ref WorldTile tile,
        IWorldGenerationVanillaRandom random,
        int sourceSlope,
        ref long sloped,
        ref long halfBricks)
    {
        if (random.Next(2) == 0)
            SetSlope(grid, x, y, ref tile, sourceSlope, ref sloped);
        else
            Pound(grid, x, y, ref tile, ref halfBricks);
    }

    private static void ShapeErodedEdge(
        Grid grid, int x, int y, ref WorldTile tile,
        IWorldGenerationVanillaRandom random,
        int sourceSlope,
        ref long sloped,
        ref long halfBricks,
        ref long removed)
    {
        if (random.Next(5) == 0)
            Kill(grid, x, y, ref removed);
        else if (random.Next(5) == 0)
            Pound(grid, x, y, ref tile, ref halfBricks);
        else
            SetSlope(grid, x, y, ref tile, sourceSlope, ref sloped);
    }

    private static bool CanPound(Grid grid, int x, int y)
    {
        WorldTile tile = grid.At(x, y);
        if (!tile.IsActive || !WorldSmoothingCatalog1458.CanBePounded(tile.TileType))
            return false;
        if (tile.WallType == VanillaWallIds.UnbreakableTemple)
            return false;
        WorldTile above = grid.At(x, y - 1);
        return grid.Framer.CanRemoveTile(x, y) &&
               (!above.IsActive || !WorldSmoothingCatalog1458.ForbidsSlopingBelow(above.TileType));
    }

    private static void SetSlope(Grid grid, int x, int y, ref WorldTile tile, int sourceSlope, ref long sloped)
    {
        if (!CanPound(grid, x, y))
            return;
        tile.TrySetSourceShape(false, checked((byte)sourceSlope));
        sloped++;
    }

    private static void Pound(Grid grid, int x, int y, ref WorldTile tile, ref long halfBricks)
    {
        if (!CanPound(grid, x, y))
            return;
        tile.TrySetSourceShape(!tile.IsHalfBrick, tile.SourceSlope);
        halfBricks++;
    }

    private static void Kill(Grid grid, int x, int y, ref long removed)
    {
        bool before = grid.At(x, y).IsActive;
        grid.Framer.KillTile(x, y);
        if (before && !grid.At(x, y).IsActive) removed++;
    }

    private static void PlaceGapTile(Grid grid, IWorldGenerationVanillaRandom random, int x, int y, TileTypeId type)
    {
        if (type.Value is 2 or 23 or 109 or 199 or 60 or 661 or 662 or 70 or 633 or
            179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627)
            return;
        if (grid.Framer.GenerationSolid(checked((ushort)type.Value)) && !grid.Framer.EmptyPlacementTile(x, y)) return;
        if (type == VanillaTileIds.ClosedDoor)
        {
            CaveHousePlacement1458.PlaceHouseDoor(grid.Workspace, random, x, y, 0);
            return;
        }
        if (WorldSmoothingCatalog1458.UsesGapBoulderPlacement(type))
        {
            grid.Framer.PlaceTrapBoulder(x, y, checked((ushort)type.Value));
            return;
        }
        ref WorldTile tile = ref grid.At(x, y);
        if (!tile.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref tile);
        if (WorldSmoothingCatalog1458.IsGapTeleporter(type))
        {
            // Either diagonal gap requires an occupied neighbour in the same row;
            // Place3x1 therefore refuses its footprint after clearing the anchor.
            grid.Framer.SquareTileFrame(x, y);
            grid.Framer.FinishPlacement(x, y);
            return;
        }
        tile.Type = checked((ushort)type.Value);
        tile.Flags |= WorldTileFlags.Active;
        if (WorldSmoothingCatalog1458.IsGapGolfHole(type)) grid.Framer.SquareTileFrame(x, y);
        grid.Framer.FinishPlacement(x, y);
    }

    private static void ClearShape(ref WorldTile tile) => tile.Shape = (byte)TileShape1458.Full;

    private static int GetSourceSlope(byte shape) => shape >= (byte)TileShape1458.SlopeDownRight ? shape - 1 : 0;

    private static bool IsTileEmpty(in WorldTile tile) => !tile.IsActive || tile.IsActuated;

    private static bool IsFull(in WorldTile tile) => tile.Shape == (byte)TileShape1458.Full;

    private static bool IsHalfBrick(in WorldTile tile) => tile.IsHalfBrick;

    private static bool IsFullSolid(Grid grid, in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && IsFull(tile) && IsSolidIdentity(grid, tile.TileType);

    private static bool IsSolidOrSloped(Grid grid, in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && IsSolidIdentity(grid, tile.TileType);

    private static bool IsSolidIdentity(Grid grid, TileTypeId type) =>
        !VanillaTileCollisionCatalog.IsSolidTop(type) && grid.Framer.GenerationSolid(checked((ushort)type.Value));

    private readonly ref struct Grid
    {
        private readonly Span<WorldTile> tiles;

        public Grid(WorldTileStore store, GenerationTileFraming1458 framer, Workspace workspace)
        {
            Framer = framer;
            Workspace = workspace;
            Width = store.Dimensions.WidthTiles;
            Height = store.Dimensions.HeightTiles;
            tiles = store.Tiles;
        }

        public GenerationTileFraming1458 Framer { get; }
        public Workspace Workspace { get; }
        public int Width { get; }
        public int Height { get; }
        public ref WorldTile At(int x, int y) => ref tiles[x * Height + y];
    }
}
