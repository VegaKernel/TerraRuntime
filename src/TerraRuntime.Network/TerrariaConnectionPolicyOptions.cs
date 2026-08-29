namespace TerraRuntime.Network;

public readonly record struct TerrariaConnectionPolicyOptions
{
    /// <summary>
    /// Conservative abuse ceiling for a connection that completed Hello but never reaches the runtime's
    /// ready/playing state. This is deliberately not a gameplay cadence rule.
    /// </summary>
    public static TimeSpan DefaultJoinTimeout { get; } = TimeSpan.FromMinutes(2);

    public static TerrariaConnectionPolicyOptions Default { get; } = new(
        handshakeTimeout: TimeSpan.FromSeconds(10),
        idleTimeout: Timeout.InfiniteTimeSpan,
        rateBudget: ConnectionRateBudgetOptions.HardAbuse,
        messageRateLimits: ConnectionMessageRateLimits.HardAbuse,
        joinTimeout: DefaultJoinTimeout);

    public TerrariaConnectionPolicyOptions(TimeSpan handshakeTimeout, TimeSpan idleTimeout)
        : this(
            handshakeTimeout,
            idleTimeout,
            ConnectionRateBudgetOptions.AccountingOnly,
            ConnectionMessageRateLimits.None,
            DefaultJoinTimeout)
    {
    }

    public TerrariaConnectionPolicyOptions(
        TimeSpan handshakeTimeout,
        TimeSpan idleTimeout,
        ConnectionRateBudgetOptions rateBudget)
        : this(
            handshakeTimeout,
            idleTimeout,
            rateBudget,
            ConnectionMessageRateLimits.None,
            DefaultJoinTimeout)
    {
    }

    public TerrariaConnectionPolicyOptions(
        TimeSpan handshakeTimeout,
        TimeSpan idleTimeout,
        ConnectionRateBudgetOptions rateBudget,
        ConnectionMessageRateLimits messageRateLimits)
        : this(
            handshakeTimeout,
            idleTimeout,
            rateBudget,
            messageRateLimits,
            DefaultJoinTimeout)
    {
    }

    public TerrariaConnectionPolicyOptions(
        TimeSpan handshakeTimeout,
        TimeSpan idleTimeout,
        ConnectionRateBudgetOptions rateBudget,
        ConnectionMessageRateLimits messageRateLimits,
        TimeSpan joinTimeout)
    {
        if (handshakeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));

        if (idleTimeout != Timeout.InfiniteTimeSpan && idleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));

        if (joinTimeout != Timeout.InfiniteTimeSpan && joinTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(joinTimeout));

        ArgumentNullException.ThrowIfNull(messageRateLimits);
        HandshakeTimeout = handshakeTimeout;
        IdleTimeout = idleTimeout;
        RateBudget = rateBudget;
        MessageRateLimits = messageRateLimits;
        JoinTimeout = joinTimeout;
    }

    public TimeSpan HandshakeTimeout { get; }

    public TimeSpan IdleTimeout { get; }

    public ConnectionRateBudgetOptions RateBudget { get; }

    public ConnectionMessageRateLimits MessageRateLimits { get; }

    public TimeSpan JoinTimeout { get; }
}
