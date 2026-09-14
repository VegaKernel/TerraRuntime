using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Network;

/// <summary>Single-writer counters for one connection using a shared live policy.</summary>
public sealed class SessionPacketRateBudget
{
    private readonly IPacketRateLimitControl control;
    private readonly TimeProvider timeProvider;
    private readonly long[] counts = new long[byte.MaxValue + 1];
    private long windowStart;

    public SessionPacketRateBudget(IPacketRateLimitControl control, TimeProvider? timeProvider = null)
    {
        this.control = control ?? throw new ArgumentNullException(nameof(control));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        windowStart = this.timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Consumes one frame if allowed; otherwise returns the remaining fixed one-second window.
    /// Repeated attempts and policy changes never refill the current window.
    /// </summary>
    public bool TryAcquire(byte messageId, out TimeSpan retryAfter)
    {
        long now = timeProvider.GetTimestamp();
        TimeSpan elapsed = timeProvider.GetElapsedTime(windowStart, now);
        if (elapsed >= TimeSpan.FromSeconds(1))
        {
            Array.Clear(counts);
            windowStart = now;
            elapsed = TimeSpan.Zero;
        }

        int? limit = control.GetLimit(messageId);
        if (limit is int maximum && counts[messageId] >= maximum)
        {
            retryAfter = TimeSpan.FromSeconds(1) - elapsed;
            return false;
        }

        // Count unrestricted traffic too, so installing a limit cannot grant a fresh burst mid-window.
        if (counts[messageId] < long.MaxValue)
            counts[messageId]++;
        retryAfter = TimeSpan.Zero;
        return true;
    }
}
