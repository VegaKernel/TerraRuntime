namespace TerraRuntime.Network;

public readonly record struct TerrariaConnectionPolicyOptions
{
    public static TerrariaConnectionPolicyOptions Default { get; } = new(
        handshakeTimeout: TimeSpan.FromSeconds(10),
        idleTimeout: Timeout.InfiniteTimeSpan,
        rateBudget: ConnectionRateBudgetOptions.AccountingOnly,
        messageRateLimits: ConnectionMessageRateLimits.HardAbuse);

    public TerrariaConnectionPolicyOptions(TimeSpan handshakeTimeout, TimeSpan idleTimeout)
        : this(
            handshakeTimeout,
            idleTimeout,
            ConnectionRateBudgetOptions.AccountingOnly,
            ConnectionMessageRateLimits.None)
    {
    }

    public TerrariaConnectionPolicyOptions(
        TimeSpan handshakeTimeout,
        TimeSpan idleTimeout,
        ConnectionRateBudgetOptions rateBudget)
        : this(handshakeTimeout, idleTimeout, rateBudget, ConnectionMessageRateLimits.None)
    {
    }

    public TerrariaConnectionPolicyOptions(
        TimeSpan handshakeTimeout,
        TimeSpan idleTimeout,
        ConnectionRateBudgetOptions rateBudget,
        ConnectionMessageRateLimits messageRateLimits)
    {
        if (handshakeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));
        }

        if (idleTimeout != Timeout.InfiniteTimeSpan && idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        }

        ArgumentNullException.ThrowIfNull(messageRateLimits);
        HandshakeTimeout = handshakeTimeout;
        IdleTimeout = idleTimeout;
        RateBudget = rateBudget;
        MessageRateLimits = messageRateLimits;
    }

    public TimeSpan HandshakeTimeout { get; }

    public TimeSpan IdleTimeout { get; }

    public ConnectionRateBudgetOptions RateBudget { get; }

    public ConnectionMessageRateLimits MessageRateLimits { get; }
}
