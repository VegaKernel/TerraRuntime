using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Source platform cosmetic branch; physical half/slope bits remain independent.</summary>
internal static class VanillaToolPlatformFrames1458
{
    public static bool TryPlan(in WorldTile tile, ReadOnlySpan<WorldTile> window,
        bool showInvisible, out short frameX)
    {
        frameX = 0;
        if (window.Length != 9) return false;
        WorldTile up = window[1], left = window[3], right = window[5];
        WorldTile bottomLeft = window[6], bottomRight = window[8], topLeft = window[0], topRight = window[2];
        if (!TryFacts(in left, out var lf) || !TryFacts(in right, out var rf) ||
            !TryFacts(in bottomLeft, out var blf) || !TryFacts(in bottomRight, out var brf) ||
            !TryFacts(in topLeft, out var tlf) || !TryFacts(in topRight, out var trf)) return false;
        bool cLeft = Cull(in tile, in left, showInvisible), cRight = Cull(in tile, in right, showInvisible);
        bool cTopLeft = Cull(in tile, in topLeft, showInvisible), cTopRight = Cull(in tile, in topRight, showInvisible);
        bool cBottomLeft = Cull(in tile, in bottomLeft, showInvisible), cBottomRight = Cull(in tile, in bottomRight, showInvisible);
        int l = Classify(in left, lf, cLeft), r = Classify(in right, rf, cRight);
        if (l == 1 && left.IsHalfBrick != tile.IsHalfBrick) l = -1;
        if (r == 1 && right.IsHalfBrick != tile.IsHalfBrick) r = -1;
        if (tile.IsHalfBrick) { if (l == 0) l = -1; if (r == 0) r = -1; }
        if (l == -1 && topLeft.IsActive && topLeft.Type == tile.Type && topLeft.SourceSlope == 1 && !cTopLeft) l = 1;
        if (r == -1 && topRight.IsActive && topRight.Type == tile.Type && topRight.SourceSlope == 2 && !cTopRight) r = 1;
        if (l == 1 && left.SourceSlope == 2 && r != 1) r = -1;
        if (r == 1 && right.SourceSlope == 1 && l != 1) l = -1;
        bool forbids = up.Type is 21 or 26 or 77 or 88 or 235 or 237 or 441 or 467 or 468 or 470 or 475 or 488 or 597;
        int x;
        if (tile.SourceSlope == 1)
        {
            bool fullRightPlatform = rf.IsPlatform && right.SourceSlope == 0 && !right.IsHalfBrick && !cRight;
            bool lowerOpen = (!bottomRight.IsActive || cBottomRight) &&
                (!brf.IsPlatform || bottomRight.SourceSlope == 2 || cBottomRight);
            bool leftAttached = (left.IsActive && !cLeft) || (tlf.IsPlatform && topLeft.SourceSlope == 1 && !cTopLeft);
            x = fullRightPlatform || forbids ? 468 : lowerOpen ? leftAttached ? 360 : 432 : leftAttached ? 180 : 396;
        }
        else if (tile.SourceSlope == 2)
        {
            bool fullLeftPlatform = lf.IsPlatform && left.SourceSlope == 0 && !left.IsHalfBrick && !cLeft;
            bool lowerOpen = (!bottomLeft.IsActive || cBottomLeft) &&
                (!blf.IsPlatform || bottomLeft.SourceSlope == 1 || cBottomLeft);
            bool rightAttached = (right.IsActive && !cRight) || (trf.IsPlatform && topRight.SourceSlope == 2 && !cTopRight);
            x = fullLeftPlatform || forbids ? 450 : lowerOpen ? rightAttached ? 342 : 414 : rightAttached ? 144 : 378;
        }
        else if (l == 1 && r == 1)
            x = left.SourceSlope == 2 && right.SourceSlope == 1 ? 252 : left.SourceSlope == 2 ? 216 : right.SourceSlope == 1 ? 234 : 0;
        else if (l == 1 && r == -1) x = left.SourceSlope == 2 ? 270 : 18;
        else if (l == -1 && r == 1) x = right.SourceSlope == 1 ? 288 : 36;
        else if (l != 1 && r == 1) x = 54;
        else if (l == 1 && r != 1) x = 72;
        else if (l is not (-1 or 1) && r == -1) x = 108;
        else if (l == -1 && r is not (-1 or 1)) x = 126;
        else x = 90;
        frameX = checked((short)x);
        return true;
    }

    private static bool TryFacts(in WorldTile tile, out ToolTileDefinition1458 facts) =>
        VanillaToolTileCatalog1458.TryGet(tile.TileType, out facts);
    private static bool Cull(in WorldTile tile, in WorldTile other, bool showInvisible) =>
        !showInvisible && tile.IsBlockInvisible != other.IsBlockInvisible;
    // -1 absent/non-solid, 0 other solid, 1 platform. Source stone aliases
    // cannot become a platform, so their numeric replacement does not alter this partition.
    private static int Classify(in WorldTile tile, in ToolTileDefinition1458 facts, bool culled) =>
        !tile.IsActive || culled || (!facts.IsPlatform && !facts.IsSolid) ? -1 : facts.IsPlatform ? 1 : 0;
}
