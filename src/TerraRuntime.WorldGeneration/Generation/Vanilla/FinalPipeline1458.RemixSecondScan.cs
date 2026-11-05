using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    // Source WorldGen.FinishRemixWorld's second x/y scan. This admitted sparse slice
    // preserves its two unconditional draws per cell and the Drunk split draw.
    // Stone1 with wall0/1, grass2/wall0 and inactive natural wall1 are admitted Convert candidates;
    // broader tile/wall families still need an explicit source fixture.
    internal static void FinishRemixSecondScanSparse(Workspace workspace, int deepestCloud,
        IWorldGenerationVanillaRandom random, GenerationTileFraming1458 framing,
        CancellationToken cancellationToken)
    {
        VanillaWorldSeedProfile1458 profile = workspace.VanillaSeedProfile;
        if (!profile.Has(VanillaSpecialWorldSeed1458.Remix) ||
            profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) ||
            profile.Has(VanillaSpecialWorldSeed1458.NotTheBees) ||
            profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10))
            throw new NotSupportedException("This Remix second-scan slice admits ordinary or Drunk Remix only.");
        if (!workspace.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Remix second scan requires source terrain layers.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Remix second scan requires retained evil-side facts.");
        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        bool drunk = profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld);
        bool errorWorld = profile.Has(VanillaSecretWorldSeed1458.IAmError);
        bool surfaceInSpace = profile.Has(VanillaSecretWorldSeed1458.SuchGreatHeights);

        // Admit the whole candidate before touching any cell or RNG. Convert is called
        // even on ordinary air, but only these known inert inputs are source-qualified.
        for (int x = 25; x < width - 25; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 25; y < height - 25 && y < layers.WorldSurface + 2; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive && tile.Type is not (1 or 2 or 56) ||
                    tile.IsActive && tile.Type == 56 && tile.Wall == 1 ||
                    tile.IsActive && tile.Type == 1 && tile.Wall is not (0 or 1) ||
                    tile.IsActive && tile.Type == 2 && tile.Wall != 0 ||
                    tile.Wall is not (0 or 1 or 87 or 178 or 180))
                    throw new NotSupportedException("Remix conversion needs WorldGen.Convert for this occupied cell.");
            }
        }

        var converter = new GenerationWorldConvert1458(store, random, framing);
        for (int x = 25; x < width - 25; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 25; y < height - 25; y++)
            {
                ref WorldTile tile = ref At(x, y);
                if ((y > deepestCloud || errorWorld || surfaceInSpace) &&
                    y < layers.WorldSurface && x > 380 && x < width - 380 &&
                    tile.Wall == 87 && tile.LiquidAmount > 0)
                    tile.LiquidKind = WorldLiquidKind.Water;

                bool convertOffer = y < layers.WorldSurface + random.Next(3);
                int conversionType = bootstrap.EffectiveCrimson ? 4 : 1;
                if (convertOffer && drunk)
                {
                    // WorldGen chooses corruption/crimson even when Convert immediately
                    // returns for an inactive empty cell. The choice consumes one draw.
                    bool left = x < width / 2 + random.Next(-2, 3);
                    conversionType = left == bootstrap.CrimsonLeft ? 4 : 1;
                }
                // Source Convert is inert for the other admitted sparse identities.
                if (convertOffer && (tile.IsActive && tile.Type is (1 or 2) ||
                    !tile.IsActive && tile.Wall == 1))
                    converter.ConvertAt(x, y, conversionType, rainbowSandAndBlackSandWalls: false);
                if (y < layers.WorldSurface - random.Next(19, 22) && tile.Wall is 178 or 180)
                    tile.Wall = 0;
                if (tile.IsActive && tile.Type == 56)
                    framing.KillTile(x, y);
                if (y > layers.RockLayer && y < height - 350 && tile.IsActive && tile.Type == 0 &&
                    (!At(x - 1, y - 1).IsActive || !framing.SolidTile(x, y - 1) ||
                     !At(x + 1, y - 1).IsActive || !At(x - 1, y).IsActive ||
                     !At(x + 1, y).IsActive || !At(x - 1, y + 1).IsActive ||
                     !At(x, y + 1).IsActive || !At(x + 1, y + 1).IsActive))
                    tile.Type = 2;
            }
        }

        ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }
}
