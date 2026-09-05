using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InitialWorldPopulationDirtyTrackingTests
{
    [Fact]
    public void Canonical_world_load_publishes_clean_trackers_then_live_mutation_marks_both_consumers()
    {
        byte[] source = CreateCompleteCurrentWorld();
        WorldFileLoadLimits limits = CreateLimits();

        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        AssertInitialPopulationIsClean(world.Tiles);

        WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, 0, 0);
        WorldTile tile = world.Tiles.Get(0, 0);
        tile.Flags ^= WorldTileFlags.WireRed;
        world.Tiles.Set(0, 0, tile);

        Assert.Equal(1, world.Tiles.DirtySections.DirtyCount);
        Assert.Equal(1, world.Tiles.PersistenceDirtySections.DirtyCount);
        Assert.True(world.Tiles.GetSectionVersion(section) > 0);
        Assert.Equal(0, world.Tiles.GetSectionVersion(section) & 1L);
    }

    [Fact]
    public void Runtime_snapshot_load_publishes_clean_trackers_then_live_mutation_marks_both_consumers()
    {
        byte[] source = CreateCompleteCurrentWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData original = Assert.IsType<WorldFileData>(loaded);

        string cachePath = Path.Combine(Path.GetTempPath(), $"terraruntime-initial-population-{Guid.NewGuid():N}.runtime-world");
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(cachePath, source, stamp, original).IsWritten);
            Assert.True(RuntimeWorldSnapshotCache.TryLoad(cachePath, stamp, limits, out WorldFileData? cached).IsLoaded);
            WorldFileData world = Assert.IsType<WorldFileData>(cached);
            AssertInitialPopulationIsClean(world.Tiles);

            int x = Math.Min(1, world.Header.Dimensions.WidthTiles - 1);
            int y = Math.Min(1, world.Header.Dimensions.HeightTiles - 1);
            WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, x, y);
            WorldTile tile = world.Tiles.Get(x, y);
            tile.Flags ^= WorldTileFlags.WireBlue;
            world.Tiles.Set(x, y, tile);

            Assert.Equal(1, world.Tiles.DirtySections.DirtyCount);
            Assert.Equal(1, world.Tiles.PersistenceDirtySections.DirtyCount);
            Assert.True(world.Tiles.GetSectionVersion(section) > 0);
        }
        finally
        {
            File.Delete(cachePath);
            File.Delete(cachePath + ".tmp");
        }
    }

    [Fact]
    public void Generation_workspace_bulk_population_never_manufactures_live_dirty_backlog()
    {
        var workspace = new Workspace(widthTiles: 201, heightTiles: 151);
        var tile = new WorldGenerationTile(
            Type: 0,
            Wall: 0,
            FrameX: -1,
            FrameY: -1,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);

        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
                Assert.True(workspace.TrySetTile(x, y, tile));
        }

        AssertInitialPopulationIsClean(workspace.TileStore);

        WorldSectionId section = TerrariaSectionGeometry.FromTile(workspace.Dimensions, 100, 75);
        WorldTile liveTile = workspace.TileStore.Get(100, 75);
        liveTile.Flags ^= WorldTileFlags.WireGreen;
        workspace.TileStore.Set(100, 75, liveTile);

        Assert.Equal(1, workspace.TileStore.DirtySections.DirtyCount);
        Assert.Equal(1, workspace.TileStore.PersistenceDirtySections.DirtyCount);
        Assert.True(workspace.TileStore.GetSectionVersion(section) > 0);
    }

    private static void AssertInitialPopulationIsClean(WorldTileStore tiles)
    {
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);

        for (int x = 0; x < tiles.Dimensions.SectionColumns; x++)
        {
            for (int y = 0; y < tiles.Dimensions.SectionRows; y++)
                Assert.Equal(0, tiles.GetSectionVersion(new WorldSectionId(x, y)));
        }
    }

    private static byte[] CreateCompleteCurrentWorld() =>
        (byte[])InvokeWorldLoaderTestHelper("CreateCompleteCurrentWorld")!;

    private static WorldFileLoadLimits CreateLimits() =>
        (WorldFileLoadLimits)InvokeWorldLoaderTestHelper("CreateLimits")!;

    private static object? InvokeWorldLoaderTestHelper(string name)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"World loader test helper '{name}' was not found.");
        return method.Invoke(null, null);
    }
}
