using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private bool TileEntityStandContainsItems(int i, int j, WorldTile observed)
    {
        if (SourceClient)
            return false;
        int left = i;
        if (observed.Type == 470)
        {
            if (observed.FrameX % 36 != 0)
                left--;
        }
        else left -= observed.FrameX % 54 / 18;
        int top = j - At(left, j).FrameY / 18;
        var owner = store.RuntimeTileEntities ?? throw new NotSupportedException("Stand breakability requires its canonical furniture owner.");
        if (!owner.TryPrepare(1, out var inspection) || inspection is null || !inspection.TryGetAt(left, top, out var row))
            throw new InvalidOperationException("Could not inspect source stand breakability.");
        if (observed.Type == 470 && row?.Kind == WorldTileEntityKind.DisplayDoll && row.Payload is WorldDisplayDollPayload doll)
            return doll.Equipment.Any(StandItemPresent) || doll.Dyes.Any(StandItemPresent) || StandItemPresent(doll.Misc);
        if (observed.Type == 475 && row?.Kind == WorldTileEntityKind.HatRack && row.Payload is WorldHatRackPayload hat)
            return hat.Items.Any(StandItemPresent) || hat.Dyes.Any(StandItemPresent);
        return false;
    }

    private static bool StandItemPresent(WorldTileEntityItem? item) => item is { Type: > 0, Stack: > 0 };

    private void FrameTileEntityStand(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        int width = type == 470 ? 2 : 3;
        int height = type == 470 ? 3 : 4;
        WorldTile observed = At(i, j);
        int left = i - observed.FrameX / 18 % width;
        int top = j - observed.FrameY / 18 % height;
        if (!Contains(left, top) || !Contains(left + width - 1, top + height))
            return;
        bool broken = false;
        for (int x = left; x < left + width; x++)
        {
            broken |= !SolidTileAllowBottomSlope(x, top + height);
            for (int y = top; y < top + height; y++)
            {
                WorldTile member = At(x, y);
                broken |= !member.IsActive || member.Type != type;
            }
        }
        if (!broken)
            return;
        // Source removes the entity before its cell kills. Generation suppresses both the
        // equipment drops in GetItemDrops and the stand's Item.NewItem call.
        RemoveOwnedTileEntity(left, top, type == 470 ? WorldTileEntityKind.DisplayDoll : WorldTileEntityKind.HatRack);
        destroyingObject = true;
        for (int x = left; x < left + width; x++)
        for (int y = top; y < top + height; y++)
            if (At(x, y).IsActive && At(x, y).Type == type)
                KillTile(x, y);
        destroyingObject = false;
    }
}
