using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HellFortDecoration1458Tests
{
    [Theory]
    [InlineData(4200, 100)]
    [InlineData(6400, 66)]
    [InlineData(8400, 50)]
    public void Each_phase_uses_source_inverse_width_budget(int width, int expected) => Assert.Equal(expected, HellFortDecoration1458.AttemptCount(width));

    [Theory]
    [InlineData(0, 0, 240, 27)]
    [InlineData(0, 1, 240, 29)]
    [InlineData(0, 2, 240, 30)]
    [InlineData(0, 3, 240, 31)]
    [InlineData(0, 4, 240, 32)]
    [InlineData(2, 0, 245, 1)]
    [InlineData(2, 1, 245, 2)]
    [InlineData(2, 2, 245, 4)]
    [InlineData(3, 0, 246, 0)]
    [InlineData(3, 1, 246, 16)]
    [InlineData(3, 2, 246, 17)]
    public void Painting_palette_matches_independently_pinned_source(int family, int draw, ushort type, int style)
    {
        var random = new ScriptedRandom((0, 4, family), (0, family == 0 ? 5 : 3, draw));
        Assert.Equal((type, style), HellFortDecoration1458.SelectPainting(random)); random.AssertConsumed();
    }

    [Theory]
    [InlineData(0, 240, 27)]
    [InlineData(1, 242, 14)]
    [InlineData(2, 245, 1)]
    [InlineData(3, 246, 0)]
    public void Family_one_redraws_exactly_once(int secondFamily, ushort type, int style)
    {
        var draws = new List<(int, int, int)> { (0, 4, 1), (0, 4, secondFamily) };
        if (secondFamily != 1) draws.Add((0, secondFamily == 0 ? 5 : 3, 0));
        var random = new ScriptedRandom(draws.ToArray());
        Assert.Equal((type, style), HellFortDecoration1458.SelectPainting(random)); random.AssertConsumed();
    }

    [Theory]
    [InlineData(240, 27, -1, -1, 3, 3, 1458, 0)]
    [InlineData(240, 32, -1, -1, 3, 3, 1728, 0)]
    [InlineData(242, 14, -2, -2, 6, 4, 0, 1008)]
    [InlineData(245, 4, 0, -1, 2, 3, 144, 0)]
    [InlineData(246, 17, -1, 0, 3, 2, 0, 612)]
    [InlineData(91, 16, 0, 0, 1, 3, 288, 0)]
    [InlineData(91, 21, 0, 0, 1, 3, 378, 0)]
    [InlineData(34, 32, -1, 0, 3, 3, 0, 1728)]
    [InlineData(42, 32, 0, 0, 1, 2, 0, 1152)]
    public void Full_object_frames_preserve_liquid_walls_and_non_anchor_paint(ushort type, int style, int ox, int oy, int width, int height, int fx, int fy)
    {
        WorldTileStore store = Room();
        if (type is 91 or 34 or 42) At(store, 300, 289) = Brick();
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref At(store, 300 + ox + dx, 290 + oy + dy);
            tile.TileColor = 9; tile.LiquidAmount = 100;
        }
        Assert.True(HellFortDecoration1458.Place(store, 300, 290, type, style));
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            WorldTile tile = store.Get(300 + ox + dx, 290 + oy + dy);
            Assert.True(tile.IsActive); Assert.Equal(type, tile.Type);
            Assert.Equal(fx + dx * 18, tile.FrameX); Assert.Equal(fy + dy * 18, tile.FrameY);
            Assert.Equal(14, tile.Wall); Assert.Equal(100, tile.LiquidAmount);
            Assert.Equal(ox + dx == 0 && oy + dy == 0 ? 0 : 9, tile.TileColor);
        }
    }

    [Theory]
    [InlineData(240, 27, 301, 291)]
    [InlineData(242, 14, 303, 291)]
    [InlineData(245, 4, 301, 291)]
    [InlineData(246, 17, 301, 291)]
    public void Wall_hole_rejects_whole_painting_but_cleans_inactive_anchor(ushort type, int style, int holeX, int holeY)
    {
        WorldTileStore store = Room(); At(store, holeX, holeY).Wall = 0; At(store, 300, 290).TileColor = 9;
        Assert.False(HellFortDecoration1458.Place(store, 300, 290, type, style));
        Assert.False(store.Get(300, 290).IsActive); Assert.Equal(0, store.Get(300, 290).TileColor);
        Assert.DoesNotContain(store.Tiles.ToArray(), tile => tile.IsActive && tile.Type == type);
    }

    [Theory]
    [InlineData(240, true)]
    [InlineData(241, true)]
    [InlineData(242, true)]
    [InlineData(245, false)]
    [InlineData(246, false)]
    public void Picture_only_exclusion_uses_exact_source_set_and_inclusive_corner(ushort type, bool excluded)
    {
        WorldTileStore store = Room(); At(store, 308, 295) = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        Assert.Equal(excluded, HellFortDecoration1458.NearPicture(store, 300, 290, true));
        At(store, 308, 295).Flags = 0; At(store, 309, 295) = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        Assert.False(HellFortDecoration1458.NearPicture(store, 300, 290, true));
    }

    [Theory]
    [InlineData(-4, -3, true)]
    [InlineData(3, 2, true)]
    [InlineData(4, 2, false)]
    [InlineData(3, 3, false)]
    public void Tight_exclusion_is_asymmetric_and_checks_any_active_type(int dx, int dy, bool expected)
    {
        WorldTileStore store = Room(); At(store, 300 + dx, 290 + dy) = Brick();
        Assert.Equal(expected, HellFortDecoration1458.NearPicture(store, 300, 290, false));
    }

    [Fact]
    public void Painting_recenters_room_twice_before_placement()
    {
        WorldTileStore store = Room(); var random = new ScriptedRandom((0, 4, 0), (0, 5, 1));
        Assert.True(HellFortDecoration1458.TryPlacePaintingCandidate(store, 285, 284, random)); random.AssertConsumed();
        Assert.Equal(240, store.Get(300, 290).Type); Assert.Equal(1584, store.Get(300, 290).FrameX);
    }

    [Theory]
    [InlineData(8, 6, true)]
    [InlineData(7, 6, false)]
    [InlineData(8, 5, false)]
    public void Painting_minimum_span_is_not_cell_count(int spanX, int spanY, bool admitted)
    {
        WorldTileStore store = Room(296, 296 + spanX, 287, 287 + spanY);
        var random = admitted ? new ScriptedRandom((0, 4, 2), (0, 3, 0)) : new ScriptedRandom();
        Assert.Equal(admitted, HellFortDecoration1458.TryPlacePaintingCandidate(store, 300, 290, random)); random.AssertConsumed();
    }

    [Fact]
    public void Tight_rejection_still_consumes_painting_choice()
    {
        WorldTileStore store = Room(); At(store, 303, 292) = Brick();
        var random = new ScriptedRandom((0, 4, 1), (0, 4, 1));
        Assert.False(HellFortDecoration1458.TryPlacePaintingCandidate(store, 300, 290, random)); random.AssertConsumed();
    }

    [Fact]
    public void Banner_palette_draws_all_initial_styles_before_duplicate_retries()
    {
        var random = new ScriptedRandom((16, 22, 16), (16, 22, 16), (16, 22, 16), (16, 22, 17), (16, 22, 17), (16, 22, 21));
        Span<int> palette = stackalloc int[3];
        HellFortDecoration1458.SelectBannerPalette(random, palette, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 16, 17, 21 }, palette.ToArray()); random.AssertConsumed();
    }

    [Theory]
    [InlineData(0, 91)]
    [InlineData(1, 34)]
    [InlineData(2, 42)]
    public void Ceiling_scan_ignores_platform_and_adjacent_decoration(int choice, ushort type)
    {
        WorldTileStore store = Room(); At(store, 300, 285) = new WorldTile { Type = 19, Flags = WorldTileFlags.Active };
        At(store, 302, 281) = new WorldTile { Type = 91, Flags = WorldTileFlags.Active };
        var random = choice == 0 ? new ScriptedRandom((0, 3, choice), (0, 3, 2)) : new ScriptedRandom((0, 3, choice));
        Assert.True(HellFortDecoration1458.TryPlaceCeilingCandidate(store, 300, 290, [16, 17, 21], random)); random.AssertConsumed();
        Assert.Equal(type, store.Get(300, 281).Type);
        if (choice == 0) Assert.Equal(378, store.Get(300, 281).FrameX);
        Assert.Equal(91, store.Get(302, 281).Type);
    }

    [Theory]
    [InlineData(91, 16)]
    [InlineData(34, 32)]
    [InlineData(42, 32)]
    public void Hanging_object_rejects_missing_or_platform_support_and_occupied_last_cell(ushort type, int style)
    {
        WorldTileStore store = Room();
        Assert.False(HellFortDecoration1458.Place(store, 300, 290, type, style));
        At(store, 300, 289) = new WorldTile { Type = 19, Flags = WorldTileFlags.Active };
        Assert.False(HellFortDecoration1458.Place(store, 300, 290, type, style));
        At(store, 300, 289) = Brick(); At(store, 300, type == 42 ? 291 : 292) = Brick();
        Assert.False(HellFortDecoration1458.Place(store, 300, 290, type, style)); Assert.False(store.Get(300, 290).IsActive);
    }

    [Theory]
    [InlineData(240, 28)]
    [InlineData(241, 0)]
    [InlineData(91, 22)]
    [InlineData(34, 31)]
    public void Unverified_types_and_styles_fail_closed_without_mutation(ushort type, int style)
    {
        WorldTileStore store = Room(); At(store, 300, 290).TileColor = 9;
        Assert.False(HellFortDecoration1458.Place(store, 300, 290, type, style)); Assert.Equal(9, store.Get(300, 290).TileColor);
    }

    private static WorldTileStore Room(int left = 281, int right = 319, int top = 281, int bottom = 299)
    {
        WorldTileStore store = new Workspace(600, 400).TileStore;
        for (int x = left - 1; x <= right + 1; x++)
        for (int y = top - 1; y <= bottom + 1; y++)
            At(store, x, y) = x == left - 1 || x == right + 1 || y == top - 1 || y == bottom + 1 ? Brick() : new WorldTile { Wall = 14 };
        return store;
    }
    private static WorldTile Brick() => new() { Type = 75, Wall = 14, Flags = WorldTileFlags.Active };
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    private sealed class ScriptedRandom(params (int Min, int Max, int Value)[] script) : IWorldGenerationVanillaRandom
    {
        private int calls;
        public void AssertConsumed() => Assert.Equal(script.Length, calls);
        public int Next(int minValue, int maxValue)
        {
            Assert.True(calls < script.Length, "Unexpected decoration RNG draw");
            var draw = script[calls++]; Assert.Equal((draw.Min, draw.Max), (minValue, maxValue)); return draw.Value;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
