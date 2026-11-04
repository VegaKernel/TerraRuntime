using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary CorruptionAndCrimson placement from WorldGen.AddPasses1.4.5.8.
/// Owns surface exclusion discovery and per-biome rejection state, not chasm geometry.</summary>
internal static class EvilBiomePlacement1458
{
    // A cancellation/safety ceiling, not a vanilla coordinate fallback. Failed selection aborts generation.
    internal const int MaximumAttempts = 100_000;
    private const int BeachAvoidance = BootstrapPass1458.BeachSandRandomCenter + 60;

    internal readonly record struct SurfaceBounds(int JungleLeft, int JungleRight, int SnowLeft, int SnowRight);
    internal readonly record struct Region(int Center, int Left, int Right);

    public static SurfaceBounds Scan(WorldTileStore tiles, double worldSurface, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(worldSurface) || worldSurface < 0 || worldSurface > tiles.Dimensions.HeightTiles)
            throw new ArgumentOutOfRangeException(nameof(worldSurface));
        int jungleLeft = tiles.Dimensions.WidthTiles, jungleRight = 0;
        int snowLeft = tiles.Dimensions.WidthTiles, snowRight = 0;
        for (int x = 0; x < tiles.Dimensions.WidthTiles; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < worldSurface; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive) continue;
                if (tile.Type == 60)
                {
                    jungleLeft = Math.Min(jungleLeft, x); jungleRight = Math.Max(jungleRight, x);
                }
                else if (tile.Type is 147 or 161)
                {
                    snowLeft = Math.Min(snowLeft, x); snowRight = Math.Max(snowRight, x);
                }
            }
        }
        return new(jungleLeft - 10, jungleRight + 10, snowLeft - 10, snowRight + 10);
    }

    public static Region Select(int width, SurfaceBounds bounds, int dungeonLocation, int dungeonSide,
        int desertLeft, int desertRight, bool crimson, IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken, VanillaWorldSeedProfile1458 seedProfile = default, bool crimsonLeft = false)
    {
        if (width <= 1000) throw new ArgumentOutOfRangeException(nameof(width));
        bool drunk = seedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld);
        bool remix = seedProfile.Has(VanillaSpecialWorldSeed1458.Remix);
        bool anniversary = seedProfile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) && !seedProfile.Has(VanillaSpecialWorldSeed1458.TheConstant);
        bool ignoreJungle = seedProfile.Has(VanillaSpecialWorldSeed1458.NotTheBees) && !remix;
        int edge = anniversary && !remix ? 1000 : 500, dungeonAvoidance = anniversary && !remix ? 200 : 100;
        int centerAvoidance = drunk && crimson ? 100 : 200;
        int jungleLeft = bounds.JungleLeft, jungleRight = bounds.JungleRight;
        int snowLeft = bounds.SnowLeft, snowRight = bounds.SnowRight;
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int center = drunk
                ? crimson == crimsonLeft ? random.Next(edge, width / 2) : random.Next(width / 2, width - edge)
                : random.Next(edge, width - edge);
            int left = Math.Max(BeachAvoidance, center - random.Next(200) - 100);
            int right = Math.Min(width - BeachAvoidance, center + random.Next(200) + 100);
            center = Math.Max(center, left + 50);
            center = Math.Min(center, right - 50);
            // This asymmetric left-edge correction exists only in the Crimson source branch.
            if (crimson)
            {
                if (dungeonSide <= -1 && left < 400) left = 400;
                else if (dungeonSide >= 1 && left > width - 400) left = width - 400;
            }
            bool rejected = left < dungeonLocation + dungeonAvoidance && right > dungeonLocation - dungeonAvoidance;
            if (remix) { if (!rejected) return new(center, left, right); continue; }
            if (!anniversary)
                rejected |= Inside(center, width / 2 - centerAvoidance, width / 2 + centerAvoidance) ||
                            Inside(left, width / 2 - centerAvoidance, width / 2 + centerAvoidance) ||
                            Inside(right, width / 2 - centerAvoidance, width / 2 + centerAvoidance);
            // Source tests these three points, not generic interval overlap with the desert.
            rejected |= Inside(center, desertLeft, desertRight) || Inside(left, desertLeft, desertRight) ||
                        Inside(right, desertLeft, desertRight);
            if (left < snowRight && right > snowLeft)
            {
                snowLeft++; snowRight--; rejected = true;
            }
            if (!ignoreJungle && left < jungleRight && right > jungleLeft)
            {
                jungleLeft++; jungleRight--; rejected = true;
            }
            if (!rejected) return new(center, left, right);
        }
        throw new InvalidOperationException("Ordinary evil-biome placement exhausted its bounded search.");
    }

    private static bool Inside(int value, int left, int right) => value > left && value < right;
}
