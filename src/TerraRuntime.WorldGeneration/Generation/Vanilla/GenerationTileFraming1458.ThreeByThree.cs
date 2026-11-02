using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>
    /// Generation-time WorldGen.Check3x3 for its 28 non-Larva identities in TerrariaServer 1.4.5.8.
    /// Every dispatched member is floor supported; Giant Tiki additionally accepts another active,
    /// non-actuated Giant Tiki as an anchor. Larva231 retains a separate Hive-solidity/boss boundary.
    /// </summary>
    private void CheckFloorThreeByThree(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int column = observed.FrameX / 18;
        int styleX = column >= 3 ? column / 3 * 54 : 0;
        int left = i - column + styleX / 18;
        int top = j - observed.FrameY % 54 / 18;
        int styleY = observed.FrameY / 54 * 54;
        if (!Contains(left - 1, top - 1) || !Contains(left + 3, top + 3))
            return;

        bool broken = !HasThreeByThreeFrames(left, top, type, styleX, styleY, requireWalls: false);
        for (int x = left; x < left + 3; x++)
        {
            WorldTile support = At(x, top + 3);
            bool stackedTiki = type == 753 && support.IsActive && !support.IsActuated && support.Type == 753;
            if (!SolidTileAllowBottomSlope(x, top + 3) && !stackedTiki)
                broken = true;
        }
        if (!broken)
            return;

        RemoveThreeByThree(left, top, type);
        // Check3x3 lowers destroyObject before this 5x5 halo. Neighbouring objects can therefore
        // be invalidated immediately, even when the outer generation scan has already visited them.
        for (int x = left - 1; x < left + 4; x++)
        for (int y = top - 1; y < top + 4; y++)
            TileFrame(x, y);
    }

    /// <summary>WorldGen.Check3x3Wall for paintings240 and440; its destruction has no reframe halo.</summary>
    private void CheckWallThreeByThree(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int column = observed.FrameX / 18;
        int row = observed.FrameY / 18;
        int styleX = column >= 3 ? column / 3 * 54 : 0;
        int styleY = row >= 3 ? row / 3 * 54 : 0;
        int left = i - column + styleX / 18;
        int top = j - row + styleY / 18;
        if (!Contains(left, top) || !Contains(left + 2, top + 2) ||
            HasThreeByThreeFrames(left, top, type, styleX, styleY, requireWalls: true))
            return;
        RemoveThreeByThree(left, top, type);
    }

    private bool HasThreeByThreeFrames(int left, int top, ushort type, int styleX, int styleY, bool requireWalls)
    {
        for (int x = left; x < left + 3; x++)
        for (int y = top; y < top + 3; y++)
        {
            WorldTile part = At(x, y);
            if (!part.IsActive || part.Type != type ||
                part.FrameX != styleX + (x - left) * 18 || part.FrameY != styleY + (y - top) * 18 ||
                requireWalls && part.Wall == 0)
                return false;
        }
        return true;
    }

    private void RemoveThreeByThree(int left, int top, ushort type)
    {
        destroyingObject = true;
        for (int x = left; x < left + 3; x++)
        for (int y = top; y < top + 3; y++)
            if (At(x, y).IsActive && At(x, y).Type == type)
                KillTile(x, y);
        destroyingObject = false;
    }
}
