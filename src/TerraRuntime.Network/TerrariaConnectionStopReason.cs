namespace TerraRuntime.Network;

public enum TerrariaConnectionStopReason : byte
{
    None = 0,
    PeerClosed = 1,
    ApplicationStopped = 2,
    Cancelled = 3,
    HandshakeTimeout = 4,
    IdleTimeout = 5,
    InvalidHandshake = 6,
    UnsupportedProtocol = 7,
    ProtocolFailure = 8,
    InboundIoFailure = 9,
    OutboundFailure = 10,
    SlowClient = 11,
    RateLimited = 12,
    JoinTimeout = 13,
    FrameRejected = 14
}
