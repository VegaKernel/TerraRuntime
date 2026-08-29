using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldCacheOperationsTelemetryTests
{
    [Fact]
    public void World_operations_preserve_cache_reason_detail_and_startup_timings()
    {
        var source = new RuntimeWorldSnapshot(
            Ready: true,
            Name: "Cache-Telemetry-Test",
            WorldId: 7,
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
            InitialCacheResult: "PayloadHashMismatch",
            CacheParallelReads: 4,
            FileReadMilliseconds: 11.25,
            CacheLoadMilliseconds: 2.5,
            CanonicalWorldLoadMilliseconds: 7.75,
            CacheWriteMilliseconds: 3.125,
            BootstrapMilliseconds: 1.5,
            WorldReadyMilliseconds: 12.0,
            NetworkReadyMilliseconds: 14.0,
            CapturedAtUtc: DateTimeOffset.UnixEpoch,
            InitialCacheDetailCode: 9);
        var operations = new LocalRuntimeWorldOperations(source);

        RuntimeWorldSnapshot snapshot = operations.CaptureSnapshot();

        Assert.False(snapshot.RuntimeCacheHit);
        Assert.Equal("PayloadHashMismatch", snapshot.InitialCacheResult);
        Assert.Equal(9, snapshot.InitialCacheDetailCode);
        Assert.Equal(4, snapshot.CacheParallelReads);
        Assert.Equal(11.25, snapshot.FileReadMilliseconds);
        Assert.Equal(2.5, snapshot.CacheLoadMilliseconds);
        Assert.Equal(7.75, snapshot.CanonicalWorldLoadMilliseconds);
        Assert.Equal(3.125, snapshot.CacheWriteMilliseconds);
        Assert.Equal(1.5, snapshot.BootstrapMilliseconds);
        Assert.Equal(12.0, snapshot.WorldReadyMilliseconds);
        Assert.Equal(14.0, snapshot.NetworkReadyMilliseconds);
    }
}
