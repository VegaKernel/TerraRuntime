using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class LoadingLiquidSlotScheduler1458Tests
{
    [Fact]
    public void Full_active_loading_table_leaves_buffered_work_owned_and_pending()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 200));
        for (int i = 0; i < VanillaWorldLiquidSimulator1458.LoadingActiveLiquidCapacity1458; i++)
            Assert.True(tiles.LiquidUpdates.TryEnqueue(i / 200, i % 200));
        Assert.True(tiles.LiquidUpdates.TryBuffer(125, 0));
        var simulator = new VanillaWorldLiquidSimulator1458(tiles);
        var changes = new WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.DefaultWorkBudgetPerTick * VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];
        simulator.TickQuickSettle(changes);
        Assert.Equal(VanillaWorldLiquidSimulator1458.LoadingActiveLiquidCapacity1458, tiles.LiquidUpdates.ActiveCount);
        Assert.Equal(1, tiles.LiquidUpdates.BufferedCount);
        Assert.True(tiles.LiquidUpdates.IsBuffered(125, 0));
        Assert.False(tiles.LiquidUpdates.IsQueued(125, 0));
    }

    [Fact]
    public void Oversized_imported_active_queue_fails_before_losing_any_owned_slots()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 200));
        for (int i = 0; i <= VanillaWorldLiquidSimulator1458.LoadingActiveLiquidCapacity1458; i++)
            Assert.True(tiles.LiquidUpdates.TryEnqueue(i / 200, i % 200, delay: 7, kill: 3));
        var before = tiles.LiquidUpdates.CaptureActiveSnapshot();
        var simulator = new VanillaWorldLiquidSimulator1458(tiles);
        var changes = new WorldLiquidSimulationChange[VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];
        Assert.Throws<InvalidOperationException>(() => simulator.TickQuickSettle(changes));
        Assert.Equal(before, tiles.LiquidUpdates.CaptureActiveSnapshot());
        Assert.Equal(VanillaWorldLiquidSimulator1458.LoadingActiveLiquidCapacity1458 + 1, tiles.LiquidUpdates.ActiveCount);
        Assert.True(tiles.LiquidUpdates.IsQueued(0, 0));
        Assert.True(tiles.LiquidUpdates.IsQueued(124, 199));
    }
}
