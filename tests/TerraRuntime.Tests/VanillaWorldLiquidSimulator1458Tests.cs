using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldLiquidSimulator1458Tests
{
    [Fact]
    public void Full_water_blocked_below_levels_into_both_immediate_neighbours()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, byte.MaxValue);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(3, changed);
        Assert.Equal((byte)85, tiles.Get(11, 10).LiquidAmount);
        Assert.Equal((byte)85, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal((byte)85, tiles.Get(13, 10).LiquidAmount);
        Assert.Equal(255, SumLiquid(tiles, 11, 13, 10));
    }

    [Fact]
    public void Existing_same_liquid_second_neighbours_widen_leveling_to_five_cells()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 10, 10, byte.MaxValue);
        SetWater(tiles, 12, 10, 200);
        SetWater(tiles, 14, 10, byte.MaxValue);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        // TerrariaServer 1.4.5.8 Liquid.Update averages x-2..x+2 here:
        // (255 + 0 + 200 + 0 + 255) / 5 = 142.
        Assert.Equal(5, changed);
        for (int x = 10; x <= 14; x++)
            Assert.Equal((byte)142, tiles.Get(x, 10).LiquidAmount);
        Assert.Equal(710, SumLiquid(tiles, 10, 14, 10));
    }

    [Fact]
    public void Gravity_still_moves_the_full_cell_before_horizontal_leveling()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, byte.MaxValue);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(2, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(11, 10).LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(13, 10).LiquidAmount);
    }


    [Fact]
    public void Partial_gravity_transfer_continues_into_horizontal_leveling_in_same_update()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, byte.MaxValue);
        SetWater(tiles, 12, 11, 200);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(4, changed);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
        Assert.Equal((byte)67, tiles.Get(11, 10).LiquidAmount);
        Assert.Equal((byte)67, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal((byte)67, tiles.Get(13, 10).LiquidAmount);
        Assert.Single(changes[..changed].ToArray(), change => change.X == 12 && change.Y == 10);
    }

    [Fact]
    public void Full_source_over_254_target_uses_vanilla_one_unit_preservation_exception()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, byte.MaxValue);
        SetWater(tiles, 12, 11, 254);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(4, changed);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
        Assert.Equal((byte)85, tiles.Get(11, 10).LiquidAmount);
        Assert.Equal((byte)85, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal((byte)85, tiles.Get(13, 10).LiquidAmount);
    }

    [Fact]
    public void Five_cell_leveling_preserves_source_column_when_neighbours_already_match_and_liquid_is_above()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 10, 10, 100);
        SetWater(tiles, 11, 10, 100);
        SetWater(tiles, 12, 10, 101);
        SetWater(tiles, 13, 10, 100);
        SetWater(tiles, 14, 10, 100);
        SetWater(tiles, 12, 9, 1);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(0, changed);
        Assert.Equal((byte)101, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(501, SumLiquid(tiles, 10, 14, 10));
    }

    [Fact]
    public void Lava_waits_five_updates_before_flowing()
    {
        AssertDelayedFlow(WorldLiquidKind.Lava, expectedDelayedUpdates: 5);
    }

    [Fact]
    public void Honey_waits_ten_updates_before_flowing()
    {
        AssertDelayedFlow(WorldLiquidKind.Honey, expectedDelayedUpdates: 10);
    }

    [Fact]
    public void Large_work_budget_advances_lava_delay_only_once_per_server_tick()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, WorldLiquidKind.Lava);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 64,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        for (int i = 0; i < VanillaWorldLiquidSimulator1458.LavaFlowDelayUpdates1458; i++)
        {
            Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
            Assert.Equal(byte.MaxValue, tiles.Get(12, 10).LiquidAmount);
            Assert.Equal((byte)0, tiles.Get(12, 11).LiquidAmount);
        }

        Assert.Equal(2, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
    }

    [Theory]
    [InlineData(WorldLiquidKind.Lava)]
    [InlineData(WorldLiquidKind.Honey)]
    public void Quick_settle_bypasses_lava_and_honey_flow_delay(WorldLiquidKind kind)
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, kind);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(2, simulator.TickQuickSettle(changes));
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
    }

    [Fact]
    public void WaterCheck_clears_liquid_inside_ordinary_solid_but_keeps_ignored_boulder_and_bubble()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetActiveLiquidTile(tiles, 8, 10, VanillaTileIds.Stone, 100, WorldLiquidKind.Water);
        SetActiveLiquidTile(tiles, 12, 10, new TileTypeId(138), 100, WorldLiquidKind.Water);
        SetActiveLiquidTile(tiles, 16, 10, VanillaTileIds.Bubble, 100, WorldLiquidKind.Water);
        for (int x = 0; x < 24; x++)
            SetSolid(tiles, x, 11);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        VanillaWaterCheckDiagnostic1458 result = simulator.WaterCheckLoading();

        Assert.True(result.IsApplied);
        Assert.Equal((byte)0, tiles.Get(8, 10).LiquidAmount);
        Assert.Equal((byte)100, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal((byte)100, tiles.Get(16, 10).LiquidAmount);
    }

    [Fact]
    public void WaterCheck_normalizes_below_amount_above_250_to_full()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, 100);
        SetWater(tiles, 12, 11, 251);
        SetSolid(tiles, 12, 12);
        SetSolid(tiles, 11, 10);
        SetSolid(tiles, 13, 10);
        SetSolid(tiles, 11, 11);
        SetSolid(tiles, 13, 11);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        VanillaWaterCheckDiagnostic1458 result = simulator.WaterCheckLoading();

        Assert.True(result.IsApplied);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
    }

    [Fact]
    public void WaterCheck_rebuilds_active_queue_when_horizontal_amount_differs()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, 100);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        VanillaWaterCheckDiagnostic1458 result = simulator.WaterCheckLoading();

        Assert.True(result.IsApplied);
        Assert.True(tiles.LiquidUpdates.IsQueued(12, 10));
        Assert.False(tiles.LiquidUpdates.IsBuffered(12, 10));
    }

    [Fact]
    public void WaterCheck_queues_lava_when_foreign_liquid_is_above_even_if_horizontal_amounts_match()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, 100, WorldLiquidKind.Lava);
        SetLiquid(tiles, 11, 10, 100, WorldLiquidKind.Lava);
        SetLiquid(tiles, 13, 10, 100, WorldLiquidKind.Lava);
        SetWater(tiles, 12, 9, 100);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        VanillaWaterCheckDiagnostic1458 result = simulator.WaterCheckLoading();

        Assert.True(result.IsApplied);
        Assert.True(tiles.LiquidUpdates.IsQueued(12, 10));
    }

    [Fact]
    public void WaterCheck_kills_supported_single_cell_water_death_tile_without_dropping_liquid()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetActiveLiquidTile(tiles, 12, 10, VanillaTileIds.Cobweb, 100, WorldLiquidKind.Water);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        VanillaWaterCheckDiagnostic1458 result = simulator.WaterCheckLoading();

        Assert.True(result.IsApplied);
        WorldTile tile = tiles.Get(12, 10);
        Assert.False(tile.IsActive);
        Assert.Equal((byte)100, tile.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, tile.LiquidKind);
    }

    [Fact]
    public void WaterCheck_fails_closed_before_mutation_for_unsupported_multi_tile_liquid_death()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetActiveLiquidTile(tiles, 12, 10, new TileTypeId(215), 100, WorldLiquidKind.Water);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        VanillaWaterCheckDiagnostic1458 result = simulator.WaterCheckLoading();

        Assert.Equal(VanillaWaterCheckResult1458.UnsupportedLiquidDeathTile, result.Result);
        Assert.Equal(12, result.X);
        Assert.Equal(10, result.Y);
        Assert.Equal(new TileTypeId(215), result.TileType);
        Assert.True(tiles.Get(12, 10).IsActive);
        Assert.Equal((byte)100, tiles.Get(12, 10).LiquidAmount);
        Assert.False(tiles.LiquidUpdates.HasPendingWork);
    }

    [Fact]
    public void QuickWater_moves_liquid_to_the_lowest_open_cell_above_a_floor()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 5, 200);
        for (int x = 0; x < 24; x++)
            SetSolid(tiles, x, 15);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        simulator.QuickWater();

        Assert.Equal((byte)0, tiles.Get(12, 5).LiquidAmount);
        Assert.Equal(200, SumLiquid(tiles, 0, 23, 14));
    }

    [Fact]
    public void QuickWater_keeps_bubble_379_as_a_barrier()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 5, 200);
        for (int y = 5; y <= 15; y++)
        {
            SetSolid(tiles, 11, y);
            SetSolid(tiles, 13, y);
        }
        SetActiveTile(tiles, 12, 10, VanillaTileIds.Bubble);
        SetSolid(tiles, 12, 15);
        tiles.LiquidUpdates.Clear();

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        simulator.QuickWater();

        Assert.Equal((byte)200, tiles.Get(12, 9).LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(12, 11).LiquidAmount);
    }

    [Fact]
    public void QuickWater_temporarily_ignores_source_pinned_boulder_solidity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 5, 200);
        for (int y = 5; y <= 15; y++)
        {
            SetSolid(tiles, 11, y);
            SetSolid(tiles, 13, y);
        }
        SetActiveTile(tiles, 12, 10, new TileTypeId(138));
        SetSolid(tiles, 12, 15);
        tiles.LiquidUpdates.Clear();

        Assert.True(VanillaLiquidQuickWaterFacts1458.IgnoresSolidDuringSettle(new TileTypeId(138)));
        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        simulator.QuickWater();

        Assert.Equal((byte)0, tiles.Get(12, 9).LiquidAmount);
        Assert.Equal((byte)200, tiles.Get(12, 14).LiquidAmount);
    }

    [Fact]
    public void Quick_settle_loading_collision_clears_liquids_without_creating_merge_tile()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, 100, WorldLiquidKind.Lava);
        SetWater(tiles, 11, 10, 100);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.TickQuickSettle(changes);

        Assert.Equal(2, changed);
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(11, 10).LiquidAmount);
        Assert.False(tiles.Get(12, 10).IsActive);
        Assert.All(changes[..changed].ToArray(), change => Assert.False(change.RequiresTileSquareReplication));
    }

    [Fact]
    public void Quick_settle_retires_stable_liquid_after_eight_completed_updates()
    {
        var tiles = CreateBlockedStableWater(amount: 200);
        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 64, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        for (int i = 0; i < VanillaWorldLiquidSimulator1458.GeneratingOrLoadingKillUpdates1458 - 1; i++)
        {
            Assert.Equal(0, simulator.TickQuickSettle(changes));
            Assert.Equal(1, tiles.LiquidUpdates.ActiveCount);
        }

        Assert.Equal(0, simulator.TickQuickSettle(changes));
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
    }

    [Fact]
    public void Quick_settle_refills_nearly_full_source_after_partial_downward_transfer()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, byte.MaxValue);
        SetWater(tiles, 12, 11, 252);
        SetSolid(tiles, 11, 10);
        SetSolid(tiles, 13, 10);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.TickQuickSettle(changes);

        Assert.Equal(1, changed);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
        Assert.Equal((12, 11), (changes[0].X, changes[0].Y));
    }

    [Fact]
    public void Stable_dedicated_server_liquid_retires_after_ten_completed_updates_with_no_players()
    {
        var tiles = CreateBlockedStableWater(amount: 200);
        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 64,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
            Assert.Equal(1, tiles.LiquidUpdates.ActiveCount);
        }

        Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
    }

    [Fact]
    public void Dedicated_server_liquid_retirement_threshold_scales_with_first_fifteen_active_players()
    {
        var tiles = CreateBlockedStableWater(amount: 200, initialKill: 9);
        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 64,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 3, changes));
        Assert.Equal(1, tiles.LiquidUpdates.ActiveCount);

        Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 3, changes));
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
    }

    [Fact]
    public void Dedicated_server_liquid_player_window_rejects_counts_above_fifteen()
    {
        var tiles = CreateBlockedStableWater(amount: 200);
        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 1, discoveryBudgetPerTick: 1);
        var changes = new WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            simulator.Tick(VanillaWorldLiquidSimulator1458.DedicatedServerCountedPlayerSlots1458 + 1, changes));
    }

    [Fact]
    public void Stable_254_cell_is_normalized_to_255_when_liquid_entry_retires()
    {
        var tiles = CreateBlockedStableWater(amount: 254, initialKill: 9);
        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 64, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(1, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal(byte.MaxValue, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
    }

    [Fact]
    public void Amount_change_resets_kill_and_buffers_the_cell_above()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, byte.MaxValue);
        SetWater(tiles, 12, 11, 200);
        SetSolid(tiles, 11, 10);
        SetSolid(tiles, 13, 10);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10, delay: 0, kill: 9));

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 64, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.True(simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes) > 0);
        Assert.True(tiles.LiquidUpdates.IsBuffered(12, 9));
        Assert.True(tiles.LiquidUpdates.TryDequeue(out WorldLiquidUpdate source));
        Assert.Equal((12, 10), (source.X, source.Y));
        Assert.Equal(0, source.Kill);
    }

    [Fact]
    public void Underworld_water_loses_two_units_per_update_below_source_backed_layer()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 420));
        SetWater(tiles, 12, 221, 5);
        SetSolid(tiles, 11, 221);
        SetSolid(tiles, 13, 221);
        SetSolid(tiles, 12, 222);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 221));

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 64, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(1, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)3, tiles.Get(12, 221).LiquidAmount);
        Assert.True(tiles.LiquidUpdates.IsBuffered(12, 220));
    }

    [Fact]
    public void Underworld_water_does_not_evaporate_on_the_underworld_layer_itself()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 420));
        SetWater(tiles, 12, 220, 5);
        SetSolid(tiles, 11, 220);
        SetSolid(tiles, 13, 220);
        SetSolid(tiles, 12, 221);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 220));

        var simulator = new VanillaWorldLiquidSimulator1458(tiles, workBudgetPerTick: 64, discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            64 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)5, tiles.Get(12, 220).LiquidAmount);
    }

    [Fact]
    public void Lava_touching_24_units_of_water_above_places_obsidian_before_lava_delay()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, WorldLiquidKind.Lava);
        SetWater(tiles, 12, 9, 24);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(1, changed);
        Assert.Equal((byte)0, tiles.Get(12, 9).LiquidAmount);
        WorldTile merge = tiles.Get(12, 10);
        Assert.True(merge.IsActive);
        Assert.Equal(VanillaTileIds.Obsidian, merge.TileType);
        Assert.Equal((byte)0, merge.LiquidAmount);
        AssertMergeChange(
            changes[0],
            targetX: 12,
            targetY: 10,
            VanillaTileChangeType1458.LavaWater);
    }

    [Fact]
    public void Honey_touching_lava_places_crispy_honey_block()
    {
        AssertSideMerge(
            sourceKind: WorldLiquidKind.Honey,
            foreignKind: WorldLiquidKind.Lava,
            expectedTile: VanillaTileIds.CrispyHoneyBlock);
    }

    [Fact]
    public void Shimmer_touching_water_places_shimmer_block()
    {
        AssertSideMerge(
            sourceKind: WorldLiquidKind.Shimmer,
            foreignKind: WorldLiquidKind.Water,
            expectedTile: VanillaTileIds.ShimmerBlock);
    }

    [Fact]
    public void Honey_over_water_places_honey_block_in_lower_cell()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, 24, WorldLiquidKind.Honey);
        SetWater(tiles, 12, 11, byte.MaxValue);
        SetSolid(tiles, 11, 10);
        SetSolid(tiles, 13, 10);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(1, changed);
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        WorldTile merge = tiles.Get(12, 11);
        Assert.True(merge.IsActive);
        Assert.Equal(VanillaTileIds.HoneyBlock, merge.TileType);
        Assert.Equal((byte)0, merge.LiquidAmount);
        AssertMergeChange(
            changes[0],
            targetX: 12,
            targetY: 11,
            VanillaTileChangeType1458.HoneyWater);
    }

    [Fact]
    public void Foreign_liquid_below_consumes_sub_24_source_without_creating_merge_block()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, 23, WorldLiquidKind.Shimmer);
        SetWater(tiles, 12, 11, 100);
        SetSolid(tiles, 11, 10);
        SetSolid(tiles, 13, 10);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(1, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        Assert.False(tiles.Get(12, 11).IsActive);
        Assert.Equal((byte)100, tiles.Get(12, 11).LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, tiles.Get(12, 11).LiquidKind);
        Assert.True(changes[0].RequiresTileSquareReplication);
    }

    [Fact]
    public void Side_foreign_liquid_below_24_is_consumed_without_creating_merge_block()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, WorldLiquidKind.Honey);
        SetWater(tiles, 11, 10, 23);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(1, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)0, tiles.Get(11, 10).LiquidAmount);
        WorldTile source = tiles.Get(12, 10);
        Assert.False(source.IsActive);
        Assert.Equal(byte.MaxValue, source.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Honey, source.LiquidKind);
        Assert.False(changes[0].RequiresTileSquareReplication);
    }

    [Fact]
    public void Shimmer_has_final_merge_precedence_when_multiple_foreign_kinds_touch_source()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, WorldLiquidKind.Honey);
        SetWater(tiles, 11, 10, 8);
        SetLiquid(tiles, 13, 10, 8, WorldLiquidKind.Lava);
        SetLiquid(tiles, 12, 9, 8, WorldLiquidKind.Shimmer);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(1, changed);
        WorldTile merge = tiles.Get(12, 10);
        Assert.True(merge.IsActive);
        Assert.Equal(VanillaTileIds.ShimmerBlock, merge.TileType);
        Assert.Equal((byte)0, merge.LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(11, 10).LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(13, 10).LiquidAmount);
        Assert.Equal((byte)0, tiles.Get(12, 9).LiquidAmount);
        AssertMergeChange(
            changes[0],
            targetX: 12,
            targetY: 10,
            VanillaTileChangeType1458.ShimmerHoney);
    }

    [Fact]
    public void Water_wakes_adjacent_lava_and_lava_owns_the_later_merge_location()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, 24);
        SetLiquid(tiles, 11, 10, 24, WorldLiquidKind.Lava);
        SetSolid(tiles, 12, 11);
        SetSolid(tiles, 13, 10);
        SetSolid(tiles, 12, 9);
        SetSolid(tiles, 11, 11);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 2,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            2 * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.True(tiles.LiquidUpdates.IsBuffered(11, 10));
        Assert.False(tiles.Get(11, 10).IsActive);

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(1, changed);
        WorldTile merge = tiles.Get(11, 10);
        Assert.True(merge.IsActive);
        Assert.Equal(VanillaTileIds.Obsidian, merge.TileType);
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        AssertMergeChange(
            changes[0],
            targetX: 11,
            targetY: 10,
            VanillaTileChangeType1458.LavaWater);
    }

    [Fact]
    public void Rejected_merge_preparation_leaves_all_participating_liquid_cells_untouched()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, WorldLiquidKind.Lava);
        SetWater(tiles, 12, 9, 24);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var sink = new RejectingLiquidSideEffectSink();
        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1,
            sideEffects: sink);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal(byte.MaxValue, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(WorldLiquidKind.Lava, tiles.Get(12, 10).LiquidKind);
        Assert.Equal((byte)24, tiles.Get(12, 9).LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, tiles.Get(12, 9).LiquidKind);
        Assert.Equal(1, sink.PrepareCalls);
        Assert.Equal(0, sink.CommitCalls);
    }

    private static void AssertDelayedFlow(WorldLiquidKind kind, int expectedDelayedUpdates)
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, kind);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        for (int i = 0; i < expectedDelayedUpdates; i++)
        {
            Assert.Equal(0, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
            Assert.Equal(byte.MaxValue, tiles.Get(12, 10).LiquidAmount);
            Assert.Equal((byte)0, tiles.Get(12, 11).LiquidAmount);
        }

        Assert.Equal(2, simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes));
        Assert.Equal((byte)0, tiles.Get(12, 10).LiquidAmount);
        Assert.Equal(byte.MaxValue, tiles.Get(12, 11).LiquidAmount);
        Assert.Equal(kind, tiles.Get(12, 11).LiquidKind);
    }

    private static void AssertSideMerge(
        WorldLiquidKind sourceKind,
        WorldLiquidKind foreignKind,
        TileTypeId expectedTile)
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetLiquid(tiles, 12, 10, byte.MaxValue, sourceKind);
        SetLiquid(tiles, 11, 10, 24, foreignKind);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10));

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: 1);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        int changed = simulator.Tick(activeServerPlayersInLiquidWindow: 0, changes);

        Assert.Equal(1, changed);
        Assert.Equal((byte)0, tiles.Get(11, 10).LiquidAmount);
        WorldTile merge = tiles.Get(12, 10);
        Assert.True(merge.IsActive);
        Assert.Equal(expectedTile, merge.TileType);
        Assert.Equal((byte)0, merge.LiquidAmount);
        AssertMergeChange(
            changes[0],
            targetX: 12,
            targetY: 10,
            VanillaLiquidMergeCatalog1458.ResolveTileChangeType(sourceKind, foreignKind));
    }

    private static void AssertMergeChange(
        in WorldLiquidSimulationChange change,
        int targetX,
        int targetY,
        VanillaTileChangeType1458 expectedChangeType)
    {
        Assert.True(change.RequiresTileSquareReplication);
        Assert.True(change.HasExplicitTileSquare);
        Assert.Equal(targetX, change.X);
        Assert.Equal(targetY, change.Y);
        Assert.Equal(targetX - 2, change.TileSquareStartX);
        Assert.Equal(targetY - 2, change.TileSquareStartY);
        Assert.Equal((byte)3, change.TileSquareWidth);
        Assert.Equal((byte)3, change.TileSquareHeight);
        Assert.Equal(expectedChangeType, change.TileChangeType);
    }

    private static WorldTileStore CreateBlockedStableWater(byte amount, int initialKill = 0)
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetWater(tiles, 12, 10, amount);
        SetSolid(tiles, 11, 10);
        SetSolid(tiles, 13, 10);
        SetSolid(tiles, 12, 11);
        tiles.LiquidUpdates.Clear();
        Assert.True(tiles.LiquidUpdates.TryEnqueue(12, 10, delay: 0, kill: initialKill));
        return tiles;
    }

    private static void SetWater(WorldTileStore tiles, int x, int y, byte amount) =>
        SetLiquid(tiles, x, y, amount, WorldLiquidKind.Water);

    private static void SetLiquid(WorldTileStore tiles, int x, int y, byte amount, WorldLiquidKind kind)
    {
        var tile = new WorldTile
        {
            LiquidAmount = amount,
            LiquidKind = kind
        };
        tiles.Set(x, y, in tile);
    }

    private static void SetActiveTile(WorldTileStore tiles, int x, int y, TileTypeId type)
    {
        var tile = new WorldTile
        {
            Type = checked((ushort)type.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(x, y, in tile);
    }

    private static void SetActiveLiquidTile(
        WorldTileStore tiles,
        int x,
        int y,
        TileTypeId type,
        byte amount,
        WorldLiquidKind kind)
    {
        var tile = new WorldTile
        {
            Type = checked((ushort)type.Value),
            Flags = WorldTileFlags.Active,
            LiquidAmount = amount,
            LiquidKind = kind
        };
        tiles.Set(x, y, in tile);
    }

    private static void SetSolid(WorldTileStore tiles, int x, int y)
    {
        var tile = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(x, y, in tile);
    }

    private static int SumLiquid(WorldTileStore tiles, int minX, int maxX, int y)
    {
        int total = 0;
        for (int x = minX; x <= maxX; x++)
            total += tiles.Get(x, y).LiquidAmount;
        return total;
    }
    private sealed class RejectingLiquidSideEffectSink : IVanillaLiquidTileSideEffectSink1458
    {
        public int PrepareCalls { get; private set; }
        public int CommitCalls { get; private set; }

        public bool TryCutTile(int x, int y) => false;

        public bool TryPrepareMergeTile(in VanillaLiquidMergeTileRequest1458 request)
        {
            PrepareCalls++;
            return false;
        }

        public void CommitPreparedMergeTile(in VanillaLiquidMergeTileRequest1458 request) => CommitCalls++;

        public void AbortPreparedMergeTile() { }
    }

}
