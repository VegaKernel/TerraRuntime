using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HellFortGeneration1458Tests
{
    [Theory]
    [InlineData(75, 14)]
    [InlineData(76, 13)]
    public void No_wings_preserves_source_draws_and_builds_connected_ten_storey_tower(ushort brick, ushort wall)
    {
        var store = new WorldTileStore(new WorldDimensions(600, 400));
        var prefix = new List<(int, int, int)> { (4, 10, 6), (4, 10, 6) };
        for (int i = 0; i < 4; i++) prefix.Add((8, 20, 12));
        for (int i = 0; i < 10; i++) prefix.Add((6, 12, 8));
        for (int i = 0; i < 4; i++) prefix.Add((0, 3, 1));
        prefix.AddRange([(0, 10, 4), (0, 10, 5), (0, 10, 0), (0, 10, 9)]);
        var random = new SourceRandom(42, prefix.ToArray());
        Assert.Equal(10, HellFortGenerator1458.Build(store, 300, 320, brick, wall, random, TestContext.Current.CancellationToken));
        Assert.True(random.PrefixConsumed);
        int doors = 0, platforms = 0;
        for (int y = 0; y < 400; y++)
        for (int x = 0; x < 600; x++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.Wall == wall) { Assert.InRange(x, 295, 305); Assert.InRange(y, 289, 367); }
            if (!tile.IsActive) continue;
            Assert.InRange(x, 294, 306); Assert.InRange(y, 288, 368);
            Assert.Equal(0, tile.LiquidAmount);
            Assert.Contains(tile.Type, new ushort[] { brick, 10, 19 });
            if (tile.Type == 10)
            {
                Assert.Contains(tile.FrameX, new short[] { 0, 18, 36 });
                Assert.InRange(tile.FrameY, 19 * 54, 19 * 54 + 36);
                if (tile.FrameY == 19 * 54) doors++;
            }
            if (tile.Type == 19) { Assert.Equal(13 * 18, tile.FrameY); platforms++; }
        }
        Assert.Equal(2, doors);
        Assert.InRange(platforms, 30, 60); // Nine floor links and one dry roof exit, each 3..6 cells.
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1458)]
    [InlineData(8675309)]
    public void Settlement_has_complete_lava_safe_doors_and_no_overlapping_fort_writes(int seed)
    {
        WorldTileStore store = Ground();
        Assert.True(HellFortGenerator1458.Generate(store, new SourceRandom(seed), TestContext.Current.CancellationToken) > 0);
        int doors = 0, platforms = 0, walls = 0;
        for (int y = 0; y < 400; y++)
        for (int x = 0; x < 600; x++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.Wall is 13 or 14) { walls++; Assert.InRange(y, 200, 380); }
            if (!tile.IsActive) continue;
            if (tile.Type == 19) { platforms++; Assert.Equal(234, tile.FrameY); }
            if (tile.Type != 10 || tile.FrameY != 1026) continue;
            doors++;
            for (int dy = 0; dy < 3; dy++)
            {
                WorldTile cell = store.Get(x, y + dy);
                Assert.True(cell.IsActive); Assert.Equal(10, cell.Type); Assert.Equal(1026 + dy * 18, cell.FrameY);
            }
            Assert.True(HellFortGenerator1458.Solid(store.Get(x, y - 1)));
            Assert.True(HellFortGenerator1458.Solid(store.Get(x, y + 3)));
        }
        Assert.True(walls > 100); Assert.True(doors > 0); Assert.True(platforms > 0);
    }

    [Fact]
    public void Lava_above_ground_does_not_manufacture_a_fort_foundation()
    {
        WorldTileStore store = Ground();
        for (int x = 0; x < 600; x++) At(store, x, 349).LiquidAmount = 255;
        var random = new SourceRandom(42);
        Assert.Equal(0, HellFortGenerator1458.Generate(store, random, TestContext.Current.CancellationToken));
        Assert.Equal(0, random.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Hellforge_preserves_liquids_walls_paint_and_wire(int liquidKind)
    {
        WorldTileStore store = Ground();
        for (int x = 299; x <= 301; x++)
        for (int y = 348; y <= 349; y++)
            At(store, x, y) = new WorldTile { Wall = 14, TileColor = 6, LiquidAmount = 255,
                LiquidKind = (WorldLiquidKind)liquidKind, Flags = WorldTileFlags.WireRed };
        Assert.True(HellforgePlacement1458.TryPlace(store, 300, 349));
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile tile = store.Get(299 + dx, 348 + dy);
            Assert.True(tile.IsActive); Assert.Equal(77, tile.Type);
            Assert.Equal(dx * 18, tile.FrameX); Assert.Equal(dy * 18, tile.FrameY);
            Assert.Equal(14, tile.Wall); Assert.Equal(6, tile.TileColor);
            Assert.Equal(255, tile.LiquidAmount); Assert.Equal((WorldLiquidKind)liquidKind, tile.LiquidKind);
            Assert.True((tile.Flags & WorldTileFlags.WireRed) != 0);
        }
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("missing-support")]
    [InlineData("half")]
    [InlineData("slope")]
    [InlineData("actuated")]
    [InlineData("table")]
    [InlineData("unknown")]
    public void Invalid_forge_placement_is_atomic(string reason)
    {
        WorldTileStore store = Ground();
        switch (reason)
        {
            case "blocked": At(store, 301, 348).Flags |= WorldTileFlags.Active; break;
            case "missing-support": At(store, 299, 350) = default; break;
            case "half": At(store, 299, 350).Shape = 1; break;
            case "slope": At(store, 299, 350).Shape = 2; break;
            case "actuated": At(store, 299, 350).Flags |= WorldTileFlags.Inactive; break;
            case "table": At(store, 299, 350).Type = 14; break;
            case "unknown": At(store, 299, 350).Type = ushort.MaxValue; break;
        }
        WorldTile[] before = store.Tiles.ToArray();
        Assert.False(HellforgePlacement1458.TryPlace(store, 300, 349));
        Assert.Equal(before, store.Tiles.ToArray());
    }

    [Fact]
    public void SolidTile2_admits_full_platform_but_fort_SolidTile_does_not()
    {
        WorldTileStore store = Ground();
        for (int x = 299; x <= 301; x++) At(store, x, 350).Type = 19;
        Assert.False(HellFortGenerator1458.Solid(store.Get(300, 350)));
        Assert.True(HellforgePlacement1458.TryPlace(store, 300, 349));
    }

    [Fact]
    public void Forge_pass_uses_width_budget_wall_sample_then_downward_scan()
    {
        WorldTileStore store = Ground();
        // Width 600 => three forges. Rejected non-house sample must not place an ash-floor forge.
        var prefix = new List<(int, int, int)> { (1, 600, 50), (150, 370, 300) };
        foreach (int x in new[] { 200, 250, 300 })
        {
            At(store, x, 300).Wall = x == 250 ? (ushort)13 : (ushort)14;
            prefix.Add((1, 600, x)); prefix.Add((150, 370, 300));
        }
        var random = new SourceRandom(42, prefix.ToArray());
        Assert.Equal(3, HellforgePlacement1458.Generate(store, random, TestContext.Current.CancellationToken));
        Assert.Equal(8, random.Calls);
        Assert.False(store.Get(50, 349).IsActive);
        foreach (int x in new[] { 200, 250, 300 }) Assert.Equal(77, store.Get(x, 349).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaceTile_wrapper_clears_only_inactive_anchor_paint_even_when_object_is_rejected(bool blocked)
    {
        WorldTileStore store = Ground();
        for (int x = 299; x <= 301; x++)
        for (int y = 348; y <= 349; y++)
            At(store, x, y) = new WorldTile { TileColor = 6, Wall = 14, Shape = 2, LiquidAmount = 90,
                Flags = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireRed };
        if (blocked) At(store, 299, 348).Flags |= WorldTileFlags.Active;
        Assert.Equal(!blocked, HellforgePlacement1458.TryPlaceTile(store, 300, 349));
        WorldTile anchor = store.Get(300, 349);
        Assert.Equal(0, anchor.TileColor); Assert.Equal(0, anchor.Shape);
        Assert.False(anchor.IsBlockInvisible); Assert.False(anchor.IsBlockFullbright);
        Assert.Equal(14, anchor.Wall); Assert.Equal(90, anchor.LiquidAmount);
        Assert.True((anchor.Flags & WorldTileFlags.WireRed) != 0);
        Assert.Equal(6, store.Get(299, 349).TileColor);
        Assert.True(store.Get(299, 349).IsBlockInvisible);
    }

    private static WorldTileStore Ground()
    {
        var store = new WorldTileStore(new WorldDimensions(600, 400));
        for (int x = 0; x < 600; x++)
        for (int y = 350; y < 400; y++) At(store, x, y) = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        return store;
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private sealed class SourceRandom(int seed, params (int Min, int Max, int Value)[] prefix) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Calls { get; private set; }
        public bool PrefixConsumed => Calls >= prefix.Length;
        public int Next(int minValue, int maxValue)
        {
            if (Calls++ >= prefix.Length) return random.Next(minValue, maxValue);
            var draw = prefix[Calls - 1];
            Assert.Equal((draw.Min, draw.Max), (minValue, maxValue));
            return draw.Value;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => Next(0, int.MaxValue);
        public double NextDouble() { Calls++; return random.NextDouble(); }
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
