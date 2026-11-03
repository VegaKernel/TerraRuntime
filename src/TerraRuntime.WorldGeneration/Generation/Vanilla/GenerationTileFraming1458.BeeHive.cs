using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool HasWetBeeHiveFootprint(int i, int j)
    {
        WorldTile tile = At(i, j);
        int left = i - tile.FrameX / 18 % 2;
        int top = j - tile.FrameY / 18 % 2;
        if (!Contains(left - 1, top - 1) || !Contains(left + 2, top + 2))
            return false;
        for (int x = left; x < left + 2; x++)
        for (int y = top; y < top + 2; y++)
            if (At(x, y).LiquidAmount > 0)
                return true;
        return false;
    }

    // Source CheckSuper444 scans liquid even on an inactive member already cleared by KillTile.
    // The dry invalid arm needs its canonical projectile owner and remains outside this wet admission.
    private void FrameBeeHive(int i, int j)
    {
        if (destroyingObject)
            return;
        WorldTile tile = At(i, j);
        int left = i - tile.FrameX / 18 % 2;
        int top = j - tile.FrameY / 18 % 2;
        int styleX = tile.FrameX / 36 * 36;
        if (!Contains(left - 1, top - 1) || !Contains(left + 2, top + 2))
            return;
        bool broken = false;
        bool wet = false;
        for (int x = 0; x < 2; x++)
        {
            broken |= !SpecialFlatAnchor(At(left + x, top - 1));
            for (int y = 0; y < 2; y++)
            {
                WorldTile member = At(left + x, top + y);
                broken |= !member.IsActive || member.Type != 444 ||
                    member.FrameX != styleX + x * 18 || member.FrameY != y * 18;
                wet |= member.LiquidAmount > 0;
            }
        }
        if (!broken && !wet)
            return;
        if (!wet)
            throw new NotSupportedException("Dry bee hive destruction requires the canonical generation projectile owner.");
        destroyingObject = true;
        for (int x = left; x < left + 2; x++)
        for (int y = top; y < top + 2; y++)
            if (At(x, y).IsActive && At(x, y).Type == 444)
                KillTile(x, y);
        destroyingObject = false;
        for (int x = left - 1; x < left + 3; x++)
        for (int y = top - 1; y < top + 3; y++)
            TileFrame(x, y);
    }
}
