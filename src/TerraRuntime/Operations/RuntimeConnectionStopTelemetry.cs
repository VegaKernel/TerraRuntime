using TerraRuntime.Network;

namespace TerraRuntime.Operations;

/// <summary>
/// Retains bounded lifetime counters for normalized connection stop causes. Recording happens once
/// after the socket loop exits, never on the packet hot path.
/// </summary>
internal sealed class RuntimeConnectionStopTelemetry
{
    private readonly long[] counts = new long[byte.MaxValue + 1];

    public void Record(TerrariaConnectionStopReason reason)
    {
        if (reason == TerrariaConnectionStopReason.None)
            return;

        Interlocked.Increment(ref counts[(byte)reason]);
    }

    public long GetCount(TerrariaConnectionStopReason reason) =>
        Interlocked.Read(ref counts[(byte)reason]);

    public RuntimeConnectionStopTelemetrySnapshot CaptureSnapshot() => new(
        ProtocolFailures: GetCount(TerrariaConnectionStopReason.ProtocolFailure),
        RateLimited: GetCount(TerrariaConnectionStopReason.RateLimited),
        InvalidHandshake: GetCount(TerrariaConnectionStopReason.InvalidHandshake),
        UnsupportedProtocol: GetCount(TerrariaConnectionStopReason.UnsupportedProtocol),
        SlowClient: GetCount(TerrariaConnectionStopReason.SlowClient),
        ApplicationStopped: GetCount(TerrariaConnectionStopReason.ApplicationStopped),
        HandshakeTimeout: GetCount(TerrariaConnectionStopReason.HandshakeTimeout),
        IdleTimeout: GetCount(TerrariaConnectionStopReason.IdleTimeout));
}

internal readonly record struct RuntimeConnectionStopTelemetrySnapshot(
    long ProtocolFailures,
    long RateLimited,
    long InvalidHandshake,
    long UnsupportedProtocol,
    long SlowClient,
    long ApplicationStopped,
    long HandshakeTimeout,
    long IdleTimeout);
