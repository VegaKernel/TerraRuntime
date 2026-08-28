using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldClockOperationsTelemetryTests
{
    [Fact]
    public void World_operations_follow_committed_authoritative_clock_state()
    {
        var telemetry = new RuntimeWorldClockOperationsTelemetry();
        Assert.False(telemetry.CaptureSnapshot().Available);

        var clock = new RuntimeWorldClock(
            time: 120d,
            dayTime: true,
            moonPhase: 3,
            slimeRainTime: 30d,
            dayRate: 2,
            observer: telemetry);
        var operations = new LocalRuntimeWorldOperations(CreateStaticSnapshot(), telemetry);

        RuntimeWorldSnapshot initial = operations.CaptureSnapshot();
        Assert.True(initial.RuntimeClockAvailable);
        Assert.Equal(120d, initial.RuntimeTime);
        Assert.True(initial.RuntimeDayTime);
        Assert.Equal((byte)3, initial.RuntimeMoonPhase);
        Assert.Equal(30d, initial.RuntimeSlimeRainTime);
        Assert.Equal(2, initial.RuntimeDayRate);

        clock.Tick();

        RuntimeWorldSnapshot ticked = operations.CaptureSnapshot();
        Assert.Equal(122d, ticked.RuntimeTime);
        Assert.True(ticked.RuntimeDayTime);
        Assert.Equal((byte)3, ticked.RuntimeMoonPhase);
        Assert.Equal(28d, ticked.RuntimeSlimeRainTime);
        Assert.Equal(2, ticked.RuntimeDayRate);

        clock.SetDayRate(0);

        RuntimeWorldSnapshot frozen = operations.CaptureSnapshot();
        Assert.Equal(122d, frozen.RuntimeTime);
        Assert.Equal(28d, frozen.RuntimeSlimeRainTime);
        Assert.Equal(0, frozen.RuntimeDayRate);
    }

    private static RuntimeWorldSnapshot CreateStaticSnapshot() =>
        new(
            Ready: true,
            Name: "Clock-Test",
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
}
