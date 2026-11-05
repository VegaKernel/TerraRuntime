using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // Narrow source WorldGen.AddTrees(undergroundOnly:true) slice. On this
    // admitted material set, GrowTree(5) fails its ground test at every cell.
    // Other substrates need the real tree-growth owner and are refused.
    internal static void FinishRemixAddTreesNoGrowth(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        RequireOrdinaryCorruptRemix(workspace);
        if (!workspace.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Remix AddTrees needs terrain layers.");
        WorldTileStore store = workspace.TileStore;
        for (int x = 1; x < workspace.WidthTiles - 1; x++)
        for (int y = (int)layers.WorldSurface; y < workspace.HeightTiles - 300; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type is not (1 or 61 or 137 or 226))
                throw new NotSupportedException("Remix AddTrees needs source tree-growth behavior on this substrate.");
        }
        _ = random.NextDouble();
        _ = random.NextDouble();
    }

    // Source palette subset after AddTrees in WorldGen.FinishRemixWorld.
    // Dungeon-brick/cracked-brick and combined secret-seed paint rules remain
    // outside this owner; reject them instead of silently leaving them bare.
    internal static void FinishRemixPaletteSparse(Workspace workspace,
        CancellationToken cancellationToken)
    {
        RequireOrdinaryCorruptRemix(workspace);
        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        for (int x = 10; x < width - 10; x++)
        for (int y = 10; y < height - 10; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type is not (1 or 2 or 5 or 19 or 41 or 61 or 137 or 226 or 481 or 596 or 616 or 637) ||
                tile.Wall is not (0 or 7 or 34 or 87 or 178 or 180 or 244))
                throw new NotSupportedException("Remix palette needs a broader tile/wall family owner.");
        }
        for (int x = 10; x < width - 10; x++)
        for (int y = 10; y < height - 10; y++)
        {
            ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
            if (tile.IsActive && (tile.Type == 226 ||
                tile.Type == 61 && store.Get(x, y + 1).Type == 226 ||
                tile.Type == 137 && tile.FrameY / 18 is >= 1 and <= 4))
                tile.TileColor = 22;
            if (tile.Wall is 244 or 34 or 87)
                tile.WallColor = 25;
            if (tile.Wall == 87)
                tile.TileColor = 22;
        }
    }

    private static void RequireOrdinaryCorruptRemix(Workspace workspace)
    {
        if (workspace.VanillaSeedProfile.Special != VanillaSpecialWorldSeed1458.Remix ||
            workspace.VanillaSeedProfile.Secret != VanillaSecretWorldSeed1458.None ||
            workspace.VanillaBootstrapState is not { EffectiveCrimson: false })
            throw new NotSupportedException("This Remix palette slice admits ordinary Corrupt Remix only.");
    }
}
