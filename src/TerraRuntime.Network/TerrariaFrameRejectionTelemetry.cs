namespace TerraRuntime.Network;

/// <summary>
/// Stable, packet-library-neutral rejection categories exposed by the connection boundary.
/// Subsystem-specific stop enums remain implementation details of their owning sinks.
/// </summary>
public enum TerrariaFrameRejectionCategory : byte
{
    None = 0,
    MalformedProtocol = 1,
    RateLimited = 2,
    InvalidState = 3,
    GameplayRejected = 4,
    Backpressure = 5
}

/// <summary>
/// Optional contract for frame sinks that can explain a terminal <see cref="TerrariaFrameSinkResult.Stop"/>.
/// Wrapping sinks should forward the inner category when they did not reject the frame themselves.
/// </summary>
public interface ITerrariaFrameRejectionSource
{
    TerrariaFrameRejectionCategory RejectionCategory { get; }
}

/// <summary>
/// Process-lifetime bounded counters for normalized connection/frame rejection causes.
/// Recording is allocation-free and snapshots are lock-free reads of fixed counters.
/// </summary>
public static class TerrariaFrameRejectionTelemetry
{
    private static long malformedProtocol;
    private static long rateLimited;
    private static long invalidState;
    private static long gameplayRejected;
    private static long backpressure;

    public static TerrariaFrameRejectionTelemetrySnapshot CaptureSnapshot() => new(
        MalformedProtocol: Interlocked.Read(ref malformedProtocol),
        RateLimited: Interlocked.Read(ref rateLimited),
        InvalidState: Interlocked.Read(ref invalidState),
        GameplayRejected: Interlocked.Read(ref gameplayRejected),
        Backpressure: Interlocked.Read(ref backpressure));

    internal static void Record(TerrariaFrameRejectionCategory category)
    {
        switch (category)
        {
            case TerrariaFrameRejectionCategory.None:
                return;
            case TerrariaFrameRejectionCategory.MalformedProtocol:
                Interlocked.Increment(ref malformedProtocol);
                return;
            case TerrariaFrameRejectionCategory.RateLimited:
                Interlocked.Increment(ref rateLimited);
                return;
            case TerrariaFrameRejectionCategory.InvalidState:
                Interlocked.Increment(ref invalidState);
                return;
            case TerrariaFrameRejectionCategory.GameplayRejected:
                Interlocked.Increment(ref gameplayRejected);
                return;
            case TerrariaFrameRejectionCategory.Backpressure:
                Interlocked.Increment(ref backpressure);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(category), category, null);
        }
    }
}

public readonly record struct TerrariaFrameRejectionTelemetrySnapshot(
    long MalformedProtocol,
    long RateLimited,
    long InvalidState,
    long GameplayRejected,
    long Backpressure);
