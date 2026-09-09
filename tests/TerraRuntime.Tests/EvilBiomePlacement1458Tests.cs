using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class EvilBiomePlacement1458Tests
{
    [Fact]
    public void Scan_uses_actual_active_surface_tiles_and_source_padding()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(20, 10, Tile(60)); tiles.Set(40, 20, Tile(60));
        tiles.Set(50, 5, Tile(147)); tiles.Set(70, 30, Tile(161));
        tiles.Set(1, 40, Tile(60)); // Outside the scanned vertical interval.
        tiles.Set(90, 2, new WorldTile { Type = 147 }); // Inactive material is not a biome boundary.
        Assert.Equal(new EvilBiomePlacement1458.SurfaceBounds(10, 50, 40, 80),
            EvilBiomePlacement1458.Scan(tiles, 40, TestContext.Current.CancellationToken));
        Assert.Equal(new EvilBiomePlacement1458.SurfaceBounds(-9, 50, 40, 80),
            EvilBiomePlacement1458.Scan(tiles, 40.5, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Missing_biomes_retain_source_inverted_bounds()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        Assert.Equal(new EvilBiomePlacement1458.SurfaceBounds(90, 10, 90, 10),
            EvilBiomePlacement1458.Scan(tiles, 40, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Selection_draws_asymmetric_edges_in_source_order(bool crimson)
    {
        var random = new Script((500, 3700, 1000), (0, 200, 20), (0, 200, 80));
        Assert.Equal(new EvilBiomePlacement1458.Region(1000, 880, 1180), Select(random, crimson: crimson));
        random.AssertConsumed();
    }

    [Theory]
    [InlineData(false, -1, 380)] [InlineData(true, -1, 400)] [InlineData(true, 1, 380)]
    public void Crimson_only_has_asymmetric_dungeon_side_edge_correction(bool crimson, int side, int left)
    {
        var random = new Script((500, 3700, 500), (0, 200, 199), (0, 200, 0));
        Assert.Equal(new EvilBiomePlacement1458.Region(500, left, 600), Select(random, crimson: crimson, dungeonSide: side));
        random.AssertConsumed();
    }

    [Fact]
    public void Center_exclusion_is_strict_at_the_boundary()
    {
        var random = new Script((500, 3700, 1800), (0, 200, 0), (0, 200, 0));
        Assert.Equal(new EvilBiomePlacement1458.Region(1800, 1700, 1900), Select(random));
        random.AssertConsumed();
    }

    [Fact]
    public void Desert_checks_three_points_not_arbitrary_interval_overlap()
    {
        var random = new Script((500, 3700, 1200), (0, 200, 100), (0, 200, 100));
        Assert.Equal(new EvilBiomePlacement1458.Region(1200, 1000, 1400),
            Select(random, desertLeft: 1100, desertRight: 1150));
        random.AssertConsumed();
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Overlap_shrinks_only_current_selection_exclusion(bool jungle)
    {
        var bounds = jungle ? new EvilBiomePlacement1458.SurfaceBounds(1099, 1300, 4190, 10)
            : new EvilBiomePlacement1458.SurfaceBounds(4190, 10, 1099, 1300);
        for (int selection = 0; selection < 2; selection++)
        {
            var random = new Script((500, 3700, 1000), (0, 200, 0), (0, 200, 0),
                (500, 3700, 1000), (0, 200, 0), (0, 200, 0));
            Assert.Equal(new EvilBiomePlacement1458.Region(1000, 900, 1100), Select(random, bounds: bounds));
            random.AssertConsumed();
        }
    }

    [Theory]
    [InlineData(1000, 0, 0)] // Dungeon overlap.
    [InlineData(3500, 900, 1200)] // Desert overlap.
    public void Hard_exclusions_do_not_get_relaxed_or_fall_back(int dungeon, int desertLeft, int desertRight)
    {
        var random = new RepeatingSelection();
        Assert.Throws<InvalidOperationException>(() => Select(random, dungeonLocation: dungeon,
            desertLeft: desertLeft, desertRight: desertRight));
        Assert.Equal(EvilBiomePlacement1458.MaximumAttempts * 3, random.Calls);
    }

    [Fact]
    public void Cancelled_selection_consumes_no_random_draws()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var random = new Script();
        Assert.Throws<OperationCanceledException>(() => EvilBiomePlacement1458.Select(4200,
            default, 3500, 1, 0, 0, false, random, cancellation.Token));
        random.AssertConsumed();
    }

    private static WorldTile Tile(ushort type) => new() { Type = type, Flags = WorldTileFlags.Active };
    private static EvilBiomePlacement1458.Region Select(IWorldGenerationVanillaRandom random,
        bool crimson = false, int dungeonSide = 1, int dungeonLocation = 3500, int desertLeft = 0, int desertRight = 0,
        EvilBiomePlacement1458.SurfaceBounds? bounds = null) =>
        EvilBiomePlacement1458.Select(4200, bounds ?? new(4190, 10, 4190, 10), dungeonLocation, dungeonSide,
            desertLeft, desertRight, crimson, random, TestContext.Current.CancellationToken);

    private sealed class Script(params (int Min, int Max, int Value)[] draws) : IWorldGenerationVanillaRandom
    {
        private int index;
        public void AssertConsumed() => Assert.Equal(draws.Length, index);
        public int Next(int min, int max)
        {
            Assert.True(index < draws.Length, "Unexpected placement draw");
            var draw = draws[index++]; Assert.Equal((draw.Min, draw.Max), (min, max)); return draw.Value;
        }
        public int Next(int max) => Next(0, max);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] bytes) => throw new NotSupportedException();
    }

    private sealed class RepeatingSelection : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int min, int max) { Calls++; return min == 500 ? 1000 : 0; }
        public int Next(int max) => Next(0, max);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] bytes) => throw new NotSupportedException();
    }
}
