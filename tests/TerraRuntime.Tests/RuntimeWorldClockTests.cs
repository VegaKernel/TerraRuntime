using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldClockTests
{
    [Theory]
    [InlineData(-.6f)]
    [InlineData(.6f)]
    public void World_load_initializes_current_wind_from_persisted_target(float wind)
    {
        var metadata = new WorldFileRuntimeMetadata { WindSpeed = wind };
        var clock = RuntimeWorldClock.FromWorld(metadata, new WorldCreativePowersData(false, 0f, false, false, .5f, false));
        Assert.Equal(wind, clock.WindSpeedCurrent);
        Assert.Equal(wind, clock.WindSpeedTarget);
    }

    [Fact]
    public void Current_wind_eases_toward_the_source_target_before_time_advances()
    {
        var clock = new RuntimeWorldClock(0d, true, default, 0d, 1);
        clock.SetWindSpeedTarget(.1f);

        clock.Tick();

        Assert.Equal(.00045f, clock.WindSpeedCurrent, 7);
        Assert.Equal(.1f, clock.WindSpeedTarget);
        Assert.Equal(1d, clock.Time);
    }

    [Fact]
    public void Current_wind_uses_the_source_rain_scaled_target()
    {
        var metadata = new WorldFileRuntimeMetadata { WindSpeed = .1f, MaxRain = .5f };
        var clock = RuntimeWorldClock.FromWorld(metadata, new WorldCreativePowersData(false, 0f, false, false, .5f, false));

        clock.Tick();

        float effectiveTarget = .1f * (1f + 5f / 9f * .5f);
        float expectedCurrent = .1f + .0003f + (effectiveTarget - .1f) * .0015f;
        Assert.Equal(.1f, clock.WindSpeedTarget);
        Assert.Equal(.5f, clock.MaxRain);
        Assert.Equal(expectedCurrent, clock.WindSpeedCurrent, 7);
    }

    [Fact]
    public void Day_crosses_to_night_only_after_vanilla_threshold()
    {
        var clock = new RuntimeWorldClock(
            time: RuntimeWorldClock.DayLength,
            dayTime: true,
            moonPhase: VanillaMoonPhase.QuarterAtLeft,
            slimeRainTime: 0d,
            dayRate: 1);

        clock.Tick();

        Assert.False(clock.DayTime);
        Assert.Equal(0d, clock.Time);
        Assert.Equal(VanillaMoonPhase.QuarterAtLeft, clock.MoonPhase);
    }

    [Fact]
    public void Night_crosses_to_day_and_advances_moon_phase()
    {
        var clock = new RuntimeWorldClock(
            time: RuntimeWorldClock.NightLength,
            dayTime: false,
            moonPhase: VanillaMoonPhase.ThreeQuartersAtRight,
            slimeRainTime: 0d,
            dayRate: 1);

        clock.Tick();

        Assert.True(clock.DayTime);
        Assert.Equal(0d, clock.Time);
        Assert.Equal(VanillaMoonPhase.Full, clock.MoonPhase);
    }

    [Fact]
    public void Frozen_time_keeps_time_and_slime_rain_unchanged()
    {
        var clock = new RuntimeWorldClock(
            time: 1234d,
            dayTime: true,
            moonPhase: VanillaMoonPhase.HalfAtLeft,
            slimeRainTime: 50d,
            dayRate: 0);

        clock.Tick();

        Assert.Equal(1234d, clock.Time);
        Assert.Equal(50d, clock.SlimeRainTime);
        Assert.True(clock.SlimeRainActive);
    }

    [Fact]
    public void Slime_rain_countdown_uses_same_day_rate_and_clamps_at_zero()
    {
        var clock = new RuntimeWorldClock(
            time: 100d,
            dayTime: true,
            moonPhase: VanillaMoonPhase.HalfAtLeft,
            slimeRainTime: 3d,
            dayRate: 4);

        clock.Tick();

        Assert.Equal(104d, clock.Time);
        Assert.Equal(0d, clock.SlimeRainTime);
        Assert.False(clock.SlimeRainActive);
    }

    [Fact]
    public void Negative_slime_rain_cooldown_moves_toward_zero()
    {
        var clock = new RuntimeWorldClock(
            time: 100d,
            dayTime: true,
            moonPhase: VanillaMoonPhase.HalfAtLeft,
            slimeRainTime: -3d,
            dayRate: 4);

        clock.Tick();

        Assert.Equal(0d, clock.SlimeRainTime);
    }

    [Fact]
    public void Persisted_creative_slider_maps_to_vanilla_one_through_twenty_four_rate()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            Time = 123,
            DayTime = false,
            MoonPhase = 5,
            SlimeRainTime = 42d
        };
        var powers = new WorldCreativePowersData(
            FreezeTime: false,
            TimeRateSlider: 0.5f,
            FreezeRain: false,
            FreezeWind: false,
            DifficultySlider: 0f,
            StopBiomeSpread: false);

        RuntimeWorldClock clock = RuntimeWorldClock.FromWorld(metadata, powers);

        Assert.Equal(12, clock.DayRate);
        Assert.Equal(123d, clock.Time);
        Assert.False(clock.DayTime);
        Assert.Equal(VanillaMoonPhase.QuarterAtRight, clock.MoonPhase);
        Assert.Equal(42d, clock.SlimeRainTime);
    }

    [Fact]
    public void Persisted_freeze_time_overrides_slider_rate()
    {
        var metadata = new WorldFileRuntimeMetadata();
        var powers = new WorldCreativePowersData(
            FreezeTime: true,
            TimeRateSlider: 1f,
            FreezeRain: false,
            FreezeWind: false,
            DifficultySlider: 0f,
            StopBiomeSpread: false);

        RuntimeWorldClock clock = RuntimeWorldClock.FromWorld(metadata, powers);

        Assert.Equal(0, clock.DayRate);
    }
}
