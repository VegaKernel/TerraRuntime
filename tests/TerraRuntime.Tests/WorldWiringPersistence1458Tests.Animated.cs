using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WorldWiringPersistence1458Tests
{
    [Theory]
    [InlineData(105)] [InlineData(349)] [InlineData(593)] [InlineData(594)]
    public async Task Animated_device_material_survives_checkpoint_and_cache_without_replaying_visual_state(ushort type)
    {
        byte[] canonical = CreateWorld(out var limits);
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out var loaded).IsLoaded);
        var world = Assert.IsType<WorldFileData>(loaded);
        WiringAnimatedDevice1458Tests.PlaceDevice(world.Tiles, type);
        for (int x = 20; x < 30; x++) world.Tiles.Set(x, 30, new WorldTile { Type = (ushort)(x == 20 ? 136 : 0),
            Flags = x == 20 ? WorldTileFlags.Active | WorldTileFlags.WireRed : WorldTileFlags.WireRed });
        using var player = new SpawnedPlayer(world.Tiles);
        world.Tiles.DirtySections.Clear(); world.Tiles.PersistenceDirtySections.Clear();
        player.State.Apply(new ClientHitSwitchRuntimeCommand1458(player.Connection, new(20, 30)));
        Assert.True(world.Tiles.PersistenceDirtySections.DirtyCount > 0);
        var expected = world.Tiles.Get(30, 30);
        Assert.Equal(type == 105 ? (ushort)349 : type, expected.Type);
        Assert.True(WorldFilePreservedSections.TryCapture(canonical, world.Envelope, out var preserved));
        string directory = Path.Combine(Path.GetTempPath(), $"TerraRuntime-AnimatedSave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "world.wld");
        var coordinator = new RuntimeWorldCheckpointCoordinator(path, world.Envelope, world.Header, preserved!,
            world.Tiles, new RuntimeChestStore(world.Chests), synchronizationSectionsPerTick: 1, checkpointValidationLimits: limits);
        try
        {
            coordinator.CaptureFinalSaveAfterOwnerStopped();
            await coordinator.CompleteAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, coordinator.CaptureStatus().CompletedWrites);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(bytes, limits, out var restored).IsLoaded);
            Assert.Equal(expected, Assert.IsType<WorldFileData>(restored).Tiles.Get(30, 30));
            Assert.True(RuntimeWorldSnapshotCache.TryLoadValidatedSource(RuntimeWorldSnapshotCache.GetCachePath(path), path, limits, out var cache).IsLoaded);
            var reloaded = Assert.IsType<WorldFileData>(cache).Tiles;
            Assert.Equal(expected, reloaded.Get(30, 30));
            using var resumed = new SpawnedPlayer(reloaded);
            for (int tick = 0; tick < 65; tick++) resumed.State.Tick();
            Assert.Equal(expected, reloaded.Get(30, 30));
            resumed.State.Apply(new ClientHitSwitchRuntimeCommand1458(resumed.Connection, new(20, 30)));
            Assert.Equal((short)0, reloaded.Get(30, 30).FrameX);
            Assert.Equal(expected, world.Tiles.Get(30, 30));
        }
        finally
        {
            await coordinator.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
