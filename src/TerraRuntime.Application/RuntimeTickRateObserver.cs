using System.Diagnostics;

namespace TerraRuntime;

/// <summary>Maintains a low-cost rolling TPS observation for one authoritative game loop.</summary>
internal sealed class RuntimeTickRateObserver
{
    private static readonly long MinimumSampleTicks = Math.Max(1L, Stopwatch.Frequency / 4);
    private readonly object gate = new();
    private long sampleTick = -1;
    private long sampleTimestamp;
    private double observedTicksPerSecond;

    public double Observe(long currentTick)
    {
        long now = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (sampleTick < 0)
            {
                sampleTick = currentTick;
                sampleTimestamp = now;
                return 0d;
            }

            long elapsed = now - sampleTimestamp;
            if (elapsed < MinimumSampleTicks)
                return observedTicksPerSecond;

            long completedTicks = currentTick - sampleTick;
            if (completedTicks >= 0)
                observedTicksPerSecond = completedTicks * (double)Stopwatch.Frequency / elapsed;

            sampleTick = currentTick;
            sampleTimestamp = now;
            return observedTicksPerSecond;
        }
    }
}
