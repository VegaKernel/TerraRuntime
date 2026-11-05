using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // First scan of TerrariaServer.exe 1.4.5.8 WorldGen.FinishRemixWorld only.
    // The following Convert/QuickWater/underground-tree scans remain separate work.
    internal static int FinishRemixFirstScan(Workspace workspace, IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken = default)
    {
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        if (!profile.Has(VanillaSpecialWorldSeed1458.Remix))
            throw new ArgumentException("The Remix first scan requires a Remix generation profile.", nameof(workspace));
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Remix scan requires retained bootstrap evil and side facts.");
        bool anniversary = profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10);
        bool worthy = profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy);
        bool drunk = profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        WorldTileStore store = workspace.TileStore;
        int deepestCloud = 0;
        for (int x = 25; x < width - 25; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 25; y < height - 25; y++)
            {
                ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
                if (tile.IsActive && (tile.Type is 189 or 196 or 460 or 717 or 718 or 719 or 202))
                {
                    if (y > deepestCloud)
                        deepestCloud = y;
                    if (!anniversary || worthy)
                        tile.Type = EvilCloudTile(x);
                }
                if (tile.Wall is not (73 or 82) || anniversary && !worthy)
                    continue;
                tile.Wall = EvilCloudWall(x);
            }
        }
        return deepestCloud;

        ushort EvilCloudTile(int x)
        {
            if (!drunk)
                return bootstrap.EffectiveCrimson ? (ushort)195 : (ushort)474;
            bool left = x < width / 2 + random.Next(-2, 3);
            return left == bootstrap.CrimsonLeft ? (ushort)195 : (ushort)474;
        }

        ushort EvilCloudWall(int x)
        {
            if (!drunk)
                return bootstrap.EffectiveCrimson ? (ushort)77 : (ushort)233;
            bool left = x < width / 2 + random.Next(-2, 3);
            return left == bootstrap.CrimsonLeft ? (ushort)77 : (ushort)233;
        }
    }
}
