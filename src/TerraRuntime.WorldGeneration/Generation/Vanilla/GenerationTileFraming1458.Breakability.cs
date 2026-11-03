using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>
    /// WorldGen.CheckTileBreakability in a fresh generation candidate (hardMode is false).
    /// The return distinguishes removal, the dust/framing failure lane, and an immediate refusal.
    /// </summary>
    internal int TileBreakability(int x, int y)
    {
        if (y < 1 || y >= height - 1)
            return 0;
        WorldTile target = At(x, y);
        if (At(x, y + 1).IsActive && DoorIsLocked(At(x, y + 1)))
            return 2;
        if (!IsFramingSolid(target.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(target.TileType))
            return 0;

        WorldTile above = At(x, y - 1);
        if (above.IsActive)
        {
            if (BlocksRemoval(target.Type, above, !IsBoulderContainer(target.Type)))
            {
                if (BreakabilityTreeType(above.Type))
                    return above.FrameX == 66 && above.FrameY is >= 0 and <= 44 ||
                        above.FrameX == 88 && above.FrameY is >= 66 and <= 110 || above.FrameY >= 198 ? 0 : 2;
                if (above.Type == 323)
                    return above.FrameX is 66 or 220 ? 2 : 0;
                return 2;
            }
            if (above.Type == 80 && target.Type != 80 && above.FrameX / 18 is 0 or 1 or 4 or 5)
                return 2;
            if (DoorIsLocked(target))
                return 1;
            if (GenerationObjectSupport1458.IsBoulder(target.Type))
                return BoulderHasProtectedObject(x, y, target) ? 1 : 0;
        }

        if (target.Type == 235)
        {
            int left = x - target.FrameX % 54 / 18;
            for (int column = 0; column < 3; column++)
                if (Contains(left + column, y - 1) && At(left + column, y - 1).IsActive &&
                    BlocksRemoval(target.Type, At(left + column, y - 1), scanContainers: true))
                    return 2;
        }
        return 0;
    }

    private bool BoulderHasProtectedObject(int x, int y, in WorldTile boulder)
    {
        int offset = -boulder.FrameX / 18;
        if (offset < -1)
            offset += 2;
        int left = x + offset;
        int row = boulder.FrameY;
        if (row >= 36)
            row %= 36;
        int top = y - row / 18;
        for (int column = 0; column < 2; column++)
            // CheckBoulderChest deliberately reads types even on an inactive cell.
            if (Contains(left + column, top - 1) &&
                BlocksRemoval(boulder.Type, At(left + column, top - 1), scanContainers: true))
                return true;
        return false;
    }

    private static bool BlocksRemoval(ushort ignoredType, in WorldTile above, bool scanContainers) =>
        ignoredType != above.Type && (above.Type == 77 || above.Type is
            5 or 323 or 72 or 488 or 26 or 583 or 584 or 585 or 586 or 587 or 588 or 589 or
            596 or 616 or 470 or 475 or 634) ||
        DoorIsLocked(above) || scanContainers && IsBoulderContainer(above.Type);

    private static bool BreakabilityTreeType(ushort type) => type is
        5 or 72 or 583 or 584 or 585 or 586 or 587 or 588 or 589 or 596 or 616 or 634;
}
