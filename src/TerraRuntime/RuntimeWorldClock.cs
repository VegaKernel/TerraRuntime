using TerraRuntime.World;

namespace TerraRuntime;

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
    private int _dayRate;

    public RuntimeWorldClock(
        double time,
        bool dayTime,
        VanillaMoonPhase moonPhase,
        double slimeRainTime,
        int dayRate,
        IRuntimeWorldClockObserver? observer = null,
        bool bloodMoonActive = false,
        bool getGoodWorld = false,
        bool slimeBlueSpawnUnlocked = false)
    {
        if (!double.IsFinite(time) || time < 0d)
            throw new ArgumentOutOfRangeException(nameof(time));
        if (!Enum.IsDefined(moonPhase))
            throw new ArgumentOutOfRangeException(nameof(moonPhase));
        if (!double.IsFinite(slimeRainTime))
            throw new ArgumentOutOfRangeException(nameof(slimeRainTime));
        ArgumentOutOfRangeException.ThrowIfNegative(dayRate);

        Time = time;
        DayTime = dayTime;
        MoonPhase = moonPhase;
        SlimeRainTime = slimeRainTime;
        BloodMoonActive = bloodMoonActive && !dayTime;
        GetGoodWorld = getGoodWorld;
        SlimeBlueSpawnUnlocked = slimeBlueSpawnUnlocked;
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

    public bool GetGoodWorld { get; }

    public bool SlimeBlueSpawnUnlocked { get; private set; }

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
            metadata.UnlockedSlimeBlueSpawn);
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

    internal void MarkSlimeBlueSpawnUnlocked() => SlimeBlueSpawnUnlocked = true;

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

    private void PublishCommittedState() =>
        _observer?.WorldClockCommitted(
            Time,
            DayTime,
            MoonPhase,
            SlimeRainTime,
            _dayRate);
}
