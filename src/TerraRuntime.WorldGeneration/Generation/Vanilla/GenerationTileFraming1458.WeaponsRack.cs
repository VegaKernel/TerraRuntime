using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private void FrameWeaponsRack(int i, int j)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int left = i - observed.FrameX / 18 % 3;
        int top = j - observed.FrameY / 18 % 3;
        if (!Contains(left, top) || !Contains(left + 2, top + 2))
            return;
        bool broken = false;
        for (int x = left; x < left + 3; x++)
        for (int y = top; y < top + 3; y++)
        {
            WorldTile tile = At(x, y);
            broken |= !tile.IsActive || tile.Type != 471 || tile.Wall == 0;
        }
        if (!broken)
            return;
        TryEmptyGenerationItemHolderAt(left, top, WorldTileEntityKind.WeaponsRack);
        destroyingObject = true;
        for (int x = left; x < left + 3; x++)
        for (int y = top; y < top + 3; y++)
            if (At(x, y).IsActive && At(x, y).Type == 471)
                KillTile(x, y);
        RemoveOwnedTileEntity(left, top, WorldTileEntityKind.WeaponsRack);
        destroyingObject = false;
    }
}
