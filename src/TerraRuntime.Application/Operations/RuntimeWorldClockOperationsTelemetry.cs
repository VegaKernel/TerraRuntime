using TerraRuntime.World;

namespace TerraRuntime.Operations;

internal readonly record struct RuntimeWorldClockTelemetrySnapshot(
    bool Available,
    double Time,
    bool DayTime,
    VanillaMoonPhase MoonPhase,
    double SlimeRainTime,
    int DayRate);

/// <summary>
/// TUI-facing projection of the authoritative world clock. The game loop is the single writer; readers
/// use a sequence check so the UI never needs the mutable RuntimeWorldClock instance or a simulation lock.
/// </summary>
internal sealed class RuntimeWorldClockOperationsTelemetry : IRuntimeWorldClockObserver
{
    private long sequence;
    private double time;
    private bool dayTime;
    private VanillaMoonPhase moonPhase;
    private double slimeRainTime;
    private int dayRate;

    public void WorldClockCommitted(
        double time,
        bool dayTime,
        VanillaMoonPhase moonPhase,
        double slimeRainTime,
        int dayRate)
    {
        Interlocked.Increment(ref sequence);
        this.time = time;
        this.dayTime = dayTime;
        this.moonPhase = moonPhase;
        this.slimeRainTime = slimeRainTime;
        this.dayRate = dayRate;
        Interlocked.Increment(ref sequence);
    }

    public RuntimeWorldClockTelemetrySnapshot CaptureSnapshot()
    {
        while (true)
        {
            long before = Volatile.Read(ref sequence);
            if ((before & 1L) != 0)
                continue;

            double capturedTime = time;
            bool capturedDayTime = dayTime;
            VanillaMoonPhase capturedMoonPhase = moonPhase;
            double capturedSlimeRainTime = slimeRainTime;
            int capturedDayRate = dayRate;
            Thread.MemoryBarrier();

            long after = Volatile.Read(ref sequence);
            if (before != after || (after & 1L) != 0)
                continue;

            return new RuntimeWorldClockTelemetrySnapshot(
                Available: after != 0,
                Time: capturedTime,
                DayTime: capturedDayTime,
                MoonPhase: capturedMoonPhase,
                SlimeRainTime: capturedSlimeRainTime,
                DayRate: capturedDayRate);
        }
    }
}
