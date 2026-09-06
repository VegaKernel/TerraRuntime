using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldLiquidLoadInitializer1458Tests
{
    [Fact]
    public void Prepare_runs_post_load_settle_without_manufacturing_dirty_sections()
    {
        WorldFileData basis = LoadBasisWorld();
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetInitialLiquid(tiles, 12, 5, 200, WorldLiquidKind.Water);
        for (int x = 0; x < 24; x++)
            SetInitialSolid(tiles, x, 15);

        WorldFileData world = basis with
        {
            Tiles = tiles,
            RuntimeMetadata = new WorldFileRuntimeMetadata()
        };

        VanillaWorldLiquidLoadPreparationDiagnostic1458 result =
            VanillaWorldLiquidLoadInitializer1458.TryPrepare(world);

        Assert.True(result.IsPrepared);
        Assert.Equal(VanillaWorldLiquidLoadPreparationResult1458.Prepared, result.Result);
        Assert.True(tiles.IsPostLoadLiquidPrepared);
        Assert.Equal((byte)0, tiles.Get(12, 5).LiquidAmount);
        Assert.Equal(200, SumLiquid(tiles, y: 14));
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Prepare_is_idempotent_after_runtime_marker_is_set()
    {
        WorldFileData basis = LoadBasisWorld();
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        tiles.MarkPostLoadLiquidPrepared();
        WorldFileData world = basis with
        {
            Tiles = tiles,
            RuntimeMetadata = new WorldFileRuntimeMetadata()
        };

        VanillaWorldLiquidLoadPreparationDiagnostic1458 result =
            VanillaWorldLiquidLoadInitializer1458.TryPrepare(world);

        Assert.Equal(VanillaWorldLiquidLoadPreparationResult1458.AlreadyPrepared, result.Result);
        Assert.True(result.IsPrepared);
        Assert.Equal(0, result.SettleIterations);
    }

    [Fact]
    public void Prepare_fails_closed_for_remix_before_mutating_liquid()
    {
        WorldFileData basis = LoadBasisWorld();
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetInitialLiquid(tiles, 12, 5, 200, WorldLiquidKind.Water);
        WorldFileData world = basis with
        {
            Tiles = tiles,
            RuntimeMetadata = new WorldFileRuntimeMetadata { RemixWorld = true }
        };

        VanillaWorldLiquidLoadPreparationDiagnostic1458 result =
            VanillaWorldLiquidLoadInitializer1458.TryPrepare(world);

        Assert.Equal(VanillaWorldLiquidLoadPreparationResult1458.UnsupportedRemixLiquidMapping, result.Result);
        Assert.False(result.IsPrepared);
        Assert.False(tiles.IsPostLoadLiquidPrepared);
        Assert.Equal((byte)200, tiles.Get(12, 5).LiquidAmount);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Prepared_store_prevents_live_simulator_from_rediscovering_the_entire_world()
    {
        var tiles = new WorldTileStore(new WorldDimensions(24, 24));
        SetInitialLiquid(tiles, 12, 10, 100, WorldLiquidKind.Water);
        tiles.MarkPostLoadLiquidPrepared();

        var simulator = new VanillaWorldLiquidSimulator1458(
            tiles,
            workBudgetPerTick: 1,
            discoveryBudgetPerTick: tiles.Count);
        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];

        Assert.True(simulator.DiscoveryComplete);
        Assert.Equal(0, simulator.Tick(0, changes));
        Assert.False(tiles.LiquidUpdates.HasPendingWork);
        Assert.Equal((byte)100, tiles.Get(12, 10).LiquidAmount);
    }

    private static WorldFileData LoadBasisWorld()
    {
        MethodInfo? createFile = typeof(WorldFileLoaderTests).GetMethod(
            "CreateCompleteCurrentWorld",
            BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? createLimits = typeof(WorldFileLoaderTests).GetMethod(
            "CreateLimits",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(createFile);
        Assert.NotNull(createLimits);

        byte[] file = Assert.IsType<byte[]>(createFile!.Invoke(null, null));
        WorldFileLoadLimits limits = Assert.IsType<WorldFileLoadLimits>(createLimits!.Invoke(null, null));
        Assert.True(WorldFileLoader.TryLoad(file, limits, out WorldFileData? loaded).IsLoaded);
        return Assert.IsType<WorldFileData>(loaded);
    }

    private static void SetInitialLiquid(
        WorldTileStore tiles,
        int x,
        int y,
        byte amount,
        WorldLiquidKind kind)
    {
        var tile = new WorldTile
        {
            LiquidAmount = amount,
            LiquidKind = kind
        };
        tiles.SetInitialPopulationTile(x, y, in tile);
    }

    private static void SetInitialSolid(WorldTileStore tiles, int x, int y)
    {
        var tile = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.SetInitialPopulationTile(x, y, in tile);
    }

    private static int SumLiquid(WorldTileStore tiles, int y)
    {
        int total = 0;
        for (int x = 0; x < tiles.Dimensions.WidthTiles; x++)
            total += tiles.Get(x, y).LiquidAmount;
        return total;
    }
}
