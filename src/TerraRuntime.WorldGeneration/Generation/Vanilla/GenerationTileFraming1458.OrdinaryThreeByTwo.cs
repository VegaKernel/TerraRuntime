using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>Generation-time Check3x2 for ordinary furniture and small cages, Terraria1.4.5.8.</summary>
    private void CheckOrdinaryThreeByTwo(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int column = observed.FrameX / 18;
        // The source subtracts style bands only while positive; signed invalid frames retain their origin.
        int style = column > 2 ? column / 3 : 0;
        int left = i - column + style * 3;
        int styleRow = observed.FrameY / 36;
        int top = j - observed.FrameY % 36 / 18;
        int objectHeight = type == 14 && style == 25 ? 1 : 2;
        int floor = top + objectHeight;
        if (!Contains(left - 1, top - 1) || !Contains(left + 3, top + 3))
            return;
        bool broken = !HasLargeObjectFrames(left, top, 3, objectHeight, type, style * 54, styleRow * 36, backingWall: false);
        for (int x = left; x < left + 3; x++)
        {
            // The later, unconditional source arm enforces this even for the small cages whose
            // earlier branch also tests solid-top/platform alternatives. Both checks must hold.
            WorldTile support = At(x, floor);
            if (!SolidTileAllowBottomSlope(x, floor) ||
                type == 26 && support.IsActive && GenerationObjectSupport1458.IsBoulder(support.Type))
                broken = true;
        }
        if (!broken)
            return;
        RemoveLargeFramedObject(left, top, 3, objectHeight, type);
        // Type26's SmashAltar and all item births are suppressed by the generating-world guards.
        // Check3x2 still frames five rows even for the one-row table variant.
        for (int x = left - 1; x < left + 4; x++)
        for (int y = top - 1; y < top + 4; y++)
            TileFrame(x, y);
    }
}
