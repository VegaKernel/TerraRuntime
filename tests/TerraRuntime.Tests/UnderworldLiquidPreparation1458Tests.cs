using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class UnderworldLiquidPreparation1458Tests
{
    [Theory]
    [InlineData(0,false,false)] [InlineData(1,false,false)] [InlineData(57,false,false)]
    [InlineData(0,true,true)] [InlineData(19,false,true)] [InlineData(379,false,true)]
    [InlineData(10,false,true)] [InlineData(190,false,true)] [InlineData(191,false,true)] [InlineData(192,false,true)]
    [InlineData(138,false,true)] [InlineData(546,false,true)]
    public void Generation_water_check_clears_only_embedded_solid_liquid(ushort type, bool actuated, bool retained)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 320));
        var original = new WorldTile { Type = type, Flags = WorldTileFlags.Active | (actuated ? WorldTileFlags.Inactive : 0),
            Wall = 13, FrameX = 18, Shape = 3, LiquidAmount = 127, LiquidKind = WorldLiquidKind.Lava };
        store.Set(15, 200, original); store.Set(0, 200, original); store.Set(15, 0, original);
        new VanillaWorldLiquidSimulator1458(store).ClearEmbeddedLiquidDuringGenerationSettle(TestContext.Current.CancellationToken);
        Assert.Equal(original, store.Get(0, 200)); Assert.Equal(original, store.Get(15, 0));
        if (!retained) original.LiquidAmount = 0;
        Assert.Equal(original, store.Get(15, 200));
    }

    [Theory]
    [InlineData(WorldLiquidKind.Water, WorldLiquidKind.Lava)]
    [InlineData(WorldLiquidKind.Lava, WorldLiquidKind.Lava)]
    [InlineData(WorldLiquidKind.Honey, WorldLiquidKind.Honey)]
    [InlineData(WorldLiquidKind.Shimmer, WorldLiquidKind.Shimmer)]
    public void Generation_vertical_fall_converts_only_water_below_the_water_line(WorldLiquidKind initial, WorldLiquidKind expected)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 320));
        store.Set(15, 100, new WorldTile { LiquidAmount = 127, LiquidKind = initial });
        new VanillaWorldLiquidSimulator1458(store).QuickWaterBeforeDungeonGeneration(180, TestContext.Current.CancellationToken);
        WorldTile destination = Assert.Single(store.Tiles.ToArray(), tile => tile.LiquidAmount > 0);
        Assert.Equal(127, destination.LiquidAmount);
        Assert.Equal(expected, destination.LiquidKind);
        Assert.Equal(initial, store.Get(15, 100).LiquidKind); // Clearing amount does not clear the source kind bits.
    }

    [Theory]
    [InlineData(100,180,180)] // Equal to the line, not below.
    [InlineData(180,180,50)] // No vertical fall in this iteration.
    public void Depth_alone_does_not_turn_stationary_water_into_lava(int originY, int floorY, int waterLine)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 320));
        for (int x = 0; x < 30; x++) store.Set(x, floorY + 1, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        store.Set(15, originY, new WorldTile { LiquidAmount = 127 });
        new VanillaWorldLiquidSimulator1458(store).QuickWaterBeforeDungeonGeneration(waterLine, TestContext.Current.CancellationToken);
        Assert.Equal(WorldLiquidKind.Water, Assert.Single(store.Tiles.ToArray(), tile => tile.LiquidAmount > 0).LiquidKind);
    }

    [Fact]
    public void Loading_keeps_deep_water_and_generation_rejects_an_invalid_line()
    {
        var store = new WorldTileStore(new WorldDimensions(30, 320));
        store.Set(15, 100, new WorldTile { LiquidAmount = 127 });
        var simulator = new VanillaWorldLiquidSimulator1458(store);
        Assert.Throws<ArgumentOutOfRangeException>(() => simulator.QuickWaterBeforeDungeonGeneration(320, TestContext.Current.CancellationToken));
        Assert.Equal(127, store.Get(15, 100).LiquidAmount);
        simulator.QuickWater();
        Assert.Equal(WorldLiquidKind.Water, Assert.Single(store.Tiles.ToArray(), tile => tile.LiquidAmount > 0).LiquidKind);
    }

    [Theory]
    [InlineData(56,WorldLiquidKind.Lava)] [InlineData(659,WorldLiquidKind.Shimmer)]
    public void Isolated_merge_blocks_become_liquid_and_clear_all_metadata(ushort type, WorldLiquidKind kind)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 320));
        store.Set(15, 200, new WorldTile { Type = type, Wall = 13, Shape = 3, FrameX = 18,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed, TileColor = 7, LiquidAmount = 33 });
        UnderworldLiquidPreparation1458.CleanupInteractions(store, TestContext.Current.CancellationToken);
        Assert.Equal(new WorldTile { LiquidAmount = 255, LiquidKind = kind }, store.Get(15, 200));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Cleanup_preserves_the_vanilla_above_shimmer_asymmetry(bool secondKind)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 320));
        store.Set(15, 200, new WorldTile { Type = 56, Wall = 13, Flags = WorldTileFlags.Active, LiquidAmount = 17, LiquidKind = WorldLiquidKind.Honey });
        store.Set(15, 199, new WorldTile { LiquidAmount = 17, LiquidKind = WorldLiquidKind.Shimmer });
        if (secondKind) store.Set(15, 201, new WorldTile { LiquidAmount = 17, LiquidKind = WorldLiquidKind.Lava });
        UnderworldLiquidPreparation1458.CleanupInteractions(store, TestContext.Current.CancellationToken);
        WorldTile actual = store.Get(15, 200);
        Assert.Equal(secondKind, actual.IsActive);
        Assert.Equal(secondKind ? 0 : 255, actual.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, actual.LiquidKind);
        Assert.Equal(secondKind ? 13 : 0, actual.Wall);
    }

    [Fact]
    public void Pre_aether_clear_uses_reset_origin_radius_and_preserves_shimmer()
    {
        var store = new WorldTileStore(new WorldDimensions(200, 320));
        var waterIce = new WorldTile { Type = 162, Flags = WorldTileFlags.Active, LiquidAmount = 127 };
        store.Set(149, 0, waterIce); store.Set(150, 0, waterIce); store.Set(0, 76, waterIce);
        store.Set(0, 75, new WorldTile { LiquidKind = WorldLiquidKind.Shimmer, LiquidAmount = 127 });
        UnderworldLiquidPreparation1458.ClearBeforeAether(store);
        Assert.False(store.Get(149, 0).IsActive); Assert.Equal(0, store.Get(149, 0).LiquidAmount);
        Assert.Equal(waterIce, store.Get(150, 0)); Assert.Equal(waterIce, store.Get(0, 76));
        Assert.Equal(127, store.Get(0, 75).LiquidAmount);
    }
}
