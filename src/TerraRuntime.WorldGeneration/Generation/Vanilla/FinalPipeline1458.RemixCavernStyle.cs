using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // TerrariaServer.exe 1.4.5.8 WorldGen.FinishRemixWorld final tile637 band.
    // The source uses genRand for the band and Main.rand for each selected
    // frame. Keep both streams explicit even when an official helper harness
    // configures them as the same UnifiedRandom instance.
    internal static void FinishRemixCavernStyle637(WorldTileStore store,
        IWorldGenerationVanillaRandom genRandom,
        IWorldGenerationVanillaRandom mainRandom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(genRandom);
        ArgumentNullException.ThrowIfNull(mainRandom);
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (width < 1000 || height < 300)
            throw new ArgumentOutOfRangeException(nameof(store));

        double bands = width / 4200d * genRandom.Next(2, 5);
        for (int band = 0; band < bands; band++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int center = genRandom.Next((int)(width * 0.39), (int)(width * 0.61));
            int radius = genRandom.Next(10, 31);
            for (int x = center - radius; x <= center + radius; x++)
            for (int y = height - 250; y < height - 25; y++)
            {
                ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
                if (tile.Type == 637)
                    tile.FrameX = (short)(18 * mainRandom.Next(6, 11));
            }
        }
    }
}
