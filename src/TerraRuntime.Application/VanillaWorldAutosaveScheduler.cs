using System.Diagnostics;

namespace TerraRuntime;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 dedicated-server autosave cadence.
/// Main.DoUpdate_AutoSave starts a Stopwatch while the active server is running and, for netMode == 2,
/// resets it and saves the world only after ElapsedMilliseconds is strictly greater than 600000.
/// Reset leaves the Stopwatch stopped; the following update starts the next interval.
/// </summary>
internal sealed class VanillaWorldAutosaveScheduler
{
    public const long DedicatedServerIntervalMilliseconds = 600_000;

    private readonly Func<long> getTimestamp;
    private readonly long timestampFrequency;
    private long startedTimestamp;
    private bool running;

    public VanillaWorldAutosaveScheduler()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal VanillaWorldAutosaveScheduler(Func<long> getTimestamp, long timestampFrequency)
    {
        ArgumentNullException.ThrowIfNull(getTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(timestampFrequency, 1);
        this.getTimestamp = getTimestamp;
        this.timestampFrequency = timestampFrequency;
    }

    public bool IsRunning => running;

    /// <summary>
    /// Advances the dedicated-server autosave timer. Returns true exactly when the vanilla interval has elapsed
    /// and a world save should be requested. After firing, the timer is stopped until the next owner update.
    /// </summary>
    public bool Tick()
    {
        long now = getTimestamp();
        if (!running)
        {
            startedTimestamp = now;
            running = true;
            return false;
        }

        long elapsedTicks = now - startedTimestamp;
        if (elapsedTicks < 0)
        {
            // Stopwatch.GetTimestamp is monotonic. Treat a custom/test provider regression as a fresh interval
            // rather than manufacturing an immediate save.
            startedTimestamp = now;
            return false;
        }

        long elapsedMilliseconds = elapsedTicks > long.MaxValue / 1000
            ? long.MaxValue
            : elapsedTicks * 1000 / timestampFrequency;
        if (elapsedMilliseconds <= DedicatedServerIntervalMilliseconds)
            return false;

        running = false;
        startedTimestamp = 0;
        return true;
    }
}
