using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonSeasonClock1458Tests
{
    private static readonly DateTime NeutralDate = new(2026, 2, 1);

    [Fact]
    public void Wave_fifteen_awards_at_dawn_and_score_survives_the_counter_reset()
    {
        foreach (bool pumpkin in new[] { false, true })
        foreach (int wave in new[] { 14, 15 })
        {
            var clock = KnownClock();
            Assert.True(pumpkin ? clock.TryStartPumpkinMoon() : clock.TryStartSnowMoon());
            ReachWave(clock, wave);
            float score = clock.MoonEventTotalInvasionPoints;
            Assert.False(clock.ForceHalloweenForToday);
            Assert.False(clock.ForceXMasForToday);
            clock.Tick();
            Assert.True(clock.DayTime);
            Assert.False(clock.MoonEventActive);
            Assert.Equal(0f, clock.MoonEventTotalInvasionPoints);
            Assert.Equal(0, clock.MoonEventWaveNumber);
            bool awarded = wave == 15;
            Assert.Equal(pumpkin && awarded, clock.ForceHalloweenForToday);
            Assert.Equal(!pumpkin && awarded, clock.ForceXMasForToday);
            Assert.Equal(clock.ForceHalloweenForToday, clock.HalloweenActive);
            Assert.Equal(clock.ForceXMasForToday, clock.ChristmasActive);
            var notice = Assert.IsType<MoonEventDawnNotice1458>(clock.ConsumeMoonEventDawnNotice());
            Assert.Equal(pumpkin ? 2 : 1, notice.MoonIcon);
            Assert.Equal(score, notice.TotalPoints);
            Assert.Equal(pumpkin && awarded ? true : (bool?)null, notice.HalloweenForToday);
            Assert.Equal(!pumpkin && awarded ? true : (bool?)null, notice.XMasForToday);
            Assert.Null(clock.ConsumeMoonEventDawnNotice());
            Assert.Equal(score, notice.TotalPoints);
        }
    }

    [Fact]
    public void Victory_flags_last_through_next_night_and_expire_at_its_dawn()
    {
        var clock = KnownClock(halloween: true, christmas: true, time: RuntimeWorldClock.DayLength, dayTime: true);
        clock.Tick();
        Assert.False(clock.DayTime);
        Assert.True(clock.ForceHalloweenForToday);
        Assert.True(clock.ForceXMasForToday);
        Assert.Null(clock.ConsumeMoonEventDawnNotice());
        clock.SetDayRate((int)RuntimeWorldClock.NightLength);
        clock.Tick();
        Assert.False(clock.DayTime); // strict boundary, including temporary season state.
        Assert.True(clock.ChristmasActive);
        clock.SetDayRate(1);
        clock.Tick();
        Assert.True(clock.DayTime);
        Assert.False(clock.ForceHalloweenForToday);
        Assert.False(clock.ForceXMasForToday);
        Assert.False(clock.HalloweenActive);
        Assert.False(clock.ChristmasActive);
        Assert.Equal(new MoonEventDawnNotice1458(0, 0, false, false), clock.ConsumeMoonEventDawnNotice());
    }

    [Fact]
    public void Renewed_victory_has_no_duplicate_start_and_forever_suppresses_only_today()
    {
        foreach (bool forever in new[] { false, true })
        {
            var clock = KnownClock(halloween: true, christmas: true, forever: forever);
            Assert.True(clock.HalloweenActive);
            Assert.True(clock.ChristmasActive);
            Assert.True(clock.TryStartSnowMoon());
            ReachWave(clock, 15);
            clock.Tick();
            Assert.False(clock.ForceHalloweenForToday);
            Assert.Equal(!forever, clock.ForceXMasForToday);
            Assert.Equal(forever, clock.HalloweenActive);
            Assert.True(clock.ChristmasActive);
            var notice = Assert.IsType<MoonEventDawnNotice1458>(clock.ConsumeMoonEventDawnNotice());
            Assert.Equal(false, notice.HalloweenForToday);
            Assert.Equal(forever ? false : (bool?)null, notice.XMasForToday);
        }
    }

    [Fact]
    public void Calendar_is_evaluated_at_owned_load_and_dawn_in_xmas_then_halloween_order()
    {
        DateTime date = NeutralDate;
        int calls = 0;
        var clock = new RuntimeWorldClock(RuntimeWorldClock.DayLength, true, default, 0, 1,
            freezeWind: true, freezeRain: true,
            forceHalloweenForToday: false, forceXMasForToday: false,
            forceHalloweenForever: false, forceXMasForever: false,
            seasonDateSource: () => { calls++; return date; });
        Assert.True(clock.HasOwnedSeasonalState);
        Assert.Equal(2, calls);
        date = new(2026, 12, 20);
        clock.Tick();
        Assert.False(clock.ChristmasActive);
        Assert.Equal(2, calls);
        clock.SetDayRate((int)RuntimeWorldClock.NightLength + 1);
        clock.Tick();
        Assert.Equal(4, calls);
        Assert.True(clock.ChristmasActive);
        Assert.False(clock.HalloweenActive);
        Assert.Null(clock.ConsumeMoonEventDawnNotice()); // Calendar changes do not invent victory notices.

        int orderedCalls = 0;
        var ordered = new RuntimeWorldClock(0, true, default, 0, 0,
            forceHalloweenForToday: false, forceXMasForToday: false,
            forceHalloweenForever: false, forceXMasForever: false,
            seasonDateSource: () => ++orderedCalls == 1 ? new(2026, 12, 20) : new(2026, 10, 20));
        Assert.True(ordered.ChristmasActive);
        Assert.True(ordered.HalloweenActive);
    }

    [Fact]
    public void Unowned_scalar_context_does_not_read_calendar_or_invent_today_flags_and_preview_cannot_drain_live_notice()
    {
        var unknown = new RuntimeWorldClock(RuntimeWorldClock.NightLength, false, default, 0, 1,
            freezeWind: true, freezeRain: true,
            seasonDateSource: () => throw new InvalidOperationException("Missing seasonal baseline."));
        Assert.False(unknown.HasOwnedSeasonalState);
        Assert.True(unknown.TryStartSnowMoon());
        ReachWave(unknown, 15);
        unknown.Tick();
        Assert.False(unknown.ForceXMasForToday);
        Assert.Null(unknown.ConsumeMoonEventDawnNotice()!.Value.XMasForToday);

        var live = KnownClock(halloween: true);
        live.Tick();
        var preview = live.CreateDeathPreview();
        var retained = preview.ConsumeMoonEventDawnNotice();
        Assert.NotNull(retained);
        Assert.Equal(retained, live.ConsumeMoonEventDawnNotice());
        Assert.Null(live.ConsumeMoonEventDawnNotice());
    }

    [Fact]
    public void World_load_preserves_raw_today_forever_combination_until_the_next_dawn()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            Time = (int)RuntimeWorldClock.NightLength, DayTime = false,
            ForceHalloweenForToday = true, ForceXMasForToday = true,
            ForceHalloweenForever = true, ForceXMasForever = true
        };
        var clock = RuntimeWorldClock.FromWorld(metadata, new(false, 0, true, true, 0, false),
            seasonDateSource: () => NeutralDate);
        Assert.True(clock.HasOwnedSeasonalState);
        Assert.True(clock.ForceHalloweenForToday);
        Assert.True(clock.ForceXMasForToday);
        clock.Tick();
        Assert.False(clock.ForceHalloweenForToday);
        Assert.False(clock.ForceXMasForToday);
        Assert.True(clock.HalloweenActive);
        Assert.True(clock.ChristmasActive);
        Assert.Equal(new MoonEventDawnNotice1458(0, 0, false, false), clock.ConsumeMoonEventDawnNotice());
    }

    private static RuntimeWorldClock KnownClock(bool halloween = false, bool christmas = false,
        bool forever = false, double time = RuntimeWorldClock.NightLength, bool dayTime = false) =>
        new(time, dayTime, default, 0, 1, freezeWind: true, freezeRain: true,
            forceHalloweenForToday: halloween, forceXMasForToday: christmas,
            forceHalloweenForever: forever, forceXMasForever: forever, seasonDateSource: () => NeutralDate);

    private static void ReachWave(RuntimeWorldClock clock, int wave)
    {
        int kills = 0;
        while (clock.MoonEventWaveNumber < wave)
        {
            Assert.True(kills++ < 1000);
            Assert.True(clock.TryAdvanceMoonEventDeath(new(clock.SnowMoonActive ? 345 : 327), false, false));
        }
    }
}
