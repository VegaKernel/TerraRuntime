namespace TerraRuntime;

/// <summary>
/// Server-scoped fixed-window ceiling for chat fan-out operations. The budget limits the O(players)
/// broadcast step itself, so many individually legal senders cannot multiply a per-connection allowance
/// into unbounded aggregate queue work.
/// </summary>
internal sealed class RuntimeChatFanoutBudget
{
    public const int DefaultMaxBroadcastsPerWindow = 256;
    public static TimeSpan DefaultWindow { get; } = TimeSpan.FromSeconds(1);

    private readonly object gate = new();
    private readonly int maxBroadcasts;
    private readonly TimeSpan window;
    private readonly TimeProvider timeProvider;
    private long windowStarted;
    private int broadcastsInWindow;
    private long acceptedBroadcasts;
    private long rejectedBroadcasts;

    public RuntimeChatFanoutBudget(
        int maxBroadcasts = DefaultMaxBroadcastsPerWindow,
        TimeSpan? window = null,
        TimeProvider? timeProvider = null)
    {
        if (maxBroadcasts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBroadcasts));

        TimeSpan effectiveWindow = window ?? DefaultWindow;
        if (effectiveWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        this.maxBroadcasts = maxBroadcasts;
        this.window = effectiveWindow;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        windowStarted = this.timeProvider.GetTimestamp();
    }

    public bool TryConsume()
    {
        lock (gate)
        {
            long now = timeProvider.GetTimestamp();
            if (timeProvider.GetElapsedTime(windowStarted, now) >= window)
            {
                windowStarted = now;
                broadcastsInWindow = 0;
            }

            if (broadcastsInWindow >= maxBroadcasts)
            {
                rejectedBroadcasts++;
                return false;
            }

            broadcastsInWindow++;
            acceptedBroadcasts++;
            return true;
        }
    }

    public RuntimeChatFanoutBudgetSnapshot CaptureSnapshot()
    {
        lock (gate)
        {
            return new RuntimeChatFanoutBudgetSnapshot(
                MaxBroadcastsPerWindow: maxBroadcasts,
                Window: window,
                BroadcastsInCurrentWindow: broadcastsInWindow,
                AcceptedBroadcasts: acceptedBroadcasts,
                RejectedBroadcasts: rejectedBroadcasts);
        }
    }
}

internal readonly record struct RuntimeChatFanoutBudgetSnapshot(
    int MaxBroadcastsPerWindow,
    TimeSpan Window,
    int BroadcastsInCurrentWindow,
    long AcceptedBroadcasts,
    long RejectedBroadcasts);
