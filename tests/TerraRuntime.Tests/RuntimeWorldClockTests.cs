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
    public void Weather_wind_runs_once_per_day_rate_unit_and_freeze_time_stops_it()
    {
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 2,
            windCounter: 100, extremeWindCounter: 100);
        clock.SetWindSpeedTarget(.1f);

        clock.Tick();

        Assert.Equal(.000899325f, clock.WindSpeedCurrent, 8);

        var frozen = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 0,
            windSpeedCurrent: 0f, windCounter: 1, extremeWindCounter: 1,
            weatherRandom: new ThrowingWeatherRandom());
        frozen.SetWindSpeedTarget(.1f);
        frozen.Tick();

        Assert.Equal(0f, frozen.WindSpeedCurrent);
        Assert.Equal(.1f, frozen.WindSpeedTarget);
    }

    [Fact]
    public void Wind_target_changes_clamp_before_the_first_120_life_player_joins()
    {
        var weather = new SequenceWeatherRandom(3, 1, 100, 0);
        var counter = new SequenceWindCounterRandom(900);
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 1,
            windSpeedCurrent: .34f, windCounter: 1, extremeWindCounter: 2,
            weatherRandom: weather, windCounterRandom: counter, freezeRain: true);
        clock.SetWindSpeedTarget(.34f);

        clock.Tick();

        Assert.Equal(.35f, clock.WindSpeedTarget, 6);
        Assert.Equal(900, counter.LastValue);

        var eligible = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 1,
            windSpeedCurrent: .34f, windCounter: 1, extremeWindCounter: 2,
            weatherRandom: new SequenceWeatherRandom(3, 1, 100, 0),
            windCounterRandom: new SequenceWindCounterRandom(900), freezeRain: true);
        eligible.SetWeatherEligiblePlayerProvider(static () => true);
        eligible.SetWindSpeedTarget(.34f);
        eligible.Tick();

        Assert.Equal(.44f, eligible.WindSpeedTarget, 6);
    }

    [Fact]
    public void Freeze_wind_keeps_the_target_schedule_still_but_not_source_easing()
    {
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 1,
            freezeWind: true, windCounter: 1, extremeWindCounter: 1,
            weatherRandom: new ThrowingWeatherRandom());
        clock.SetWindSpeedTarget(.1f);

        clock.Tick();

        Assert.Equal(.00045f, clock.WindSpeedCurrent, 7);
        Assert.Equal(.1f, clock.WindSpeedTarget);
    }

    [Fact]
    public void Eligible_players_start_source_timed_rain_and_publish_its_packet_strength()
    {
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 1,
            windCounter: 100, extremeWindCounter: 100,
            weatherRandom: new SequenceWeatherRandom(
                0, 28_800,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1,
                1, 20));
        clock.SetWeatherEligiblePlayerProvider(static () => true);

        clock.Tick();

        Assert.True(clock.Raining);
        Assert.Equal(28_800, clock.RainTime);
        Assert.Equal(.2f, clock.MaxRain, 6);
        Assert.Equal(.2f, clock.NetworkRain, 6);
        Assert.True(clock.ConsumeWorldInfoSyncRequest());
    }

    [Fact]
    public void Source_rain_countdown_stops_and_clears_packet_strength_at_zero()
    {
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 1,
            maxRain: .4f, raining: true, rainTime: 1,
            windCounter: 100, extremeWindCounter: 100,
            weatherRandom: new ThrowingWeatherRandom());

        clock.Tick();

        Assert.False(clock.Raining);
        Assert.Equal(0, clock.RainTime);
        Assert.Equal(0f, clock.MaxRain);
        Assert.Equal(0f, clock.NetworkRain);
        Assert.True(clock.ConsumeWorldInfoSyncRequest());
    }

    [Fact]
    public void Freeze_rain_preserves_the_active_interval_without_consuming_weather_randomness()
    {
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 2,
            maxRain: .4f, raining: true, rainTime: 20, freezeRain: true,
            windCounter: 100, extremeWindCounter: 100,
            weatherRandom: new ThrowingWeatherRandom());

        clock.Tick();

        Assert.True(clock.Raining);
        Assert.Equal(20, clock.RainTime);
        Assert.Equal(.4f, clock.MaxRain);
        Assert.False(clock.ConsumeWorldInfoSyncRequest());
    }

    [Fact]
    public void Extreme_wind_branch_uses_source_target_ranges_and_counter_bonuses()
    {
        var counters = new SequenceWindCounterRandom(900, 10);
        var clock = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 1,
            windSpeedCurrent: .1f, windCounter: 1, extremeWindCounter: 1,
            weatherRandom: new SequenceWeatherRandom(0, 0, 14, 14, 800, 5, 10, 15, 0),
            windCounterRandom: counters, freezeRain: true);
        clock.SetWeatherEligiblePlayerProvider(static () => true);
        clock.SetWindSpeedTarget(.1f);

        clock.Tick();

        Assert.Equal(.8f, clock.WindSpeedTarget, 6);
        Assert.Equal(10, counters.LastValue);
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
    public void Slime_rain_kill_counter_uses_source_threshold_and_post_spawn_cooldown()
    {
        var clock = new RuntimeWorldClock(0d, true, default, slimeRainTime: 1d, dayRate: 0);

        for (int index = 0; index < 149; index++)
            Assert.False(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: true, kingSlimeActive: false, kingSlimeDowned: false));

        Assert.Equal(149, clock.SlimeRainKillCount);
        Assert.True(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: true, kingSlimeActive: false, kingSlimeDowned: false));
        Assert.Equal(-75, clock.SlimeRainKillCount);
        Assert.False(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: true, kingSlimeActive: false, kingSlimeDowned: false));
        Assert.Equal(-74, clock.SlimeRainKillCount);
    }

    [Fact]
    public void Slime_rain_counter_uses_post_king_slime_threshold_and_ignores_blocked_deaths()
    {
        var clock = new RuntimeWorldClock(0d, true, default, slimeRainTime: 1d, dayRate: 0);

        Assert.False(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: false, kingSlimeActive: false, kingSlimeDowned: true));
        Assert.False(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: true, kingSlimeActive: true, kingSlimeDowned: true));
        Assert.Equal(0, clock.SlimeRainKillCount);
        for (int index = 0; index < 74; index++)
            Assert.False(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: true, kingSlimeActive: false, kingSlimeDowned: true));

        Assert.True(clock.TryAdvanceSlimeRainKillCount(slimeRainNpc: true, kingSlimeActive: false, kingSlimeDowned: true));
        Assert.Equal(-37, clock.SlimeRainKillCount);
    }

    [Fact]
    public void Frost_moon_deaths_use_source_points_and_discard_wave_overflow()
    {
        var clock = new RuntimeWorldClock(0d, false, default, 0d, dayRate: 0, snowMoon: true);

        Assert.Equal(1, clock.MoonEventWaveNumber);
        for (int index = 0; index < 24; index++)
            Assert.True(clock.TryAdvanceMoonEventDeath(new NpcTypeId(338), expertMode: false, masterMode: false));

        Assert.Equal(24f, clock.MoonEventWaveKills);
        Assert.Equal(25, clock.MoonEventWaveRequirement);
        Assert.Equal(24, clock.MoonEventProgressRevision);
        Assert.True(clock.TryAdvanceMoonEventDeath(new NpcTypeId(338), expertMode: false, masterMode: false));

        Assert.Equal(2, clock.MoonEventWaveNumber);
        Assert.Equal(0f, clock.MoonEventWaveKills);
        Assert.Equal(40, clock.MoonEventWaveRequirement);
        Assert.Equal(25, clock.MoonEventProgressRevision);
        Assert.Equal(25f, clock.MoonEventTotalInvasionPoints);
    }

    [Fact]
    public void Pumpkin_moon_applies_master_scalar_and_ignores_unrelated_deaths()
    {
        var clock = new RuntimeWorldClock(0d, false, default, 0d, dayRate: 0, pumpkinMoon: true);

        Assert.False(clock.TryAdvanceMoonEventDeath(new NpcTypeId(1), expertMode: false, masterMode: false));
        Assert.True(clock.TryAdvanceMoonEventDeath(new NpcTypeId(315), expertMode: true, masterMode: true));

        Assert.Equal(2, clock.MoonEventWaveNumber);
        Assert.Equal(0f, clock.MoonEventWaveKills);
        Assert.Equal(125f, clock.MoonEventTotalInvasionPoints);
        Assert.Throws<ArgumentException>(() => clock.TryAdvanceMoonEventDeath(new NpcTypeId(315), expertMode: false, masterMode: true));
    }

    [Fact]
    public void Moon_events_start_only_at_night_and_clear_their_transient_progress_at_dawn()
    {
        var day = new RuntimeWorldClock(0d, true, default, 0d, dayRate: 0);
        Assert.False(day.TryStartPumpkinMoon());

        var night = new RuntimeWorldClock(RuntimeWorldClock.NightLength, false, default, 0d, dayRate: 1);
        Assert.True(night.TryStartSnowMoon());
        Assert.True(night.SnowMoonActive);
        Assert.True(night.ConsumeWorldInfoSyncRequest());
        Assert.False(night.TryStartPumpkinMoon());
        Assert.True(night.TryAdvanceMoonEventDeath(new NpcTypeId(338), expertMode: false, masterMode: false));

        night.Tick();

        Assert.True(night.DayTime);
        Assert.False(night.MoonEventActive);
        Assert.Equal(0, night.MoonEventWaveNumber);
        Assert.Equal(0f, night.MoonEventWaveKills);
        Assert.Equal(0f, night.MoonEventTotalInvasionPoints);
        Assert.True(night.ConsumeWorldInfoSyncRequest());
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

    private sealed class SequenceWeatherRandom(params int[] values) : IRuntimeWeatherRandom1458
    {
        private readonly Queue<int> values = new(values);

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = values.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }

    private sealed class SequenceWindCounterRandom(params int[] values) : IRuntimeWindCounterRandom1458
    {
        private readonly Queue<int> values = new(values);

        public int LastValue { get; private set; }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = values.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            LastValue = value;
            return value;
        }
    }

    private sealed class ThrowingWeatherRandom : IRuntimeWeatherRandom1458
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) =>
            throw new Xunit.Sdk.XunitException("Frozen weather must not consume target-selection randomness.");
    }
}
