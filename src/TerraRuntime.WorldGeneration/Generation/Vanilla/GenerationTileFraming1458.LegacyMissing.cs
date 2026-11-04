using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    // Main.halloween is an external calendar fact in the source. The ordinary cold generation
    // default is false; callers with an active seasonal world can supply the source value.
    internal bool SourceHalloween { get; set; }

    private void FrameLegacyWeaponsRack(int i, int j)
    {
        if (destroyingObject)
            return;

        WorldTile observed = At(i, j);
        int top = j - observed.FrameY / 18;
        int frameColumn = LegacyWeaponsRackColumn(observed.FrameX);
        int left = i - frameColumn / 18;
        if (!Contains(left, top) || !Contains(left + 2, top + 2))
            return;

        bool broken = false;
        for (int column = 0; column < 3; column++)
            for (int row = 0; row < 3; row++)
            {
                WorldTile part = At(left + column, top + row);
                if (!part.IsActive || part.Type != 334 || part.Wall == 0 ||
                    part.FrameY != row * 18 ||
                    LegacyWeaponsRackColumn(part.FrameX) != column * 18)
                    broken = true;
            }
        if (!broken)
            return;

        // WorldGen.CheckWeaponsRack drops its held item before destroying the footprint.
        // During generation Item.NewItem returns immediately; the canonical TE record is
        // removed together with the object and cannot retain an orphaned held item.
        destroyingObject = true;
        if (store.RuntimeTileEntities is not null)
            RemoveOwnedTileEntity(left, top, WorldTileEntityKind.WeaponsRack);
        for (int column = 0; column < 3; column++)
            for (int row = 0; row < 3; row++)
                if (At(left + column, top + row).IsActive && At(left + column, top + row).Type == 334)
                    KillTile(left + column, top + row);
        destroyingObject = false;
    }

    private static int LegacyWeaponsRackColumn(int frameX)
    {
        int band = 0;
        while (frameX >= 5000)
        {
            frameX -= 5000;
            band++;
        }
        return ((band == 0 ? frameX : (band - 1) * 18) % 54);
    }
}
