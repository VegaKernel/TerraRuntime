using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldClockBloodMoonTests
{
    [Fact]
    public void Persisted_night_blood_moon_is_exposed_to_npc_world_events()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            Time = 1_000,
            DayTime = false,
            MoonPhase = 0,
            BloodMoon = true
        };
        var powers = new WorldCreativePowersData(
            FreezeTime: false,
            TimeRateSlider: 0f,
            FreezeRain: false,
            FreezeWind: false,
            DifficultySlider: 0f,
            StopBiomeSpread: false);

        RuntimeWorldClock clock = RuntimeWorldClock.FromWorld(metadata, powers);

        Assert.True(clock.BloodMoonActive);
    }

    [Fact]
    public void Persisted_daytime_blood_moon_flag_is_rejected_as_inactive()
    {
        var clock = new RuntimeWorldClock(
            time: 1_000d,
            dayTime: true,
            moonPhase: VanillaMoonPhase.Full,
            slimeRainTime: 0d,
            dayRate: 1,
            bloodMoonActive: true);

        Assert.False(clock.BloodMoonActive);
    }

    [Fact]
    public void Dawn_clears_active_blood_moon()
    {
        var clock = new RuntimeWorldClock(
            time: RuntimeWorldClock.NightLength,
            dayTime: false,
            moonPhase: VanillaMoonPhase.Full,
            slimeRainTime: 0d,
            dayRate: 1,
            bloodMoonActive: true);

        clock.Tick();

        Assert.True(clock.DayTime);
        Assert.False(clock.BloodMoonActive);
    }

    [Fact]
    public void Runtime_activation_is_allowed_only_at_night()
    {
        var clock = new RuntimeWorldClock(
            time: 100d,
            dayTime: false,
            moonPhase: VanillaMoonPhase.Full,
            slimeRainTime: 0d,
            dayRate: 1);

        clock.SetBloodMoonActive(true);

        Assert.True(clock.BloodMoonActive);
    }
}
