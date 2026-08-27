namespace TerraRuntime.Network;

public readonly record struct TerrariaConnectionPolicyOptions
{
    public static TerrariaConnectionPolicyOptions Default { get; } = new(
        handshakeTimeout: TimeSpan.FromSeconds(10),
        idleTimeout: TimeSpan.FromSeconds(60));

    public TerrariaConnectionPolicyOptions(TimeSpan handshakeTimeout, TimeSpan idleTimeout)
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
    }

    public TimeSpan HandshakeTimeout { get; }

    public TimeSpan IdleTimeout { get; }
}
