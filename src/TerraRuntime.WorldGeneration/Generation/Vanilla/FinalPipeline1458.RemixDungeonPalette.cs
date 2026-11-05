using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // Bounded WorldGen.FinishRemixWorld dungeon paint subset from TerrariaServer
    // 1.4.5.8. Tile 41 belongs to Main.tileDungeon, 481 to the cracked-brick
    // set, and wall 7 to Main.wallDungeon. Other families remain unadmitted.
    internal static void FinishRemixDungeonPaletteSparse(Workspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.VanillaSeedProfile.Special != VanillaSpecialWorldSeed1458.Remix ||
            workspace.VanillaSeedProfile.Secret != VanillaSecretWorldSeed1458.None ||
            workspace.VanillaBootstrapState is not { EffectiveCrimson: false })
            throw new NotSupportedException("This dungeon palette slice admits ordinary Corrupt Remix only.");
        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;

        // Reject unowned paint branches before changing any cell.
        for (int x = 10; x < width - 10; x++)
        for (int y = 10; y < height - 10; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type is not (1 or 2 or 5 or 19 or 41 or 61 or 137 or 226 or 481 or 596 or 616 or 637) ||
                tile.Wall is not (0 or 7 or 34 or 87 or 178 or 180 or 244))
                throw new NotSupportedException("Remix dungeon palette needs another tile/wall family owner.");
        }
        for (int x = 10; x < width - 10; x++)
        for (int y = 10; y < height - 10; y++)
        {
            ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
            if (tile.IsActive && tile.Type is (41 or 481))
                tile.TileColor = 22;
            if (tile.Wall != 7)
                continue;
            tile.WallColor = 25;
            if (tile.Type == 19 && tile.FrameY != 180)
                tile.TileColor = 22;
        }
    }
}
