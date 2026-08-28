using System.Collections.Concurrent;
using TerraRuntime.Network;

namespace TerraRuntime.Operations;

/// <summary>
/// Samples the network layer's existing per-connection inbound rate accountants. No packet-path
/// counters are duplicated here; the connection read path remains the sole writer.
/// </summary>
internal sealed class RuntimeConnectionRateTelemetry
{
    private readonly ConcurrentDictionary<long, TerrariaConnectionRateAccountant> accountants = new();

    public bool TryRegister(long connectionId, TerrariaConnectionRateAccountant accountant)
    {
        ArgumentNullException.ThrowIfNull(accountant);
        return connectionId > 0 && accountants.TryAdd(connectionId, accountant);
    }

    public bool TryUnregister(long connectionId) => accountants.TryRemove(connectionId, out _);

    public RuntimeConnectionRateTelemetrySnapshot CaptureSnapshot(int maximumDetails)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDetails);

        int trackedConnections = 0;
        long windowFrames = 0;
        long windowBytes = 0;
        long totalFrames = 0;
        long totalBytes = 0;
        long rejectedFrames = 0;
        RuntimeConnectionRateDetail[] top = maximumDetails == 0
            ? []
            : new RuntimeConnectionRateDetail[maximumDetails];
        int topCount = 0;

        foreach ((long connectionId, TerrariaConnectionRateAccountant accountant) in accountants)
        {
            int currentFrames = accountant.CurrentWindowFrames;
            long currentBytes = accountant.CurrentWindowBytes;
            ConnectionRateSnapshot lifetime = accountant.Snapshot;

            trackedConnections++;
            windowFrames += currentFrames;
            windowBytes += currentBytes;
            totalFrames += lifetime.TotalFrames;
            totalBytes += lifetime.TotalBytes;
            rejectedFrames += lifetime.RejectedFrames;

            if (maximumDetails == 0 ||
                (currentFrames == 0 && currentBytes == 0 && lifetime.RejectedFrames == 0))
            {
                continue;
            }

            var detail = new RuntimeConnectionRateDetail(
                ConnectionId: connectionId,
                WindowFrames: currentFrames,
                WindowBytes: currentBytes,
                TotalFrames: lifetime.TotalFrames,
                TotalBytes: lifetime.TotalBytes,
                RejectedFrames: lifetime.RejectedFrames);
            InsertTop(top, ref topCount, detail);
        }

        ReadOnlyMemory<RuntimeConnectionRateDetail> details = topCount == 0
            ? ReadOnlyMemory<RuntimeConnectionRateDetail>.Empty
            : top.AsMemory(0, topCount);
        return new RuntimeConnectionRateTelemetrySnapshot(
            TrackedConnections: trackedConnections,
            WindowFrames: windowFrames,
            WindowBytes: windowBytes,
            TotalFrames: totalFrames,
            TotalBytes: totalBytes,
            RejectedFrames: rejectedFrames,
            TopConnections: details);
    }

    private static void InsertTop(
        RuntimeConnectionRateDetail[] top,
        ref int count,
        RuntimeConnectionRateDetail candidate)
    {
        int insertAt = count;
        for (int i = 0; i < count; i++)
        {
            if (Compare(candidate, top[i]) < 0)
            {
                insertAt = i;
                break;
            }
        }

        if (insertAt >= top.Length)
            return;

        int last = Math.Min(count, top.Length - 1);
        for (int i = last; i > insertAt; i--)
            top[i] = top[i - 1];

        top[insertAt] = candidate;
        if (count < top.Length)
            count++;
    }

    private static int Compare(RuntimeConnectionRateDetail left, RuntimeConnectionRateDetail right)
    {
        int bytes = right.WindowBytes.CompareTo(left.WindowBytes);
        if (bytes != 0)
            return bytes;

        int frames = right.WindowFrames.CompareTo(left.WindowFrames);
        if (frames != 0)
            return frames;

        int rejected = right.RejectedFrames.CompareTo(left.RejectedFrames);
        return rejected != 0 ? rejected : left.ConnectionId.CompareTo(right.ConnectionId);
    }
}

internal readonly record struct RuntimeConnectionRateTelemetrySnapshot(
    int TrackedConnections,
    long WindowFrames,
    long WindowBytes,
    long TotalFrames,
    long TotalBytes,
    long RejectedFrames,
    ReadOnlyMemory<RuntimeConnectionRateDetail> TopConnections);

internal readonly record struct RuntimeConnectionRateDetail(
    long ConnectionId,
    int WindowFrames,
    long WindowBytes,
    long TotalFrames,
    long TotalBytes,
    long RejectedFrames);
