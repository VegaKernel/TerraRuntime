using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>One tile selected by the TerrariaServer 1.4.5.8 projectile CutTilesAt path.</summary>
public readonly record struct VanillaProjectileTileCutCandidate(int X, int Y);

/// <summary>
/// Pure source-backed TerrariaServer 1.4.5.8 projectile tile-cut planning. This reproduces the ordinary
/// CutTilesAt rectangle traversal, Main.tileCut lookup and WorldGen.CanCutTile predicate without performing
/// irreversible WorldGen.KillTile side effects. Mutation, drops and network publication belong to a later
/// authoritative effect stage.
/// </summary>
public static class VanillaWorldProjectileTileCut
{
    private const double TileSizePixels = 16d;
    private const ushort ProtectedWallType = 350;
    private const ushort SpecialCuttableTileType = 254;
    private const short SpecialCuttableMinimumFrameX = 144;

    /// <summary>
    /// Mirrors Projectile.CutTilesAt integer conversion and clamping. The returned bounds are half-open and
    /// may be empty when the whole projectile rectangle lies outside the normalized runtime world.
    /// </summary>
    public static WorldTileBounds GetCutBounds(
        WorldDimensions dimensions,
        float positionX,
        float positionY,
        int boxWidth,
        int boxHeight)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ValidateBox(positionX, positionY, boxWidth, boxHeight);

        return GetBoundsFromPixelEdges(
            dimensions,
            positionX,
            positionY,
            (double)positionX + boxWidth,
            (double)positionY + boxHeight);
    }

    /// <summary>
    /// Conservatively checks the axis-aligned sweep of one projectile rectangle between two committed-state
    /// positions. Vanilla CutTilesAt itself evaluates one rectangle; the swept superset is used only as a
    /// safety gate while irreversible KillTile/drop effects are not yet modeled. False positives keep a
    /// server-owned projectile unsupported for that tick, while false negatives would lose a world mutation.
    /// </summary>
    public static bool HasCandidateAlongSweep(
        WorldTileStore tiles,
        float startX,
        float startY,
        float endX,
        float endY,
        int boxWidth,
        int boxHeight)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ValidateBox(startX, startY, boxWidth, boxHeight);
        if (!float.IsFinite(endX))
            throw new ArgumentOutOfRangeException(nameof(endX));
        if (!float.IsFinite(endY))
            throw new ArgumentOutOfRangeException(nameof(endY));

        double left = Math.Min(startX, endX);
        double top = Math.Min(startY, endY);
        double right = Math.Max((double)startX + boxWidth, (double)endX + boxWidth);
        double bottom = Math.Max((double)startY + boxHeight, (double)endY + boxHeight);
        WorldTileBounds bounds = GetBoundsFromPixelEdges(tiles.Dimensions, left, top, right, bottom);

        for (int x = bounds.X; x < bounds.ExclusiveRight; x++)
        {
            for (int y = bounds.Y; y < bounds.ExclusiveBottom; y++)
            {
                if (IsCutCandidate(tiles, x, y))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collects the exact ordinary CutTilesAt candidates that reach WorldGen.KillTile after the source-backed
    /// tileCut and CanCutTile predicates. The destination must hold the whole traversed rectangle so the method
    /// cannot return a silently truncated mutation plan.
    /// </summary>
    public static int CollectCandidates(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int boxWidth,
        int boxHeight,
        Span<VanillaProjectileTileCutCandidate> destination)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        WorldTileBounds bounds = GetCutBounds(tiles.Dimensions, positionX, positionY, boxWidth, boxHeight);
        int traversalCount = checked(bounds.Width * bounds.Height);
        if (destination.Length < traversalCount)
            throw new ArgumentException("Destination must hold the complete projectile CutTilesAt traversal.", nameof(destination));

        int written = 0;
        for (int x = bounds.X; x < bounds.ExclusiveRight; x++)
        {
            for (int y = bounds.Y; y < bounds.ExclusiveBottom; y++)
            {
                if (!IsCutCandidate(tiles, x, y))
                    continue;

                destination[written++] = new VanillaProjectileTileCutCandidate(x, y);
            }
        }

        return written;
    }

    /// <summary>
    /// Mirrors the ordinary CutTilesAt guard followed by WorldGen.CanCutTile(..., AttackProjectile).
    /// Projectile-specific exceptions for types 1047/1052 are intentionally absent because the current
    /// supported thrown family (3/48/54/599) never enters those branches.
    /// </summary>
    public static bool IsCutCandidate(WorldTileStore tiles, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)y >= (uint)tiles.Dimensions.HeightTiles)
        {
            return false;
        }

        WorldTile tile = tiles.Get(x, y);
        if (!tile.IsActive || !VanillaProjectileTileCutFacts.IsCuttable(tile.TileType))
            return false;

        return CanCutTile(tiles, x, y, in tile);
    }

    private static WorldTileBounds GetBoundsFromPixelEdges(
        WorldDimensions dimensions,
        double leftPixels,
        double topPixels,
        double rightPixels,
        double bottomPixels)
    {
        int left = ProjectStart(leftPixels, dimensions.WidthTiles);
        int right = ProjectEnd(rightPixels, dimensions.WidthTiles);
        int top = ProjectStart(topPixels, dimensions.HeightTiles);
        int bottom = ProjectEnd(bottomPixels, dimensions.HeightTiles);

        if (right < left)
            right = left;
        if (bottom < top)
            bottom = top;

        return new WorldTileBounds(left, top, right - left, bottom - top);
    }

    private static int ProjectStart(double pixels, int maximumTiles)
    {
        if (pixels <= 0d)
            return 0;

        double maximumPixels = maximumTiles * TileSizePixels;
        if (pixels >= maximumPixels)
            return maximumTiles;

        return (int)(pixels / TileSizePixels);
    }

    private static int ProjectEnd(double pixels, int maximumTiles)
    {
        if (pixels < -TileSizePixels)
            return 0;

        double maximumPixels = maximumTiles * TileSizePixels;
        if (pixels >= maximumPixels)
            return maximumTiles;

        return Math.Clamp((int)(pixels / TileSizePixels) + 1, 0, maximumTiles);
    }

    private static void ValidateBox(float positionX, float positionY, int boxWidth, int boxHeight)
    {
        if (!float.IsFinite(positionX))
            throw new ArgumentOutOfRangeException(nameof(positionX));
        if (!float.IsFinite(positionY))
            throw new ArgumentOutOfRangeException(nameof(positionY));
        ArgumentOutOfRangeException.ThrowIfLessThan(boxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(boxHeight, 1);
    }

    private static bool CanCutTile(WorldTileStore tiles, int x, int y, in WorldTile tile)
    {
        // WorldGen.CanCutTile reads y + 1. The normalized runtime store has no out-of-range sentinel tile,
        // therefore the final world row cannot satisfy the source predicate safely.
        if (y + 1 >= tiles.Dimensions.HeightTiles || tile.Wall == ProtectedWallType)
            return false;

        WorldTile below = tiles.Get(x, y + 1);
        if (below.Type is 78 or 380 or 579)
            return false;

        if (tile.Type == SpecialCuttableTileType)
            return tile.FrameX >= SpecialCuttableMinimumFrameX;

        return true;
    }
}
