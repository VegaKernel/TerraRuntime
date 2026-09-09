using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class UnderworldVegetation1458Tests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 66)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 88)]
    public void Ash_profile_uses_independent_roots_and_source_base_frames(bool left, bool right, int baseX)
    {
        WorldTileStore store = TreeSite();
        var random = new ScriptedRandom(TreeScript(7, left, right));
        Assert.True(AshTreeGrower1458.TryGrow(store, 50, 70, random));
        random.AssertConsumed();
        for (int y = 63; y < 70; y++)
        {
            WorldTile tile = store.Get(50, y);
            Assert.True(tile.IsActive); Assert.Equal(634, tile.Type);
            Assert.Equal(9, tile.TileColor); Assert.True(tile.IsBlockInvisible); Assert.True(tile.IsBlockFullbright);
        }
        Assert.Equal(left, store.Get(49, 69).IsActive);
        Assert.Equal(right, store.Get(51, 69).IsActive);
        Assert.Equal(baseX, store.Get(50, 69).FrameX);
        Assert.Equal(left || right ? 132 : 0, store.Get(50, 69).FrameY);
        Assert.Equal(22, store.Get(50, 63).FrameX); Assert.Equal(198, store.Get(50, 63).FrameY);
        if (left) Assert.Equal(44, store.Get(49, 69).FrameX);
        if (right) Assert.Equal(22, store.Get(51, 69).FrameX);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(12)]
    public void Ash_height_is_seven_through_twelve_and_requires_full_five_wide_clearance(int height)
    {
        WorldTileStore store = TreeSite();
        var random = new ScriptedRandom(TreeScript(height, true, true));
        Assert.True(AshTreeGrower1458.TryGrow(store, 50, 70, random));
        random.AssertConsumed();
        Assert.Equal(634, store.Get(50, 70 - height).Type);
        Assert.False(store.Get(50, 69 - height).IsActive);
    }

    [Fact]
    public void Outer_bottom_clearance_obstruction_rejects_after_height_draw_without_writes()
    {
        WorldTileStore store = TreeSite();
        At(store, 52, 69) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        WorldTile[] before = store.Tiles.ToArray();
        var random = new ScriptedRandom((7, 13, 7));
        Assert.False(AshTreeGrower1458.TryGrow(store, 50, 70, random));
        random.AssertConsumed(); Assert.Equal(before, store.Tiles.ToArray());
    }

    [Theory]
    [InlineData("ash-not-grass")]
    [InlineData("half")]
    [InlineData("slope")]
    [InlineData("actuated")]
    [InlineData("wet-left")]
    [InlineData("wet-center")]
    [InlineData("wet-right")]
    [InlineData("wall")]
    [InlineData("no-ash-neighbour")]
    public void Invalid_profile_ground_rejects_before_rng(string reason)
    {
        WorldTileStore store = TreeSite();
        switch (reason)
        {
            case "ash-not-grass": At(store, 50, 70).Type = 57; break;
            case "half": At(store, 50, 70).Shape = 1; break;
            case "slope": At(store, 50, 70).Shape = 2; break;
            case "actuated": At(store, 50, 70).Flags |= WorldTileFlags.Inactive; break;
            case "wet-left": At(store, 49, 69).LiquidAmount = 1; break;
            case "wet-center": At(store, 50, 69).LiquidAmount = 1; break;
            case "wet-right": At(store, 51, 69).LiquidAmount = 1; break;
            case "wall": At(store, 50, 69).Wall = 14; break;
            case "no-ash-neighbour": At(store, 49, 70).Type = 2; At(store, 51, 70).Type = 2; break;
        }
        WorldTile[] before = store.Tiles.ToArray();
        Assert.False(AshTreeGrower1458.TryGrow(store, 50, 70, new ScriptedRandom()));
        Assert.Equal(before, store.Tiles.ToArray());
    }

    [Fact]
    public void Generic_root_ground_consumes_growth_draws_then_strict_ash_framing_removes_root()
    {
        WorldTileStore store = TreeSite();
        At(store, 49, 70).Type = 2;
        var script = TreeScript(7, true, true).ToList();
        for (int dust = 0; dust < 10; dust++) script.AddRange([(0,10,0),(0,12,0)]);
        var random = new ScriptedRandom(script.ToArray());
        Assert.True(AshTreeGrower1458.TryGrow(store, 50, 70, random));
        random.AssertConsumed();
        Assert.False(store.Get(49,69).IsActive);
        Assert.Equal(0, store.Get(49,69).Type);
        Assert.Equal((154,65), ((int)store.Get(49,69).FrameX, (int)store.Get(49,69).FrameY));
        Assert.Equal(0, store.Get(50,69).FrameX);
    }

    [Fact]
    public void Missing_root_ground_still_consumes_its_independent_suppression_draw()
    {
        WorldTileStore store = TreeSite();
        At(store, 49, 70) = default;
        var script = TreeScript(7, false, true);
        script[15] = (0, 3, 1); // Nonzero roll cannot restore an ineligible left root.
        var random = new ScriptedRandom(script);
        Assert.True(AshTreeGrower1458.TryGrow(store, 50, 70, random));
        random.AssertConsumed(); Assert.False(store.Get(49, 69).IsActive);
    }

    [Fact]
    public void Consecutive_branch_reroll_and_branch_frames_follow_settings_source_order()
    {
        WorldTileStore store = TreeSite();
        var script = new List<(int, int, int)> { (7, 13, 7), (0, 3, 0), (0, 10, 5) }; // Top forced straight.
        script.AddRange([(0, 3, 1), (0, 10, 5), (0, 3, 2), (0, 3, 0)]); // Leafy left branch.
        script.AddRange([(0, 3, 0), (0, 10, 5), (0, 10, 6), (0, 3, 1), (0, 3, 2)]); // Reroll to bare right.
        for (int i = 0; i < 4; i++) script.AddRange([(0, 3, 0), (0, 10, 0)]);
        script.AddRange([(0, 3, 0), (0, 3, 0), (0, 3, 0), (0, 13, 0), (0, 3, 2)]);
        var random = new ScriptedRandom(script.ToArray());
        Assert.True(AshTreeGrower1458.TryGrow(store, 50, 70, random));
        random.AssertConsumed();
        Assert.Equal((44, 242), ((int)store.Get(49, 64).FrameX, (int)store.Get(49, 64).FrameY));
        Assert.Equal((88, 88), ((int)store.Get(51, 65).FrameX, (int)store.Get(51, 65).FrameY));
        Assert.Equal((0, 242), ((int)store.Get(50, 63).FrameX, (int)store.Get(50, 63).FrameY));
    }

    [Fact]
    public void Grass_scan_has_strict_edge_bands_eight_neighbours_and_per_condition_rng()
    {
        var store = new WorldTileStore(new WorldDimensions(1000, 400));
        foreach (int x in new[] { 24, 25, 169, 170, 830, 831, 974, 975 })
            At(store, x, 150) = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        for (int x = 99; x <= 101; x++)
        for (int y = 199; y <= 201; y++) At(store, x, y) = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        At(store, 110, 299) = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        At(store, 110, 300) = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        var random = new GrassRandom();
        UnderworldVegetation1458.GrowGrass(store, random, TestContext.Current.CancellationToken);
        Assert.Equal(289 * 201, random.Calls);
        foreach (int x in new[] { 25, 169, 831, 974 }) Assert.Equal(633, store.Get(x, 150).Type);
        foreach (int x in new[] { 24, 170, 830, 975 }) Assert.Equal(57, store.Get(x, 150).Type);
        Assert.Equal(57, store.Get(100, 200).Type); // Enclosed, even though neighbors change type in-place.
        Assert.Equal(633, store.Get(99, 199).Type);
        Assert.Equal(633, store.Get(110, 299).Type); Assert.Equal(57, store.Get(110, 300).Type);
    }

    private static (int, int, int)[] TreeScript(int height, bool left, bool right)
    {
        var script = new List<(int, int, int)> { (7, 13, height) };
        for (int i = 0; i < height; i++) script.AddRange([(0, 3, 0), (0, 10, 0)]);
        script.AddRange([(0, 3, left ? 1 : 0), (0, 3, right ? 1 : 0)]);
        if (right) script.Add((0, 3, 0));
        if (left) script.Add((0, 3, 0));
        script.AddRange([(0, 3, 0), (0, 13, 1), (0, 3, 0)]);
        return script.ToArray();
    }
    private static WorldTileStore TreeSite()
    {
        var store = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 49; x <= 51; x++) At(store, x, 70) = new WorldTile { Type = 633, TileColor = 9,
            Flags = WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock };
        return store;
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    private sealed class ScriptedRandom(params (int Min, int Max, int Value)[] script) : IWorldGenerationVanillaRandom
    {
        private int calls;
        public void AssertConsumed() => Assert.Equal(script.Length, calls);
        public int Next(int minValue, int maxValue)
        {
            Assert.True(calls < script.Length, "Unexpected ash-tree RNG draw");
            var draw = script[calls++]; Assert.Equal((draw.Min, draw.Max), (minValue, maxValue)); return draw.Value;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
    private sealed class GrassRandom : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int minValue, int maxValue) { Assert.Equal((-1, 2), (minValue, maxValue)); Calls++; return 0; }
        public int Next(int maxValue) => throw new NotSupportedException();
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
