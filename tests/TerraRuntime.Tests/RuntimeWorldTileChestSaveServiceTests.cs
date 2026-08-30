using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldTileChestSaveServiceTests
{
    [Fact]
    public async Task Requested_save_waits_for_owner_sync_and_commits_loadable_world()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var changedTile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        source.Tiles.Set(0, 0, in changedTile);
        var chestStore = new RuntimeChestStore(
        [
            new WorldChest(
                0,
                0,
                0,
                "service-save",
                [new WorldChestItem(9, 1, 3)])
        ]);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-save-service-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);
        var service = new RuntimeWorldTileChestSaveService(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            chestStore,
            synchronizationSectionsPerTick: 1);

        try
        {
            RuntimeWorldSaveStatus initial = service.CaptureStatus();
            Assert.True(initial.AcceptingRequests);
            Assert.False(initial.TileShadowReady);
            Assert.True(initial.RemainingBootstrapSections > 0);
            Assert.False(initial.SaveRequested);
            Assert.False(initial.WriteActive);
            Assert.False(initial.PendingWrite);
            Assert.Equal(0, initial.AcceptedSnapshots);

            service.RequestSave();
            Assert.True(service.CaptureStatus().SaveRequested);
            Assert.Equal(
                RuntimeWorldTileChestSaveTickResult.SaveWaitingForSynchronization,
                service.Tick());
            Assert.True(service.IsTileShadowReady);
            Assert.True(service.IsSaveRequested);

            RuntimeWorldSaveStatus synchronized = service.CaptureStatus();
            Assert.True(synchronized.TileShadowReady);
            Assert.Equal(0, synchronized.RemainingBootstrapSections);
            Assert.True(synchronized.SaveRequested);
            Assert.Equal(0, synchronized.AcceptedSnapshots);

            Assert.Equal(RuntimeWorldTileChestSaveTickResult.SaveQueued, service.Tick());
            Assert.False(service.IsSaveRequested);
            Assert.Equal(1, service.CaptureStatus().AcceptedSnapshots);
            await service.CompleteAsync(TestContext.Current.CancellationToken);

            RuntimeWorldSaveStatus completed = service.CaptureStatus();
            Assert.False(completed.AcceptingRequests);
            Assert.True(completed.TileShadowReady);
            Assert.False(completed.SaveRequested);
            Assert.False(completed.WriteActive);
            Assert.False(completed.PendingWrite);
            Assert.Equal(1, completed.AcceptedSnapshots);
            Assert.Equal(1, completed.StartedWrites);
            Assert.Equal(1, completed.CompletedWrites);
            Assert.Equal(0, completed.CoalescedSnapshots);
            Assert.Equal(0, completed.FailedWrites);

            byte[] saved = await File.ReadAllBytesAsync(
                destinationPath,
                TestContext.Current.CancellationToken);
            WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(saved, limits, out WorldFileData? savedWorld);
            Assert.True(diagnostic.IsLoaded);
            WorldFileData loaded = Assert.IsType<WorldFileData>(savedWorld);
            Assert.True(loaded.Tiles.Get(0, 0).IsActive);
            Assert.Equal((ushort)1, loaded.Tiles.Get(0, 0).Type);
            WorldChest chest = Assert.Single(loaded.Chests);
            Assert.Equal("service-save", chest.Name);
            Assert.Equal(new WorldChestItem(9, 1, 3), Assert.Single(chest.Items));
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Final_capture_after_owner_stop_drains_post_bootstrap_dirty_state()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var chestStore = new RuntimeChestStore(
        [
            new WorldChest(0, 0, 0, "shutdown-save", [new WorldChestItem(2, 1, 0)])
        ]);
        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-final-save-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);
        var service = new RuntimeWorldTileChestSaveService(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            chestStore,
            synchronizationSectionsPerTick: 1);

        try
        {
            Assert.Equal(RuntimeWorldTileChestSaveTickResult.Synchronizing, service.Tick());
            Assert.True(service.IsTileShadowReady);

            var changedTile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
            source.Tiles.Set(1, 2, in changedTile);

            service.CaptureFinalSaveAfterOwnerStopped();
            await service.CompleteAsync(TestContext.Current.CancellationToken);

            RuntimeWorldSaveStatus completed = service.CaptureStatus();
            Assert.False(completed.AcceptingRequests);
            Assert.Equal(1, completed.AcceptedSnapshots);
            Assert.Equal(1, completed.CompletedWrites);
            Assert.Equal(0, completed.FailedWrites);

            byte[] saved = await File.ReadAllBytesAsync(
                destinationPath,
                TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(saved, limits, out WorldFileData? savedWorld).IsLoaded);
            WorldFileData loaded = Assert.IsType<WorldFileData>(savedWorld);
            WorldTile persisted = loaded.Tiles.Get(1, 2);
            Assert.True(persisted.IsActive);
            Assert.Equal((ushort)1, persisted.Type);
            Assert.True((persisted.Flags & WorldTileFlags.WireRed) != 0);
            Assert.Equal("shutdown-save", Assert.Single(loaded.Chests).Name);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Save_snapshot_patches_authoritative_runtime_clock_into_preserved_header()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var chestStore = new RuntimeChestStore(source.Chests);
        var clock = new RuntimeWorldClock(
            time: 12_345d,
            dayTime: false,
            moonPhase: VanillaMoonPhase.QuarterAtRight,
            slimeRainTime: -321d,
            dayRate: 0);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-clock-save-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);
        var service = new RuntimeWorldTileChestSaveService(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            chestStore,
            worldClock: clock,
            synchronizationSectionsPerTick: 1);

        try
        {
            service.CaptureFinalSaveAfterOwnerStopped();
            await service.CompleteAsync(TestContext.Current.CancellationToken);

            byte[] saved = await File.ReadAllBytesAsync(
                destinationPath,
                TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(saved, limits, out WorldFileData? savedWorld).IsLoaded);
            WorldFileData loaded = Assert.IsType<WorldFileData>(savedWorld);
            Assert.Equal(12_345d, loaded.RuntimeMetadata.Time);
            Assert.False(loaded.RuntimeMetadata.DayTime);
            Assert.Equal((byte)5, loaded.RuntimeMetadata.MoonPhase);
            Assert.Equal(-321d, loaded.RuntimeMetadata.SlimeRainTime);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
