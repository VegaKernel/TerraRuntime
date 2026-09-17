using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Network;

public readonly record struct TerrariaConnectionPolicyOptions
{
    /// <summary>
    /// Conservative abuse ceiling for a connection that completed Hello but never reaches the runtime's
    /// ready/playing state. This is deliberately not a gameplay cadence rule.
    /// </summary>
    public static TimeSpan DefaultJoinTimeout { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The source-backed inactivity ceiling for an established connection. TerrariaServer 1.4.5.8 increments
    /// <c>Netplay.Clients[i].TimeOutTimer</c> once per server update and terminates the client past
    /// <c>7200</c>, which is <c>$120\,\mathrm{s}$</c> at the dedicated server's 60 updates per second; any
    /// received message resets the counter in <c>MessageBuffer.GetData</c>. A playing vanilla client sends
    /// player controls several times per second, so two minutes of complete inbound silence means the peer is
    /// gone - and until it is released it still holds its player slot and its reserved player name, which is
    /// what refuses the same player's rejoin.
    /// </summary>
    public static TimeSpan DefaultIdleTimeout { get; } = TimeSpan.FromMinutes(2);

    public static TerrariaConnectionPolicyOptions Default { get; } = new(
        handshakeTimeout: TimeSpan.FromSeconds(10),
        idleTimeout: DefaultIdleTimeout,
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

    /// <summary>Shared live inbound policy; each connection owns independent counters.</summary>
    public IPacketRateLimitControl? PacketRateLimits { get; init; }
}
