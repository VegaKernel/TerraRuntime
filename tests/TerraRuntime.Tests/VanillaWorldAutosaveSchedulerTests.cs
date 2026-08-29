using TerraRuntime;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldAutosaveSchedulerTests
{
    [Fact]
    public void Dedicated_server_autosave_fires_only_after_strict_ten_minute_threshold()
    {
        long timestamp = 10_000;
        var scheduler = new VanillaWorldAutosaveScheduler(() => timestamp, timestampFrequency: 1_000);

        Assert.False(scheduler.Tick());
        Assert.True(scheduler.IsRunning);

        timestamp += VanillaWorldAutosaveScheduler.DedicatedServerIntervalMilliseconds;
        Assert.False(scheduler.Tick());
        Assert.True(scheduler.IsRunning);

        timestamp += 1;
        Assert.True(scheduler.Tick());
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void Reset_after_save_leaves_timer_stopped_until_following_owner_tick()
    {
        long timestamp = 0;
        var scheduler = new VanillaWorldAutosaveScheduler(() => timestamp, timestampFrequency: 1_000);

        Assert.False(scheduler.Tick());
        timestamp = VanillaWorldAutosaveScheduler.DedicatedServerIntervalMilliseconds + 1;
        Assert.True(scheduler.Tick());
        Assert.False(scheduler.IsRunning);

        timestamp += 50_000;
        Assert.False(scheduler.Tick());
        Assert.True(scheduler.IsRunning);

        timestamp += VanillaWorldAutosaveScheduler.DedicatedServerIntervalMilliseconds;
        Assert.False(scheduler.Tick());
        timestamp += 1;
        Assert.True(scheduler.Tick());
    }

    [Fact]
    public void Uses_real_elapsed_time_instead_of_simulation_tick_count()
    {
        long timestamp = 0;
        var scheduler = new VanillaWorldAutosaveScheduler(() => timestamp, timestampFrequency: 10_000_000);

        Assert.False(scheduler.Tick());
        timestamp += 6_000_000_000; // exactly 600 seconds
        Assert.False(scheduler.Tick());
        timestamp += 10_000; // one millisecond later
        Assert.True(scheduler.Tick());
    }

    [Fact]
    public void Timestamp_regression_restarts_interval_without_spurious_save()
    {
        long timestamp = 100_000;
        var scheduler = new VanillaWorldAutosaveScheduler(() => timestamp, timestampFrequency: 1_000);

        Assert.False(scheduler.Tick());
        timestamp = 50_000;
        Assert.False(scheduler.Tick());
        Assert.True(scheduler.IsRunning);

        timestamp += VanillaWorldAutosaveScheduler.DedicatedServerIntervalMilliseconds + 1;
        Assert.True(scheduler.Tick());
    }
}
