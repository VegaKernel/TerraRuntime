using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Tiles;

public readonly record struct ToolSingleCellFrameTile1458(TileTypeId Type, short FrameX,
    bool Active, bool Inactive, bool HalfBrick, byte Slope);

// Original1458 TileFrameImportant: CheckOnTable1x1(49) and switch136.
// This is only the source anchor/frame decision, not permission to remove an object.
public static class VanillaToolSingleCellFrame1458
{
    // TileFrame's platform atlas cell stride (1.4.5.8).
    private const int PlatformFrameStep = 18;
    public static bool TryPlan(TileTypeId type, short frameX, ushort wall,
        ReadOnlySpan<ToolSingleCellFrameTile1458> window, out short afterFrameX, out bool kill)
    {
        afterFrameX = frameX; kill = false;
        if (window.Length != 9 || type.Value is not (49 or 136)) return false;
        foreach (var cell in window)
            if (cell.Slope > 4 || !VanillaToolTileCatalog1458.TryGet(cell.Type, out _)) return false;
        var below = window[7];
        if (type.Value == 49)
        {
            var facts = Facts(below);
            if (below.Slope is 1 or 2)
            {
                bool paired = below.Slope == 2 && PlatformFull(window[6]) || below.Slope == 1 && PlatformFull(window[8]);
                kill = !facts.IsPlatform || !paired;
            }
            else if (below.HalfBrick) kill = true;
            else kill = !TableAnchor(below) && !(below.Active && IsTable(below.Type));
            return true;
        }
        var left = window[3]; var right = window[5];
        if (NActive(below) && !below.HalfBrick && below.Slope is not (1 or 2) &&
            Facts(below).IsSolid && !Facts(below).NoAttach && (below.Slope == 0 || below.Slope is 3 or 4))
            afterFrameX = 0;
        else if (Side(left, true) || Beam(left) || Tree(left) && Tree(window[0]) && Tree(window[6])) afterFrameX = 18;
        else if (Side(right, false) || Beam(right) || Tree(right) && Tree(window[2]) && Tree(window[8])) afterFrameX = 36;
        else if (wall > 0) afterFrameX = 54;
        else kill = true;
        return true;
    }

    private static ToolTileDefinition1458 Facts(in ToolSingleCellFrameTile1458 tile)
    { VanillaToolTileCatalog1458.TryGet(tile.Type, out var facts); return facts; }
    private static bool NActive(in ToolSingleCellFrameTile1458 tile) => tile.Active && !tile.Inactive;
    private static bool PlatformFull(in ToolSingleCellFrameTile1458 tile) => tile.Active && !tile.HalfBrick && tile.Slope == 0 && Facts(tile).IsPlatform;
    private static bool TableAnchor(in ToolSingleCellFrameTile1458 tile)
    {
        if (!NActive(tile)) return false;
        var facts = Facts(tile);
        if (facts.IsPlatform)
        {
            int frame = tile.FrameX / PlatformFrameStep;
            return !tile.HalfBrick && frame is >= 0 and <= 7 || frame is >= 12 and <= 16 or >= 25 and <= 26;
        }
        return facts.IsSolid && facts.IsSolidTop || IsTable(tile.Type) && !tile.HalfBrick && tile.Slope == 0;
    }
    private static bool Side(in ToolSingleCellFrameTile1458 tile, bool left) => NActive(tile) &&
        Facts(tile).IsSolid && !Facts(tile).NoAttach && !tile.HalfBrick &&
        (tile.Slope == 0 || (left ? tile.Slope is 2 or 4 : tile.Slope is 1 or 3));
    private static bool Beam(in ToolSingleCellFrameTile1458 tile) => NActive(tile) && VanillaToolFurniture1458.IsBeam(tile.Type);
    private static bool Tree(in ToolSingleCellFrameTile1458 tile) => NActive(tile) && Facts(tile).IsTreeTrunk;
    // Main.tileTable, all754 initialized identities, independently captured from both official editions.
    private static bool IsTable(TileTypeId type) => type.Value is
        14 or 18 or 19 or 87 or 88 or 101 or 114 or 275 or 276 or 277 or 278 or 279 or 280 or 281 or
        285 or 286 or 296 or 297 or 298 or 299 or 309 or 310 or 339 or 358 or 359 or 361 or 362 or 363 or
        364 or 376 or 380 or 391 or 392 or 393 or 394 or 405 or 413 or 414 or 427 or 435 or 436 or 437 or
        438 or 439 or 469 or 532 or 533 or 538 or 542 or 544 or 550 or 551 or 553 or 554 or 555 or 556 or
        558 or 559 or 582 or 599 or 600 or 601 or 602 or 603 or 604 or 605 or 606 or 607 or 608 or 609 or
        610 or 611 or 612 or 619 or 629 or 632 or 640 or 643 or 644 or 645 or 710;
}
