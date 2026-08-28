namespace TerraRuntime.Tests;

public sealed class RuntimeWorldClockTests
{
    [Fact]
    public void Day_crosses_to_night_only_after_vanilla_threshold()
    {
        var clock = new RuntimeWorldClock(
            time: RuntimeWorldClock.DayLength,
            dayTime: true,
            moonPhase: 3,
            slimeRainTime: 0d,
            dayRate: 1);

        clock.Tick();

        Assert.False(clock.DayTime);
        Assert.Equal(0d, clock.Time);
        Assert.Equal((byte)3, clock.MoonPhase);
    }

    [Fact]
    public void Night_crosses_to_day_and_advances_moon_phase()
    {
        var clock = new RuntimeWorldClock(
            time: RuntimeWorldClock.NightLength,
            dayTime: false,
            moonPhase: 7,
            slimeRainTime: 0d,
            dayRate: 1);

        clock.Tick();

        Assert.True(clock.DayTime);
        Assert.Equal(0d, clock.Time);
        Assert.Equal((byte)0, clock.MoonPhase);
    }

    [Fact]
    public void Frozen_time_keeps_time_and_slime_rain_unchanged()
    {
        var clock = new RuntimeWorldClock(
            time: 1234d,
            dayTime: true,
            moonPhase: 2,
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
            moonPhase: 2,
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
            moonPhase: 2,
            slimeRainTime: -3d,
            dayRate: 4);

        clock.Tick();

        Assert.Equal(0d, clock.SlimeRainTime);
    }
}
