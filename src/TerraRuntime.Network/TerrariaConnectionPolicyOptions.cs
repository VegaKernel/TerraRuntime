namespace TerraRuntime.Network;

public readonly record struct TerrariaConnectionPolicyOptions
{
    public static TerrariaConnectionPolicyOptions Default { get; } = new(
        handshakeTimeout: TimeSpan.FromSeconds(10),
        idleTimeout: TimeSpan.FromSeconds(60),
        rateBudget: ConnectionRateBudgetOptions.AccountingOnly);

    public TerrariaConnectionPolicyOptions(TimeSpan handshakeTimeout, TimeSpan idleTimeout)
        : this(handshakeTimeout, idleTimeout, ConnectionRateBudgetOptions.AccountingOnly)
    {
    }

    public TerrariaConnectionPolicyOptions(
        TimeSpan handshakeTimeout,
        TimeSpan idleTimeout,
        ConnectionRateBudgetOptions rateBudget)
    {
        if (handshakeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));
        }

        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        }

        HandshakeTimeout = handshakeTimeout;
        IdleTimeout = idleTimeout;
        RateBudget = rateBudget;
    }

    public TimeSpan HandshakeTimeout { get; }

    public TimeSpan IdleTimeout { get; }

    public ConnectionRateBudgetOptions RateBudget { get; }
}
