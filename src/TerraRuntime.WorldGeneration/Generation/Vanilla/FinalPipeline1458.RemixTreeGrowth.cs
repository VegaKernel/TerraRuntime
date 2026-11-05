using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // WorldGen.AddTrees(undergroundOnly:true) inside FinishRemixWorld, TerrariaServer
    // 1.4.5.8. This bounded owner admits grass2 plus materials on which
    // GrowTree(5) fails its ground test; existing growers own actual growth.
    internal static void FinishRemixAddTreesGrassGrowth(Workspace workspace,
        IWorldGenerationVanillaRandom random, GenerationTileFraming1458 framing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(framing);
        if (workspace.VanillaSeedProfile.Special != VanillaSpecialWorldSeed1458.Remix ||
            workspace.VanillaSeedProfile.Secret != VanillaSecretWorldSeed1458.None ||
            !workspace.TryGetLayers(out WorldGenerationLayers layers))
            throw new NotSupportedException("Remix tree growth requires ordinary Remix and retained layers.");
        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        int firstY = (int)layers.WorldSurface;
        int lastY = height - 300;
        if (width < 1000 || firstY < 1 || lastY > height || firstY >= lastY)
            throw new ArgumentOutOfRangeException(nameof(workspace));

        for (int x = 1; x < width - 1; x++)
        for (int y = firstY; y < lastY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type is not (1 or 2 or 19 or 41 or 61 or 137 or 226 or 481))
                throw new NotSupportedException("Remix AddTrees requires another growth substrate owner.");
        }

        _ = random.NextDouble();
        _ = random.NextDouble();
        for (int x = 1; x < width - 1; x++)
        for (int y = firstY; y < lastY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type == 2 && random.Next(7) == 0)
            {
                ushort vanity = random.Next(2) == 0 ? (ushort)616 : (ushort)596;
                SettingsTreeGrower1458.TryGrow(store, SettingsTreeGrower1458.VanityTree(vanity),
                    x, y, random, framing);
            }
            else
                TreeGrower1458.TryGrow(store, x, y, random, framing: framing);
        }
    }
}
