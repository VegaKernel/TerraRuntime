using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldClockGetGoodWorldTests
{
    [Fact]
    public void Persisted_getGoodWorld_is_preserved_while_npc_door_projection_suppresses_blood_moon_pressure()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            Time = 1_000,
            DayTime = false,
            MoonPhase = 0,
            BloodMoon = true,
            GetGoodWorld = true
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
        Assert.True(clock.GetGoodWorld);
        Assert.False(((IVanillaNpcWorldEventState)clock).BloodMoonActive);
    }

    [Fact]
    public void Ordinary_world_exposes_real_blood_moon_to_npc_door_projection()
    {
        var clock = new RuntimeWorldClock(
            time: 100d,
            dayTime: false,
            moonPhase: VanillaMoonPhase.Full,
            slimeRainTime: 0d,
            dayRate: 1,
            bloodMoonActive: true,
            getGoodWorld: false);

        Assert.True(((IVanillaNpcWorldEventState)clock).BloodMoonActive);
    }
}
