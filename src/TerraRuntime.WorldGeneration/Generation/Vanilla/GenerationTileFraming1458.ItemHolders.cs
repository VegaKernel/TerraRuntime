using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private void FrameFoodPlatter(int x, int y)
    {
        if (destroyingObject || !Contains(x, y + 1) || SolidTileAllowBottomSlope(x, y + 1))
            return;
        TryEmptyGenerationItemHolder(x, y, At(x, y));
        destroyingObject = true;
        if (At(x, y).Type == 520)
            KillTile(x, y);
        RemoveOwnedTileEntity(x, y, WorldTileEntityKind.FoodPlatter);
        destroyingObject = false;
    }

    // KillTile empties a held item and returns before dust or tile removal. Item.NewItem
    // itself returns immediately while generating/loading; no dropped-item actor is born.
    private bool TryEmptyGenerationItemHolder(int i, int j, WorldTile tile)
    {
        WorldTileEntityKind kind;
        int x = i, y = j;
        switch (tile.Type)
        {
            case 395:
                kind = WorldTileEntityKind.ItemFrame;
                x -= tile.FrameX % 36 / 18; y -= tile.FrameY % 36 / 18;
                break;
            case 471:
                kind = WorldTileEntityKind.WeaponsRack;
                x -= tile.FrameX % 54 / 18; y -= tile.FrameY % 54 / 18;
                break;
            case 520:
                kind = WorldTileEntityKind.FoodPlatter;
                break;
            case 698:
                kind = WorldTileEntityKind.DeadCellsDisplayJar;
                x -= tile.FrameX % 18 / 18; y -= tile.FrameY % 32 / 18;
                break;
            default:
                return false;
        }
        return TryEmptyGenerationItemHolderAt(x, y, kind);
    }

    private bool TryEmptyGenerationItemHolderAt(int x, int y, WorldTileEntityKind kind)
    {
        var owner = store.RuntimeTileEntities ?? throw new NotSupportedException("Generation item holders require their canonical furniture owner.");
        if (!owner.TryPrepare(1, out var preparation) || preparation is null ||
            !preparation.TryGetAt(x, y, out var entity))
            throw new InvalidOperationException("Could not inspect the generation item holder.");
        if (entity is null || entity.Kind != kind || entity.Payload is not WorldItemTileEntityPayload held || held.Item.Stack <= 0)
        {
            return false;
        }
        WorldTileEntityItem expected = held.Item;
        if (!preparation.TryStageItemHolderClear(x, y, kind, in expected) || !preparation.TrySeal())
            throw new InvalidOperationException("Could not stage the generation item reset.");
        Span<WorldSectionId> sections = stackalloc WorldSectionId[1];
        sections[0] = TerrariaSectionGeometry.FromTile(store.Dimensions, x, y);
        store.BeginMetadataMutation(sections);
        try
        {
            if (!preparation.TryAdoptUnpublished())
                throw new InvalidOperationException("Generation item holder changed during adoption.");
        }
        finally { store.EndMetadataMutation(sections); }
        return true;
    }
}
