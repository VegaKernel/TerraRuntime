using TerraRuntime.Core;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class WorldSaveTimingTelemetryTests
{
    [Fact]
    public async Task Coordinator_reports_capture_serialization_and_atomic_write_durations()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await using var coordinator = new WorldSaveCoordinator<int>(
                destination,
                () =>
                {
                    Thread.Sleep(5);
                    return 42;
                },
                async (snapshot, stream, token) =>
                {
                    Assert.Equal(42, snapshot);
                    await Task.Delay(10, token);
                    byte[] payload = [0x2A];
                    await stream.WriteAsync(payload, token);
                });

            coordinator.RequestSave();
            await coordinator.CompleteAsync(cancellationToken);

            WorldSaveCoordinatorTimingSnapshot timing = coordinator.CaptureTimingSnapshot();
            Assert.True(timing.LastSnapshotCaptureDuration > TimeSpan.Zero);
            Assert.True(timing.LastSerializationDuration > TimeSpan.Zero);
            Assert.True(timing.LastWriteDuration >= timing.LastSerializationDuration);
            Assert.Equal(timing.LastSnapshotCaptureDuration, timing.TotalSnapshotCaptureDuration);
            Assert.Equal(timing.LastSerializationDuration, timing.TotalSerializationDuration);
            Assert.Equal(timing.LastWriteDuration, timing.TotalWriteDuration);
            Assert.Equal(new byte[] { 0x2A }, await File.ReadAllBytesAsync(destination, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void World_operations_project_persistence_timing_without_exposing_coordinator()
    {
        var persistence = new global::TerraRuntime.RuntimeWorldSaveStatus(
            AcceptingRequests: true,
            TileShadowReady: true,
            RemainingBootstrapSections: 0,
            PendingDirtyTileSections: 0,
            SaveRequested: false,
            WriteActive: false,
            PendingWrite: false,
            AcceptedSnapshots: 4,
            StartedWrites: 3,
            CompletedWrites: 3,
            CoalescedSnapshots: 1,
            FailedWrites: 0,
            LastSnapshotCaptureDuration: TimeSpan.FromMilliseconds(1.25),
            LastSerializationDuration: TimeSpan.FromMilliseconds(12.5),
            LastWriteDuration: TimeSpan.FromMilliseconds(16.75),
            TotalSnapshotCaptureDuration: TimeSpan.FromMilliseconds(5),
            TotalSerializationDuration: TimeSpan.FromMilliseconds(40),
            TotalWriteDuration: TimeSpan.FromMilliseconds(55));
        var operations = new LocalRuntimeWorldOperations(
            CreateStaticSnapshot(),
            persistenceSnapshotProvider: () => persistence);

        RuntimeWorldSnapshot snapshot = operations.CaptureSnapshot();
        RuntimeWorldPersistenceSnapshot mapped = Assert.IsType<RuntimeWorldPersistenceSnapshot>(snapshot.Persistence);

        Assert.Equal(1.25, mapped.LastSnapshotCaptureMilliseconds);
        Assert.Equal(12.5, mapped.LastSerializationMilliseconds);
        Assert.Equal(16.75, mapped.LastWriteMilliseconds);
        Assert.Equal(5, mapped.TotalSnapshotCaptureMilliseconds);
        Assert.Equal(40, mapped.TotalSerializationMilliseconds);
        Assert.Equal(55, mapped.TotalWriteMilliseconds);
    }

    private static RuntimeWorldSnapshot CreateStaticSnapshot() =>
        new(
            Ready: true,
            Name: "Save-Timing-Test",
            WorldId: 1,
            UniqueId: Guid.Empty,
            FormatVersion: 326,
            WorldGeneratorVersion: 0,
            WidthTiles: 100,
            HeightTiles: 100,
            TileCount: 10_000,
            ChestCount: 0,
            SignCount: 0,
            TownNpcCount: 0,
            PersistentNpcCount: 0,
            TileEntityCount: 0,
            PressurePlateCount: 0,
            TownRoomCount: 0,
            RuntimeCacheHit: false,
            InitialCacheResult: "None",
            CacheParallelReads: 1,
            FileReadMilliseconds: 0,
            CacheLoadMilliseconds: 0,
            CanonicalWorldLoadMilliseconds: 0,
            CacheWriteMilliseconds: 0,
            BootstrapMilliseconds: 0,
            WorldReadyMilliseconds: 0,
            NetworkReadyMilliseconds: 0,
            CapturedAtUtc: DateTimeOffset.UnixEpoch);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-SaveTiming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
