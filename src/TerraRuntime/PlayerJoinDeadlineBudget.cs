using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Bounds how long an accepted client may retain a player slot without reaching the authoritative
/// Playing state. This is an abuse ceiling, not a vanilla gameplay timing rule. The clock starts only
/// after a join session exists, so the network handshake deadline remains the owner of pre-Hello stalls.
/// </summary>
internal sealed class PlayerJoinDeadlineBudget
{
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromMinutes(2);

    private readonly TimeSpan timeout;
    private readonly TimeProvider timeProvider;
    private long startedTimestamp;
    private bool started;

    public PlayerJoinDeadlineBudget(
        TimeSpan timeout,
        TimeProvider? timeProvider = null)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        this.timeout = timeout;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public TimeSpan GetRemaining(PlayerJoinState? state)
    {
        if (state is null or PlayerJoinState.Playing or PlayerJoinState.Closed)
            return Timeout.InfiniteTimeSpan;

        long now = timeProvider.GetTimestamp();
        if (!started)
        {
            startedTimestamp = now;
            started = true;
            return timeout;
        }

        TimeSpan elapsed = timeProvider.GetElapsedTime(startedTimestamp, now);
        TimeSpan remaining = timeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool IsExpired(PlayerJoinState? state) => GetRemaining(state) == TimeSpan.Zero;
}
