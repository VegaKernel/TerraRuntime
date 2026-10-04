using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class LoadingLiquidPotRemnants1458Tests
{
    [Theory]
    [InlineData(false, "coherent")]
    [InlineData(false, "foreign")]
    [InlineData(false, "inactive")]
    [InlineData(false, "mixedFrameX")]
    [InlineData(false, "single")]
    [InlineData(true, "coherent")]
    [InlineData(true, "foreign")]
    [InlineData(true, "inactive")]
    [InlineData(true, "mixedFrameX")]
    [InlineData(true, "single")]
    public void WaterCheck_matches_original_pot_remnant_death_set(bool generation, string mode)
    {
        // Captured by actual TerrariaServer 1.4.5.8 Main tile-data initialization + WorldGen.WaterCheck.
        // Only live type28 cells disappear; inactive type28 and the foreign stone remain unchanged.
        WorldTileStore tiles = Create(mode);
        WorldTile[] before = tiles.Tiles.ToArray();
        var simulator = new VanillaWorldLiquidSimulator1458(tiles);

        Assert.True((generation ? simulator.WaterCheckDuringWorldGeneration() : simulator.WaterCheckLoading()).IsApplied);

        for (int x = 0; x < 80; x++)
        for (int y = 0; y < 80; y++)
        {
            WorldTile old = before[x * 80 + y];
            WorldTile actual = tiles.Get(x, y);
            if (x is >= 40 and <= 41 && y is >= 40 and <= 41 && old is { IsActive: true, Type: 28 })
            {
                Assert.False(actual.IsActive);
                Assert.Equal(0, actual.Type);
                Assert.Equal(old.LiquidAmount, actual.LiquidAmount);
                Assert.Equal(old.LiquidKind, actual.LiquidKind);
                Assert.Equal(old.Wall, actual.Wall);
            }
            else Assert.Equal(old, actual);
        }
        Assert.Equal(100, tiles.Get(41, 41).LiquidAmount);
    }

    [Theory]
    [InlineData(21)] // Chest metadata belongs to the foreign object, never the pot's cleanup.
    [InlineData(55)] // Sign.
    [InlineData(395)] // Item-frame tile entity.
    public void Pot_cleanup_preserves_foreign_metadata_tile_identity_and_frames(int foreignType)
    {
        WorldTileStore tiles = Create("foreign");
        var foreign = new WorldTile { Type = (ushort)foreignType, FrameX = 36, FrameY = 18,
            Wall = 13, WallColor = 7, TileColor = 3, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        tiles.SetInitialPopulationTile(40, 40, in foreign);

        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);

        Assert.Equal(foreign, tiles.Get(40, 40));
        Assert.False(tiles.Get(41, 41).IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Conflicting_live_pot_anchor_or_locked_temple_door_stays_atomic(bool conflictingAnchor)
    {
        WorldTileStore tiles = Create("coherent");
        if (conflictingAnchor)
        {
            WorldTile conflicting = tiles.Get(40, 40);
            conflicting.FrameY = 36;
            tiles.SetInitialPopulationTile(40, 40, in conflicting);
        }
        else
        {
            var locked = new WorldTile { Type = 10, FrameY = 594, Flags = WorldTileFlags.Active };
            tiles.SetInitialPopulationTile(41, 42, in locked);
        }
        WorldTile[] before = tiles.Tiles.ToArray();

        Assert.False(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckDuringWorldGeneration().IsApplied);

        Assert.Equal(before, tiles.Tiles.ToArray());
    }

    private static WorldTileStore Create(string mode)
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 80));
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            var cell = new WorldTile { Type = 28, FrameX = (short)(dx * 18 + (mode == "mixedFrameX" && dy == 0 ? 36 : 0)),
                FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active, Wall = 13 };
            if ((mode == "inactive" && dx == 0 && dy == 0) || (mode == "single" && (dx != 1 || dy != 1)))
                cell.Flags = WorldTileFlags.None;
            if (mode == "foreign" && dx == 0 && dy == 0) cell.Type = 1;
            if (dx == 1 && dy == 1) { cell.LiquidAmount = 100; cell.LiquidKind = WorldLiquidKind.Lava; }
            tiles.SetInitialPopulationTile(40 + dx, 40 + dy, in cell);
        }
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 40; x <= 41; x++) tiles.SetInitialPopulationTile(x, 42, in stone);
        return tiles;
    }
}
