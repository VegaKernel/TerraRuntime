using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Authoritative ordinary-world time slice backed by TerrariaServer 1.4.5.8 Main.UpdateTime.
/// NPCs consume the current state before this clock advances each game tick, matching vanilla's
/// DoUpdateInWorld ordering where UpdateWorld_NPCs runs before UpdateWorld_Time.
/// Event start/stop side effects, sleeping-player acceleration and fast-forward controls are separate concerns.
/// </summary>
internal sealed class RuntimeWorldClock
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
        IRuntimeWorldClockObserver? observer = null)
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
        _dayRate = dayRate;
        _observer = observer;
        PublishCommittedState();
    }

    public double Time { get; private set; }

    public bool DayTime { get; private set; }

    public VanillaMoonPhase MoonPhase { get; private set; }

    public double SlimeRainTime { get; private set; }

    public bool SlimeRainActive => SlimeRainTime > 0d;

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
            observer);
    }

    public void SetDayRate(int dayRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayRate);
        _dayRate = dayRate;
        PublishCommittedState();
    }

    public void Tick()
    {
        int dayRate = _dayRate;

        // Main.UpdateTime updates the active/cooldown slime-rain counter with the current dayRate
        // before calling UpdateTimeRate and advancing Main.time.
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
