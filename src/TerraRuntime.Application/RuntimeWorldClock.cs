using TerraRuntime.World;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Application;

/// <summary>
/// Narrow world-event projection consumed by admitted NPC behavior. Blood Moon is suppressed for restricted
/// AI_003 fighters in For-the-Worthy, while Slime Rain and the persisted blue-town-slime unlock remain available
/// to the source-backed King Slime death slice.
/// </summary>
internal interface IVanillaNpcWorldEventState
{
    bool BloodMoonActive { get; }
    bool GetGoodWorld { get; }
    bool SlimeRainActive { get; }
    bool SlimeBlueSpawnUnlocked { get; }

    bool TryStopSlimeRain(IKingSlimeDeathRandom random);
    void MarkSlimeBlueSpawnUnlocked();
}

/// <summary>Random calls owned by TerrariaServer 1.4.5.8 King Slime death effects.</summary>
internal interface IKingSlimeDeathRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
    float NextFloatDirection();
}

internal sealed class SystemKingSlimeDeathRandom : IKingSlimeDeathRandom
{
    private readonly Random random = new();

    public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);

    public float NextFloatDirection() => random.NextSingle() * 2f - 1f;
}

/// <summary>Random draws made by the server-owned target-selection branch of <c>Main.UpdateWeather</c>.</summary>
internal interface IRuntimeWeatherRandom1458
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}

/// <summary>Source-compatible independent counter source used by <c>Main.ResetWindCounter</c>.</summary>
internal interface IRuntimeWindCounterRandom1458
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}

internal sealed class SystemRuntimeWeatherRandom1458 : IRuntimeWeatherRandom1458
{
    private readonly Random random = new();

    public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
}

internal sealed class SystemRuntimeWindCounterRandom1458 : IRuntimeWindCounterRandom1458
{
    private readonly Random random = new();

    public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
}

/// <summary>
/// Authoritative ordinary-world time slice backed by TerrariaServer 1.4.5.8 Main.UpdateTime.
/// NPCs consume the current state before this clock advances each game tick, matching vanilla's
/// DoUpdateInWorld ordering where UpdateWorld_NPCs runs before UpdateWorld_Time.
/// </summary>
internal sealed class RuntimeWorldClock : IVanillaNpcWorldEventState
{
    public const double DayLength = 54_000d;
    public const double NightLength = 32_400d;

    private readonly IRuntimeWorldClockObserver? _observer;
    private readonly IRuntimeWeatherRandom1458 _weatherRandom;
    private readonly IRuntimeWindCounterRandom1458 _windCounterRandom;
    private int _dayRate;
    private int windCounter;
    private int extremeWindCounter;
    private bool freezeWind;
    private readonly bool freezeRain;
    private readonly bool cloudBackgroundActive;
    private readonly byte cloudCount;
    private Func<bool>? hasWeatherEligiblePlayer;

    public RuntimeWorldClock(
        double time,
        bool dayTime,
        VanillaMoonPhase moonPhase,
        double slimeRainTime,
        int dayRate,
        IRuntimeWorldClockObserver? observer = null,
        bool bloodMoonActive = false,
        bool getGoodWorld = false,
        bool slimeBlueSpawnUnlocked = false,
        float windSpeedCurrent = 0f,
        float maxRain = 0f,
        bool freezeWind = false,
        IRuntimeWeatherRandom1458? weatherRandom = null,
        IRuntimeWindCounterRandom1458? windCounterRandom = null,
        int windCounter = -1,
        int extremeWindCounter = -1,
        bool freezeRain = false,
        bool raining = false,
        int rainTime = 0,
        bool cloudBackgroundActive = false,
        byte cloudCount = 0,
        bool pumpkinMoon = false,
        bool snowMoon = false)
    {
        if (!double.IsFinite(time) || time < 0d)
            throw new ArgumentOutOfRangeException(nameof(time));
        if (!Enum.IsDefined(moonPhase))
            throw new ArgumentOutOfRangeException(nameof(moonPhase));
        if (!double.IsFinite(slimeRainTime))
            throw new ArgumentOutOfRangeException(nameof(slimeRainTime));
        ArgumentOutOfRangeException.ThrowIfNegative(dayRate);
        if (!float.IsFinite(windSpeedCurrent))
            throw new ArgumentOutOfRangeException(nameof(windSpeedCurrent));
        if (!float.IsFinite(maxRain))
            throw new ArgumentOutOfRangeException(nameof(maxRain));
        if (windCounter < -1)
            throw new ArgumentOutOfRangeException(nameof(windCounter));
        if (extremeWindCounter < -1)
            throw new ArgumentOutOfRangeException(nameof(extremeWindCounter));
        ArgumentOutOfRangeException.ThrowIfNegative(rainTime);

        Time = time;
        DayTime = dayTime;
        MoonPhase = moonPhase;
        SlimeRainTime = slimeRainTime;
        BloodMoonActive = bloodMoonActive && !dayTime;
        PumpkinMoonActive = pumpkinMoon;
        SnowMoonActive = snowMoon;
        GetGoodWorld = getGoodWorld;
        SlimeBlueSpawnUnlocked = slimeBlueSpawnUnlocked;
        WindSpeedCurrent = windSpeedCurrent;
        WindSpeedTarget = windSpeedCurrent;
        MaxRain = maxRain;
        this.freezeWind = freezeWind;
        this.freezeRain = freezeRain;
        this.cloudBackgroundActive = cloudBackgroundActive;
        this.cloudCount = cloudCount;
        Raining = raining;
        RainTime = rainTime;
        _weatherRandom = weatherRandom ?? new SystemRuntimeWeatherRandom1458();
        _windCounterRandom = windCounterRandom ?? new SystemRuntimeWindCounterRandom1458();
        this.windCounter = windCounter;
        this.extremeWindCounter = extremeWindCounter;
        // Weather counters are transient, rather than world-file fields. A fresh runtime starts from the same
        // independently seeded intervals as Main.ResetWindCounter instead of immediately perturbing its loaded target.
        if (this.windCounter < 0)
            this.windCounter = _windCounterRandom.NextInt32(900, 2_701);
        if (this.extremeWindCounter < 0)
            this.extremeWindCounter = _windCounterRandom.NextInt32(10, 31);
        _dayRate = dayRate;
        _observer = observer;
        PublishCommittedState();
    }

    public double Time { get; private set; }

    public bool DayTime { get; private set; }

    public VanillaMoonPhase MoonPhase { get; private set; }

    public double SlimeRainTime { get; private set; }

    public bool SlimeRainActive => SlimeRainTime > 0d;

    public bool BloodMoonActive { get; private set; }

    public bool PumpkinMoonActive { get; }

    public bool SnowMoonActive { get; }

    public bool MoonEventActive => PumpkinMoonActive || SnowMoonActive;

    public bool GetGoodWorld { get; }

    public bool SlimeBlueSpawnUnlocked { get; private set; }

    /// <summary>WorldFile.LoadWorld initializes both source wind values from the persisted target.</summary>
    public float WindSpeedCurrent { get; private set; }

    /// <summary>Source <c>Main.windSpeedTarget</c>, used by the world header and packet 7.</summary>
    public float WindSpeedTarget { get; private set; }

    /// <summary>Source <c>Main.maxRaining</c>, which scales the current-wind easing target.</summary>
    public float MaxRain { get; private set; }

    /// <summary>Source <c>Main.raining</c>; packet 7 transmits its strength as zero while false.</summary>
    public bool Raining { get; private set; }

    /// <summary>Source <c>Main.rainTime</c>, in ordinary world-time units.</summary>
    public int RainTime { get; private set; }

    public float NetworkRain => Raining ? MaxRain : 0f;

    /// <summary>Applies a source-owned weather target before the per-tick current-wind easing.</summary>
    public void SetWindSpeedTarget(float target)
    {
        if (!float.IsFinite(target) || target is < -.8f or > .8f)
            throw new ArgumentOutOfRangeException(nameof(target));
        WindSpeedTarget = target;
        RequestWorldInfoSync();
    }

    /// <summary>
    /// Runtime equivalent of TerrariaServer 1.4.5.8 WorldGen.spawnMeteor. The current world clock owns the pending
    /// schedule bit; meteor materialization itself remains a separate world-event concern.
    /// </summary>
    public bool MeteorSpawnPending { get; private set; }

    bool IVanillaNpcWorldEventState.BloodMoonActive => BloodMoonActive && !GetGoodWorld;
    bool IVanillaNpcWorldEventState.GetGoodWorld => GetGoodWorld;
    bool IVanillaNpcWorldEventState.SlimeRainActive => SlimeRainActive;
    bool IVanillaNpcWorldEventState.SlimeBlueSpawnUnlocked => SlimeBlueSpawnUnlocked;

    public int DayRate => _dayRate;

    public static RuntimeWorldClock FromWorld(
        WorldFileRuntimeMetadata metadata,
        WorldCreativePowersData creativePowers,
        IRuntimeWorldClockObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(creativePowers);

        if (!VanillaMoonPhases.TryCreate(metadata.MoonPhase, out VanillaMoonPhase moonPhase))
            throw new InvalidDataException($"Unknown persisted moon phase {metadata.MoonPhase}.");

        int targetTimeRate = (int)Math.Round(1f + creativePowers.TimeRateSlider * 23f);
        int dayRate = creativePowers.FreezeTime ? 0 : targetTimeRate;
        return new RuntimeWorldClock(
            metadata.Time,
            metadata.DayTime,
            moonPhase,
            metadata.SlimeRainTime,
            dayRate,
            observer,
            metadata.BloodMoon,
            metadata.GetGoodWorld,
            metadata.UnlockedSlimeBlueSpawn,
            metadata.WindSpeed,
            metadata.MaxRain,
            creativePowers.FreezeWind,
            freezeRain: creativePowers.FreezeRain,
            raining: metadata.Raining,
            rainTime: metadata.RainTime,
            cloudBackgroundActive: metadata.CloudBackgroundActive,
            cloudCount: metadata.CloudCount);
    }

    private bool worldInfoSyncRequested;

    public void RequestWorldInfoSync() => worldInfoSyncRequested = true;

    public bool ConsumeWorldInfoSyncRequest()
    {
        bool requested = worldInfoSyncRequested;
        worldInfoSyncRequested = false;
        return requested;
    }

    public void SetDayRate(int dayRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayRate);
        _dayRate = dayRate;
        PublishCommittedState();
    }

    public void SetBloodMoonActive(bool active)
    {
        BloodMoonActive = active && !DayTime;
    }

    public void MarkSlimeBlueSpawnUnlocked() => SlimeBlueSpawnUnlocked = true;

    /// <summary>Supplies the live <c>player.active &amp;&amp; statLifeMax &gt;= 120</c> source weather predicate.</summary>
    internal void SetWeatherEligiblePlayerProvider(Func<bool>? provider) => hasWeatherEligiblePlayer = provider;

    public void ScheduleMeteor() => MeteorSpawnPending = true;

    public void ClearMeteorSchedule() => MeteorSpawnPending = false;

    public bool TryStopSlimeRain(IKingSlimeDeathRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!SlimeRainActive)
            return false;

        // Main.StopSlimeRain on server: slimeRainTime = -Main.rand.Next(3024, 6048) * 100.
        SlimeRainTime = -random.NextInt32(3024, 6048) * 100d;
        PublishCommittedState();
        return true;
    }

    public void Tick()
    {
        int dayRate = _dayRate;
        // Main.DoUpdate advances UpdateWeather once per day-rate unit. Frozen time consequently freezes both
        // easing and target selection; the persisted/networked target itself remains unscaled.
        for (int iteration = 0; iteration < dayRate; iteration++)
            TickWeatherWind();

        if (SlimeRainTime > 0d)
        {
            SlimeRainTime -= dayRate;
            if (SlimeRainTime <= 0d)
                SlimeRainTime = 0d;
        }
        else if (SlimeRainTime < 0d)
        {
            SlimeRainTime += dayRate;
            if (SlimeRainTime > 0d)
                SlimeRainTime = 0d;
        }

        TickRain(dayRate);

        Time += dayRate;

        if (!DayTime)
        {
            if (Time > NightLength)
            {
                Time = 0d;
                DayTime = true;
                BloodMoonActive = false;
                MoonPhase = VanillaMoonPhases.Next(MoonPhase);
            }
        }
        else if (Time > DayLength)
        {
            Time = 0d;
            DayTime = false;
        }

        PublishCommittedState();
    }

    private void TickWeatherWind()
    {
        float effectiveWindTarget = WindSpeedTarget * (1f + 5f / 9f * MaxRain);
        float windStep = .0003f + MathF.Abs(effectiveWindTarget - WindSpeedCurrent) * .0015f;
        if (WindSpeedCurrent < effectiveWindTarget)
            WindSpeedCurrent = MathF.Min(effectiveWindTarget, WindSpeedCurrent + windStep);
        else if (WindSpeedCurrent > effectiveWindTarget)
            WindSpeedCurrent = MathF.Max(effectiveWindTarget, WindSpeedCurrent - windStep);

        if (freezeWind)
            return;

        if (--windCounter <= 0)
        {
            bool hasEligiblePlayer = hasWeatherEligiblePlayer?.Invoke() ?? false;
            float priorDirection = WindSpeedTarget < 0f ? -1f : 1f;
            if (_weatherRandom.NextInt32(0, 4) == 0)
                WindSpeedTarget += _weatherRandom.NextInt32(-25, 26) * .001f;
            else if (_weatherRandom.NextInt32(0, 2) == 0)
                WindSpeedTarget += _weatherRandom.NextInt32(-50, 51) * .001f;
            else
                WindSpeedTarget += _weatherRandom.NextInt32(-100, 101) * .001f;

            ClampTargetForEarlyWorldPlayer(in hasEligiblePlayer);
            if (--extremeWindCounter <= 0)
            {
                ResetWindCounters(resetExtreme: true);
                if (_weatherRandom.NextInt32(0, 30) < 13)
                {
                    if (_weatherRandom.NextInt32(0, 2) == 0)
                    {
                        WindSpeedTarget = 0f;
                        windCounter = _weatherRandom.NextInt32(7_200, 28_801);
                    }
                    else
                    {
                        WindSpeedTarget = _weatherRandom.NextInt32(-200, 201) * .001f;
                    }
                }
                else if (_weatherRandom.NextInt32(0, 20) < 13)
                {
                    WindSpeedTarget = _weatherRandom.NextInt32(-400, 401) * .001f;
                }
                else
                {
                    WindSpeedTarget = _weatherRandom.NextInt32(-850, 851) * .001f;
                }

                ClampTargetForEarlyWorldPlayer(in hasEligiblePlayer);
                float magnitude = MathF.Abs(WindSpeedTarget);
                if (magnitude > .3f) extremeWindCounter += _weatherRandom.NextInt32(5, 11);
                if (magnitude > .5f) extremeWindCounter += _weatherRandom.NextInt32(10, 21);
                if (magnitude > .7f) extremeWindCounter += _weatherRandom.NextInt32(15, 31);
            }
            else
            {
                ResetWindCounters(resetExtreme: false);
            }

            if (_weatherRandom.NextInt32(0, 3) != 0 &&
                ((priorDirection < 0f && WindSpeedTarget > 0f) || (priorDirection > 0f && WindSpeedTarget < 0f)))
            {
                WindSpeedTarget *= -1f;
            }
        }

        WindSpeedTarget = Math.Clamp(WindSpeedTarget, -.8f, .8f);
    }

    private void ClampTargetForEarlyWorldPlayer(in bool hasEligiblePlayer)
    {
        if (!hasEligiblePlayer && MathF.Abs(WindSpeedTarget) > .35f)
            WindSpeedTarget = .35f * MathF.Sign(WindSpeedTarget);
    }

    private void ResetWindCounters(bool resetExtreme)
    {
        windCounter = _windCounterRandom.NextInt32(900, 2_701);
        if (resetExtreme)
            extremeWindCounter = _windCounterRandom.NextInt32(10, 31);
    }

    private void TickRain(int dayRate)
    {
        if (freezeRain)
            return;

        float priorMaxRain = MaxRain;
        if (Raining)
        {
            RainTime = Math.Max(0, RainTime - dayRate);
            if (RainTime == 0)
                StopRain();
            else if (dayRate > 0 && _weatherRandom.NextInt32(0, 2 * (86_400 / dayRate / 24)) == 0)
                ChangeRain();
        }
        else if (!SlimeRainActive && dayRate > 0 && (hasWeatherEligiblePlayer?.Invoke() ?? false))
        {
            int normalizedDayLength = 86_400 / dayRate;
            if (_weatherRandom.NextInt32(0, checked((int)(normalizedDayLength * 5.75d))) == 0 ||
                (cloudBackgroundActive && _weatherRandom.NextInt32(0, checked((int)(normalizedDayLength * 4.25d))) == 0))
            {
                StartRain();
            }
        }

        if (MaxRain != priorMaxRain)
            RequestWorldInfoSync();
    }

    private void StartRain()
    {
        int dayLength = 86_400;
        int hourLength = dayLength / 24;
        int duration = _weatherRandom.NextInt32(hourLength * 8, dayLength);
        if (_weatherRandom.NextInt32(0, 3) == 0)
            duration += _weatherRandom.NextInt32(0, hourLength);
        if (_weatherRandom.NextInt32(0, 4) == 0)
            duration += _weatherRandom.NextInt32(0, hourLength * 2);
        if (_weatherRandom.NextInt32(0, 5) == 0)
            duration += _weatherRandom.NextInt32(0, hourLength * 2);
        if (_weatherRandom.NextInt32(0, 6) == 0)
            duration += _weatherRandom.NextInt32(0, hourLength * 3);
        if (_weatherRandom.NextInt32(0, 7) == 0)
            duration += _weatherRandom.NextInt32(0, hourLength * 4);
        if (_weatherRandom.NextInt32(0, 8) == 0)
            duration += _weatherRandom.NextInt32(0, hourLength * 5);

        float multiplier = 1f;
        if (_weatherRandom.NextInt32(0, 2) == 0)
            multiplier += .05f;
        if (_weatherRandom.NextInt32(0, 3) == 0)
            multiplier += .1f;
        if (_weatherRandom.NextInt32(0, 4) == 0)
            multiplier += .15f;
        if (_weatherRandom.NextInt32(0, 5) == 0)
            multiplier += .2f;

        RainTime = checked((int)(duration * multiplier));
        ChangeRain();
        Raining = true;
    }

    private void StopRain()
    {
        RainTime = 0;
        Raining = false;
        MaxRain = 0f;
    }

    private void ChangeRain()
    {
        if (cloudBackgroundActive || cloudCount > 150)
        {
            MaxRain = (_weatherRandom.NextInt32(0, 3) != 0
                ? _weatherRandom.NextInt32(40, 91)
                : _weatherRandom.NextInt32(20, 91)) * .01f;
        }
        else if (cloudCount > 100)
        {
            MaxRain = (_weatherRandom.NextInt32(0, 3) != 0
                ? _weatherRandom.NextInt32(20, 61)
                : _weatherRandom.NextInt32(10, 71)) * .01f;
        }
        else
        {
            MaxRain = (_weatherRandom.NextInt32(0, 3) != 0
                ? _weatherRandom.NextInt32(5, 31)
                : _weatherRandom.NextInt32(5, 41)) * .01f;
        }
    }

    private void PublishCommittedState() =>
        _observer?.WorldClockCommitted(
            Time,
            DayTime,
            MoonPhase,
            SlimeRainTime,
            _dayRate);
}
