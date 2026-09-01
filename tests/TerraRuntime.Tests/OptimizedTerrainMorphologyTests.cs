using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedTerrainMorphologyTests
{
    [Fact]
    public void Canonical_world_sizes_have_bounded_distinct_macro_relief()
    {
        var fixtures = new (int Width, int Height, ulong Seed)[]
        {
            (4200, 1200, 0x0F7145EDUL),
            (6400, 1800, 0x1234ABCDUL),
            (8400, 2400, 0x987654321UL)
        };

        foreach ((int width, int height, ulong seed) in fixtures)
        {
            int baseSurface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
            int[] source = Enumerable.Repeat(baseSurface, width).ToArray();

            int[] first = OptimizedTerrainMorphology.BuildTargetSurfaceProfile(seed, width, height, source);
            int[] replay = OptimizedTerrainMorphology.BuildTargetSurfaceProfile(seed, width, height, source);
            OptimizedTerrainMorphology.ProfileMetrics metrics =
                OptimizedTerrainMorphology.AnalyzeProfile(source, first);

            Assert.Equal(first, replay);
            Assert.True(metrics.MinimumDelta <= -8, $"{width}x{height} lacks a meaningful uplift: {metrics.MinimumDelta}.");
            Assert.True(metrics.MaximumDelta >= 8, $"{width}x{height} lacks a meaningful basin: {metrics.MaximumDelta}.");
            Assert.InRange(metrics.MaximumAdjacentStep, 0, 2);
            Assert.True(metrics.DirectionChanges >= 20, $"{width}x{height} has too few macro-direction changes: {metrics.DirectionChanges}.");
            Assert.True(metrics.FlatRunColumns >= width / 2, $"{width}x{height} has no broad plateau/rolling runs.");
        }
    }

    [Fact]
    public void Different_seeds_change_the_morphology_fingerprint()
    {
        const int width = 4200;
        const int height = 1200;
        int baseSurface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
        int[] source = Enumerable.Repeat(baseSurface, width).ToArray();

        int[] first = OptimizedTerrainMorphology.BuildTargetSurfaceProfile(1UL, width, height, source);
        int[] second = OptimizedTerrainMorphology.BuildTargetSurfaceProfile(2UL, width, height, source);

        ulong firstFingerprint = OptimizedTerrainMorphology.AnalyzeProfile(source, first).Fingerprint;
        ulong secondFingerprint = OptimizedTerrainMorphology.AnalyzeProfile(source, second).Fingerprint;
        Assert.NotEqual(firstFingerprint, secondFingerprint);
    }

    [Fact]
    public void Spawn_oceans_and_evil_altar_anchor_remain_fixed()
    {
        const int width = 640;
        const int height = 320;
        const ulong seed = 0x5EEDC0DEUL;
        int baseSurface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
        int[] source = Enumerable.Repeat(baseSurface, width).ToArray();

        int[] target = OptimizedTerrainMorphology.BuildTargetSurfaceProfile(seed, width, height, source);
        int oceanWidth = Math.Clamp(width / 12, 48, 360);
        int spawnHalfWidth = Math.Clamp(width / 28, 18, 110);
        int spawn = width / 2;
        int evilLeft = Math.Clamp((int)Math.Round(width * 0.61d), 1, width - 2);
        int evilRight = Math.Clamp((int)Math.Round(width * 0.73d), evilLeft, width - 2);
        int evilCenter = evilLeft + (evilRight - evilLeft + 1) / 2;

        Assert.Equal(source[0], target[0]);
        Assert.Equal(source[oceanWidth], target[oceanWidth]);
        Assert.Equal(source[width - oceanWidth - 1], target[width - oceanWidth - 1]);
        for (int x = spawn - spawnHalfWidth; x <= spawn + spawnHalfWidth; x++)
            Assert.Equal(source[x], target[x]);
        Assert.Equal(source[evilCenter], target[evilCenter]);
    }

    [Fact]
    public void Algorithm_version_is_explicit_for_future_layout_compatibility()
    {
        Assert.Equal(2, OptimizedTerrainMorphology.AlgorithmVersion);
    }
}
