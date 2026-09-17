using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>One accepted oasis, as retained by TerrariaServer 1.4.5.8 in <c>GenVars.oasisPosition/oasisWidth</c>.</summary>
/// <remarks>
/// The retained row is the row the source settled on AFTER its post-acceptance descent, not the surface row it
/// first probed, and the width is the half-span <c>genRand.Next(45, 61)</c> drawn for this basin. Later passes
/// read both, so neither may be normalized here.
/// </remarks>
internal readonly record struct VanillaOasisAnchor1458(int X, int Y, int Width);

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.PlaceOasis</c>. The registered
/// <c>GenPassNameID.Oasis</c> pass draws candidate coordinates and this method decides, shapes and retains.
/// </summary>
/// <remarks>
/// Every constant here is the source's own. The basin is not an ellipse fill: the source measures a stretched
/// radius (<c>|dx| * 0.7</c> against <c>|dy| * 1.35</c>) against a per-cell jittered threshold
/// <c>halfWidth * (0.53 + NextDouble() * 0.04)</c>, so the rim is noisy and the draw happens for every cell in
/// the bounding box whether or not it is inside. Outside the basin it carves an overhang above the waterline and
/// lays sand below it, using a falloff raised to the fourth power. A second pass then bridges the surrounding
/// shelf between sand edges, with a one-in-five chance of a five-to-ten-tile raised lip.
/// </remarks>
internal sealed class OasisBasin1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    CancellationToken cancellation)
{
    private const ushort Sand = 53;
    private const ushort SandstoneBrick = 151;
    private const ushort Sandstone = 397;

    /// <summary>The source's fixed <c>GenVars.oasisHeight</c>.</summary>
    internal const int OasisHeight1458 = 20;

    /// <summary>The source's fixed <c>GenVars.maxOasis</c>: retention stops here, placement does not.</summary>
    internal const int MaximumRetainedOasis1458 = 20;

    /// <summary>Minimum centre separation the source enforces against already retained basins.</summary>
    private const int MinimumSeparation1458 = 350;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>
    /// Attempts one oasis at the drawn candidate column. Returns the retained anchor on acceptance, matching the
    /// source's <c>true</c>, and <c>null</c> for every refusal. A refusal consumes no shared RNG.
    /// </summary>
    public VanillaOasisAnchor1458? TryPlace(int x, int y, IReadOnlyList<VanillaOasisAnchor1458> retained)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Contains(x, y))
            return null;

        WorldTile start = At(x, y);
        if (start.IsActive || start.Wall != 0)
            return null;

        int row = y;
        while (row + 1 < height && !At(x, row).IsActive && At(x, row).Wall == 0 && row <= worldSurface)
            row++;

        if (row > worldSurface - 10.0)
            return null;
        if (At(x, row).Type != Sand)
            return null;

        for (int i = 0; i < retained.Count; i++)
        {
            double dx = retained[i].X - x;
            double dy = retained[i].Y - row;
            if (Math.Sqrt(dx * dx + dy * dy) < MinimumSeparation1458)
                return null;
        }

        int halfWidth = random.Next(45, 61);
        if (!IsSiteClear(x, row, halfWidth))
            return null;

        row = Descend(x, row, halfWidth);
        Shape(x, row, halfWidth);
        BridgeShelf(x, row, halfWidth);
        return new VanillaOasisAnchor1458(x, row, halfWidth);
    }

    /// <summary>
    /// The source's acceptance scan. Note the two different reaches: any solid cell in the whole
    /// <c>halfWidth + 50</c> box must be Sand, while liquid, walls, Sandstone and Sandstone Brick only reject
    /// inside the tighter <c>halfWidth</c> by <c>oasisHeight / 2</c> box.
    /// </summary>
    private bool IsSiteClear(int x, int row, int halfWidth)
    {
        int reach = halfWidth + 50;
        for (int i = x - reach; i <= x + reach; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int j = row - OasisHeight1458; j <= row + OasisHeight1458 + 4; j++)
            {
                if (!Contains(i, j))
                    return false;

                WorldTile tile = At(i, j);
                bool inner = Math.Abs(i - x) < halfWidth && Math.Abs(j - row) < OasisHeight1458 / 2;
                if (tile.IsActive)
                {
                    if (!VanillaTileCollisionCatalog.IsSolid(tile.TileType))
                        continue;
                    if ((tile.Type == SandstoneBrick || tile.Type == Sandstone) && inner)
                        return false;
                    if (tile.Type != Sand)
                        return false;
                }
                else if ((tile.LiquidAmount > 0 || tile.Wall > 0) && inner)
                {
                    return false;
                }
            }

            // The source's per-column ceiling/floor test is guarded by `i > X - halfWidth / 2 && i < X -
            // halfWidth / 2`, which no column can satisfy. The branch is dead in 1.4.5.8 and is deliberately
            // reproduced as dead rather than "corrected" into the symmetric test it looks like it wants to be:
            // making it live would reject sites the source accepts.
        }

        return true;
    }

    /// <summary>
    /// Sinks the basin row until both flanks stand on walled-off solid ground, giving up after twenty rows. The
    /// source keeps the row it reached either way, which is why a cavity under one flank shifts the whole basin
    /// down instead of refusing the site.
    /// </summary>
    private int Descend(int x, int row, int halfWidth)
    {
        const int probeDepth = 5;
        int origin = row;
        while (!FlankSupported(x - halfWidth, row + probeDepth) || !FlankSupported(x + halfWidth, row + probeDepth))
        {
            row++;
            if (row - origin > 20)
                break;
        }

        return row;
    }

    private bool FlankSupported(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && tile.Wall == 0;
    }

    private void Shape(int x, int row, int halfWidth)
    {
        int radius = halfWidth / 2;
        int left = Math.Max(0, x - halfWidth * 3);
        int right = Math.Min(width, x + halfWidth * 3);
        int top = Math.Max(0, row - OasisHeight1458 * 4);
        int bottom = Math.Min(height, row + OasisHeight1458 * 3);

        for (int i = left; i < right; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int j = top; j < bottom; j++)
            {
                double stretchedX = Math.Abs(i - x) * 0.7;
                double stretchedY = Math.Abs(j - row) * 1.35;
                double distance = Math.Sqrt(stretchedX * stretchedX + stretchedY * stretchedY);
                // The jitter draw happens for every cell in the box, inside the basin or not.
                double threshold = radius * (0.53 + random.NextDouble() * 0.04);
                double falloff = 1.0 - Math.Abs(i - x) / (double)(right - x);
                falloff *= 2.3;
                falloff *= falloff;
                falloff *= falloff;

                ref WorldTile tile = ref At(i, j);
                if (distance < threshold)
                {
                    if (j == row + 1)
                        tile.LiquidAmount = 127;
                    else if (j > row + 1)
                        tile.LiquidAmount = byte.MaxValue;

                    // Tile.lava(false) clears only the low bit of the two-bit liquid type, so lava becomes
                    // water and shimmer becomes honey. It is not a reset to water.
                    tile.LiquidKind = (WorldLiquidKind)((int)tile.LiquidKind & ~1);
                    tile.Flags &= ~WorldTileFlags.Active;
                }
                else if (j < row && stretchedX < threshold + Math.Abs(j - row) * 3 * falloff)
                {
                    if (tile.Type == Sand)
                        tile.Flags &= ~WorldTileFlags.Active;
                }
                else if (j >= row && stretchedX < threshold + Math.Abs(j - row) * falloff && tile.Wall == 0)
                {
                    if (tile.IsActive &&
                        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                    {
                        tile.Shape = 0;
                        continue;
                    }

                    tile.Flags |= WorldTileFlags.Active;
                    tile.Type = Sand;
                    tile.Shape = 0;
                }
            }
        }
    }

    /// <summary>
    /// The source's second pass: for every open cell outside the basin proper, walk up to fifty tiles each way
    /// along supported ground for a solid edge and fill the gap between the two with Sand.
    /// </summary>
    private void BridgeShelf(int x, int row, int halfWidth)
    {
        const int reach = 50;
        int radius = halfWidth / 2;
        int left = Math.Max(0, x - halfWidth * 2);
        int right = Math.Min(width, x + halfWidth * 2);
        int bottom = Math.Min(height - 1, row + OasisHeight1458 * 2);

        for (int i = left; i < right; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int j = bottom; j >= row; j--)
            {
                double stretchedX = Math.Abs(i - x) * 0.7;
                double stretchedY = Math.Abs(j - row) * 1.35;
                if (Math.Sqrt(stretchedX * stretchedX + stretchedY * stretchedY) <= radius * 0.5700000000000001)
                    continue;

                if (At(i, j).IsActive || At(i, j).Wall != 0)
                    continue;

                // The source also tracks whether either edge was Sand, but assigns that flag `true`
                // unconditionally immediately before testing it, so the requirement is dead in 1.4.5.8.
                int rightEdge = FindShelfEdge(i, j, 1, reach);
                int leftEdge = FindShelfEdge(i, j, -1, reach);
                if (leftEdge <= -1 || rightEdge <= -1)
                    continue;

                int lip = 0;
                for (int k = leftEdge + 1; k < rightEdge; k++)
                {
                    // The source draws the lip length before writing the cell, and only once the gap is wider
                    // than five tiles.
                    if (rightEdge - leftEdge > 5 && random.Next(5) == 0)
                        lip = random.Next(5, 10);

                    ref WorldTile shelf = ref At(k, j);
                    shelf.Flags |= WorldTileFlags.Active;
                    shelf.Type = Sand;
                    if (lip > 0)
                    {
                        lip--;
                        ref WorldTile above = ref At(k, j - 1);
                        above.Flags |= WorldTileFlags.Active;
                        above.Type = Sand;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Walks at most <paramref name="reach"/> tiles in one direction while the ground below stays solid and the
    /// row itself stays wall-free, and returns the first solid column. A non-solid active cell ends the walk
    /// with no edge.
    /// </summary>
    private int FindShelfEdge(int startX, int y, int step, int reach)
    {
        for (int i = startX; step > 0 ? i <= startX + reach : i >= startX - reach; i += step)
        {
            if (!Contains(i, y) || !Contains(i, y + 1))
                return -1;

            WorldTile below = At(i, y + 1);
            if (!below.IsActive || !VanillaTileCollisionCatalog.IsSolid(below.TileType) || At(i, y).Wall > 0)
                return -1;

            WorldTile tile = At(i, y);
            if (tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType))
                return i;
            if (tile.IsActive)
                return -1;
        }

        return -1;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
