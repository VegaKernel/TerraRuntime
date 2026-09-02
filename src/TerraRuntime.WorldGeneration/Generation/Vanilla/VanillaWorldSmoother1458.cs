using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

internal enum VanillaTileShape1458 : byte
{
    Full = 0,
    HalfBrick = 1,
    SlopeDownRight = 2,
    SlopeDownLeft = 3,
    SlopeUpRight = 4,
    SlopeUpLeft = 5
}

[Flags]
internal enum VanillaSlopeNeighborMask1458 : byte
{
    None = 0,
    SolidRight = 1 << 0,
    SolidLeft = 1 << 1,
    SolidBelow = 1 << 2,
    OccupiedAbove = 1 << 3
}

internal readonly record struct VanillaWorldSmoothingResult1458(
    long SlopedTiles,
    long HalfBricks,
    long RemovedTiles,
    long FilledTiles);

/// <summary>
/// Clean-room TerrariaServer 1.4.5.8 <c>WorldGen</c> Smooth World implementation for ordinary generation. It owns
/// both ordered topology scans, exact shared-RNG decision points, sand normalization and orphan-slope correction.
/// </summary>
internal static class VanillaWorldSmoother1458
{
    private const int Border = 20;

    public static VanillaWorldSmoothingResult1458 Apply(
        RuntimeWorldGenerationWorkspace workspace,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);

        var grid = new Grid(workspace.TileStore);
        long sloped = 0;
        long halfBricks = 0;
        long removed = 0;
        long filled = 0;

        for (int x = Border; x < grid.Width - Border; x++)
        {
            if ((x & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            for (int y = Border; y < grid.Height - Border; y++)
                ApplyTopologyCell(grid, random, x, y, ref sloped, ref halfBricks, ref removed, ref filled);
        }

        for (int x = Border; x < grid.Width - Border; x++)
        {
            if ((x & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            for (int y = Border; y < grid.Height - Border; y++)
                ApplyFinishCell(grid, random, x, y, ref sloped, ref halfBricks);
        }

        return new VanillaWorldSmoothingResult1458(sloped, halfBricks, removed, filled);
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
        if ((tile.IsActive && VanillaWorldSmoothingCatalog1458.PreventsSlopesDuringGeneration(tile.TileType)) ||
            (above.IsActive && VanillaWorldSmoothingCatalog1458.PreventsSlopesDuringGeneration(above.TileType)))
            return;

        bool topOpen = !above.IsActive;
        bool pressurePlateBeside =
            (grid.At(x - 1, y).IsActive &&
             VanillaWorldSmoothingCatalog1458.IsPressurePlateWire(grid.At(x - 1, y).TileType)) ||
            (grid.At(x + 1, y).IsActive &&
             VanillaWorldSmoothingCatalog1458.IsPressurePlateWire(grid.At(x + 1, y).TileType));
        if (topOpen && !pressurePlateBeside)
        {
            if (IsFullSolid(tile) && VanillaWorldSmoothingCatalog1458.CanBeClearedDuringGeneration(tile.TileType))
            {
                if ((!grid.At(x - 1, y).IsActive || IsFull(grid.At(x - 1, y))) &&
                    (!grid.At(x + 1, y).IsActive || IsFull(grid.At(x + 1, y))))
                {
                    ShapeExposedTop(grid, random, x, y, ref sloped, ref halfBricks, ref removed);
                }
            }
            else if (!tile.IsActive && IsFullSolid(grid.At(x, y + 1)) &&
                     VanillaWorldSmoothingCatalog1458.SupportsGapFillBase(grid.At(x, y + 1).TileType))
            {
                FillDiagonalGap(grid, random, x, y, ref sloped, ref halfBricks, ref filled);
            }
        }
        else if (!grid.At(x, y + 1).IsActive && random.Next(2) == 0 && IsFullSolid(tile) &&
                 IsFullSolid(grid.At(x, y - 1)) &&
                 (!grid.At(x + 1, y).IsActive || IsFull(grid.At(x + 1, y))) &&
                 (!grid.At(x - 1, y).IsActive || IsFull(grid.At(x - 1, y))))
        {
            if (IsFullSolid(grid.At(x - 1, y)) && !IsFullSolid(grid.At(x + 1, y)) &&
                IsFullSolid(grid.At(x - 1, y - 1)) && CanPound(grid, x, y))
            {
                SetSlope(ref tile, sourceSlope: 3, ref sloped);
            }
            else if (IsFullSolid(grid.At(x + 1, y)) && !IsFullSolid(grid.At(x - 1, y)) &&
                     IsFullSolid(grid.At(x + 1, y - 1)) && CanPound(grid, x, y))
            {
                SetSlope(ref tile, sourceSlope: 4, ref sloped);
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
        if (IsFullSolid(grid.At(x, y + 1)))
        {
            if (!IsFullSolid(grid.At(x - 1, y)) && !IsHalfBrick(grid.At(x - 1, y + 1)) &&
                IsFullSolid(grid.At(x - 1, y + 1)) && IsFullSolid(grid.At(x + 1, y)) &&
                !grid.At(x + 1, y - 1).IsActive)
            {
                ShapeSlopeOrHalf(ref tile, random, sourceSlope: 2, ref sloped, ref halfBricks);
            }
            else if (!IsFullSolid(grid.At(x + 1, y)) && !IsHalfBrick(grid.At(x + 1, y + 1)) &&
                     IsFullSolid(grid.At(x + 1, y + 1)) && IsFullSolid(grid.At(x - 1, y)) &&
                     !grid.At(x - 1, y - 1).IsActive)
            {
                ShapeSlopeOrHalf(ref tile, random, sourceSlope: 1, ref sloped, ref halfBricks);
            }
            else if (IsFullSolid(grid.At(x + 1, y + 1)) && IsFullSolid(grid.At(x - 1, y + 1)) &&
                     !grid.At(x + 1, y).IsActive && !grid.At(x - 1, y).IsActive)
            {
                Pound(ref tile, ref halfBricks);
            }

            if (IsFullSolid(tile))
            {
                if (IsFullSolid(grid.At(x - 1, y)) && IsFullSolid(grid.At(x + 1, y + 2)) &&
                    !grid.At(x + 1, y).IsActive && !grid.At(x + 1, y + 1).IsActive &&
                    !grid.At(x - 1, y - 1).IsActive)
                {
                    Kill(ref tile, ref removed);
                }
                else if (IsFullSolid(grid.At(x + 1, y)) && IsFullSolid(grid.At(x - 1, y + 2)) &&
                         !grid.At(x - 1, y).IsActive && !grid.At(x - 1, y + 1).IsActive &&
                         !grid.At(x + 1, y - 1).IsActive)
                {
                    Kill(ref tile, ref removed);
                }
                else if (!grid.At(x - 1, y + 1).IsActive && !grid.At(x - 1, y).IsActive &&
                         IsFullSolid(grid.At(x + 1, y)) && IsFullSolid(grid.At(x, y + 2)))
                {
                    ShapeErodedEdge(ref tile, random, sourceSlope: 2, ref sloped, ref halfBricks, ref removed);
                }
                else if (!grid.At(x + 1, y + 1).IsActive && !grid.At(x + 1, y).IsActive &&
                         IsFullSolid(grid.At(x - 1, y)) && IsFullSolid(grid.At(x, y + 2)))
                {
                    ShapeErodedEdge(ref tile, random, sourceSlope: 1, ref sloped, ref halfBricks, ref removed);
                }
            }
        }

        if (IsFullSolid(tile) && !grid.At(x - 1, y).IsActive && !grid.At(x + 1, y).IsActive)
            Kill(ref tile, ref removed);
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
        WorldTile below = grid.At(x, y + 1);
        WorldTile right = grid.At(x + 1, y);
        if (VanillaWorldSmoothingCatalog1458.SupportsGapFillNeighbor(right.TileType) &&
            IsFullSolid(grid.At(x - 1, y + 1)) && IsFullSolid(right) && !grid.At(x - 1, y).IsActive &&
            !grid.At(x + 1, y - 1).IsActive)
        {
            PlaceGapTile(ref tile, VanillaWorldSmoothingCatalog1458.UsesNeighborIdentityForGapFill(right.TileType)
                ? right.TileType
                : below.TileType);
            filled++;
            ShapeSlopeOrHalf(ref tile, random, sourceSlope: 2, ref sloped, ref halfBricks);
        }

        WorldTile left = grid.At(x - 1, y);
        if (VanillaWorldSmoothingCatalog1458.SupportsGapFillNeighbor(left.TileType) &&
            IsFullSolid(grid.At(x + 1, y + 1)) && IsFullSolid(left) && !grid.At(x + 1, y).IsActive &&
            !grid.At(x - 1, y - 1).IsActive)
        {
            PlaceGapTile(ref tile, VanillaWorldSmoothingCatalog1458.UsesNeighborIdentityForGapFill(left.TileType)
                ? left.TileType
                : below.TileType);
            filled++;
            ShapeSlopeOrHalf(ref tile, random, sourceSlope: 1, ref sloped, ref halfBricks);
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
            VanillaWorldSmoothingCatalog1458.IsSecondPhaseCandidate(tile.TileType) && IsFullSolid(tile) &&
            (!grid.At(x - 1, y).IsActive || !VanillaWorldSmoothingCatalog1458.IsTrap(grid.At(x - 1, y).TileType)) &&
            (grid.At(x + 1, y).IsActive || !VanillaWorldSmoothingCatalog1458.IsTrap(grid.At(x + 1, y).TileType)))
        {
            if (IsFullSolid(grid.At(x, y + 1)) && IsFullSolid(grid.At(x + 1, y)) && !grid.At(x - 1, y).IsActive)
                SetSlope(ref tile, sourceSlope: 2, ref sloped);
            if (IsFullSolid(grid.At(x, y + 1)) && IsFullSolid(grid.At(x - 1, y)) && !grid.At(x + 1, y).IsActive)
                SetSlope(ref tile, sourceSlope: 1, ref sloped);
        }

        if (tile.IsActive && VanillaWorldSmoothingCatalog1458.IsSandConversion(tile.TileType))
            SmoothSandSlope(grid, x, y, ref sloped, ref halfBricks);

        int sourceSlope = GetSourceSlope(tile.Shape);
        if (sourceSlope == 1 && !IsFullSolid(grid.At(x - 1, y)) && CanPound(grid, x, y))
        {
            ClearShape(ref tile);
            Pound(ref tile, ref halfBricks);
        }
        if (GetSourceSlope(tile.Shape) == 2 && !IsFullSolid(grid.At(x + 1, y)) && CanPound(grid, x, y))
        {
            ClearShape(ref tile);
            Pound(ref tile, ref halfBricks);
        }
    }

    private static void SmoothSandSlope(Grid grid, int x, int y, ref long sloped, ref long halfBricks)
    {
        ref WorldTile tile = ref grid.At(x, y);
        if (!CanPound(grid, x, y) || !IsSolidOrSloped(tile))
            return;

        bool occupiedAbove = !IsTileEmpty(grid.At(x, y - 1));
        bool nonSolidAbove = occupiedAbove && !IsSolidOrSloped(grid.At(x, y - 1));
        bool solidBelow = IsSolidOrSloped(grid.At(x, y + 1));
        bool solidLeft = IsSolidOrSloped(grid.At(x - 1, y));
        bool solidRight = IsSolidOrSloped(grid.At(x + 1, y));
        VanillaSlopeNeighborMask1458 mask =
            (occupiedAbove ? VanillaSlopeNeighborMask1458.OccupiedAbove : VanillaSlopeNeighborMask1458.None) |
            (solidBelow ? VanillaSlopeNeighborMask1458.SolidBelow : VanillaSlopeNeighborMask1458.None) |
            (solidLeft ? VanillaSlopeNeighborMask1458.SolidLeft : VanillaSlopeNeighborMask1458.None) |
            (solidRight ? VanillaSlopeNeighborMask1458.SolidRight : VanillaSlopeNeighborMask1458.None);

        switch (mask)
        {
            case VanillaSlopeNeighborMask1458.OccupiedAbove | VanillaSlopeNeighborMask1458.SolidLeft
                when !nonSolidAbove:
                SetSlope(ref tile, sourceSlope: 3, ref sloped);
                break;
            case VanillaSlopeNeighborMask1458.OccupiedAbove | VanillaSlopeNeighborMask1458.SolidRight
                when !nonSolidAbove:
                SetSlope(ref tile, sourceSlope: 4, ref sloped);
                break;
            case VanillaSlopeNeighborMask1458.SolidBelow | VanillaSlopeNeighborMask1458.SolidLeft:
                SetSlope(ref tile, sourceSlope: 1, ref sloped);
                break;
            case VanillaSlopeNeighborMask1458.SolidBelow | VanillaSlopeNeighborMask1458.SolidRight:
                SetSlope(ref tile, sourceSlope: 2, ref sloped);
                break;
            case VanillaSlopeNeighborMask1458.SolidBelow:
                ClearShape(ref tile);
                Pound(ref tile, ref halfBricks);
                break;
            default:
                ClearShape(ref tile);
                break;
        }
    }

    private static void ShapeSlopeOrHalf(
        ref WorldTile tile,
        IWorldGenerationVanillaRandom random,
        int sourceSlope,
        ref long sloped,
        ref long halfBricks)
    {
        if (random.Next(2) == 0)
            SetSlope(ref tile, sourceSlope, ref sloped);
        else
            Pound(ref tile, ref halfBricks);
    }

    private static void ShapeErodedEdge(
        ref WorldTile tile,
        IWorldGenerationVanillaRandom random,
        int sourceSlope,
        ref long sloped,
        ref long halfBricks,
        ref long removed)
    {
        if (random.Next(5) == 0)
            Kill(ref tile, ref removed);
        else if (random.Next(5) == 0)
            Pound(ref tile, ref halfBricks);
        else
            SetSlope(ref tile, sourceSlope, ref sloped);
    }

    private static bool CanPound(Grid grid, int x, int y)
    {
        WorldTile tile = grid.At(x, y);
        if (!tile.IsActive || !VanillaWorldSmoothingCatalog1458.CanBePounded(tile.TileType))
            return false;
        if (tile.WallType == VanillaWallIds.UnbreakableTemple)
            return false;
        WorldTile above = grid.At(x, y - 1);
        return VanillaWorldSmoothingCatalog1458.CanRemoveTileBelow(in above, tile.TileType) &&
               (!above.IsActive || !VanillaWorldSmoothingCatalog1458.ForbidsSlopingBelow(above.TileType));
    }

    private static void SetSlope(ref WorldTile tile, int sourceSlope, ref long sloped)
    {
        if (!tile.IsActive || !VanillaWorldSmoothingCatalog1458.CanBePounded(tile.TileType))
            return;
        tile.Shape = checked((byte)(sourceSlope + 1));
        sloped++;
    }

    private static void Pound(ref WorldTile tile, ref long halfBricks)
    {
        if (!tile.IsActive || !VanillaWorldSmoothingCatalog1458.CanBePounded(tile.TileType))
            return;
        tile.Shape = tile.Shape == (byte)VanillaTileShape1458.HalfBrick
            ? (byte)VanillaTileShape1458.Full
            : (byte)VanillaTileShape1458.HalfBrick;
        halfBricks++;
    }

    private static void Kill(ref WorldTile tile, ref long removed)
    {
        if (!tile.IsActive)
            return;
        tile.Type = checked((ushort)VanillaTileIds.Dirt.Value);
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.Actuator);
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.Shape = 0;
        removed++;
    }

    private static void PlaceGapTile(ref WorldTile tile, TileTypeId type)
    {
        tile.Type = checked((ushort)type.Value);
        tile.Flags |= WorldTileFlags.Active;
        tile.Flags &= ~WorldTileFlags.Inactive;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ClearShape(ref WorldTile tile) => tile.Shape = (byte)VanillaTileShape1458.Full;

    private static int GetSourceSlope(byte shape) => shape >= (byte)VanillaTileShape1458.SlopeDownRight ? shape - 1 : 0;

    private static bool IsTileEmpty(in WorldTile tile) => !tile.IsActive || tile.IsActuated;

    private static bool IsFull(in WorldTile tile) => tile.Shape == (byte)VanillaTileShape1458.Full;

    private static bool IsHalfBrick(in WorldTile tile) => tile.Shape == (byte)VanillaTileShape1458.HalfBrick;

    private static bool IsFullSolid(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && IsFull(tile) && IsSolidIdentity(tile.TileType);

    private static bool IsSolidOrSloped(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && IsSolidIdentity(tile.TileType);

    private static bool IsSolidIdentity(TileTypeId type) =>
        VanillaTileCollisionCatalog.IsSolid(type) || VanillaWorldSmoothingCatalog1458.IsTemporarilySolidCrackedBrick(type);

    private readonly ref struct Grid
    {
        private readonly Span<WorldTile> tiles;

        public Grid(WorldTileStore store)
        {
            Width = store.Dimensions.WidthTiles;
            Height = store.Dimensions.HeightTiles;
            tiles = store.Tiles;
        }

        public int Width { get; }
        public int Height { get; }
        public ref WorldTile At(int x, int y) => ref tiles[x * Height + y];
    }
}
