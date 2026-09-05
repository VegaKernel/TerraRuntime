using System.Collections.Concurrent;
using TerraRuntime.Network;

namespace TerraRuntime.Application.Operations;

internal sealed class RuntimeConnectionQueueTelemetry
{
    private const int MaximumDetailCount = 64;
    private readonly ConcurrentDictionary<long, TerrariaConnectionOutboundQueue> queues = new();
    private long lifetimePeakQueuedFrames;
    private long lifetimePeakQueuedBytes;
    private long lifetimeConfiguredMaxFrames;
    private long lifetimeConfiguredMaxQueuedBytes;

    public bool TryRegister(long connectionId, TerrariaConnectionOutboundQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (connectionId <= 0 || !queues.TryAdd(connectionId, queue))
            return false;

        UpdateMaximum(ref lifetimeConfiguredMaxFrames, queue.MaxFrames);
        UpdateMaximum(ref lifetimeConfiguredMaxQueuedBytes, queue.MaxQueuedBytes);
        return true;
    }

    public bool TryUnregister(long connectionId)
    {
        if (!queues.TryRemove(connectionId, out TerrariaConnectionOutboundQueue? queue))
            return false;

        UpdateMaximum(ref lifetimePeakQueuedFrames, queue.PeakQueuedFrames);
        UpdateMaximum(ref lifetimePeakQueuedBytes, queue.PeakQueuedBytes);
        UpdateMaximum(ref lifetimeConfiguredMaxFrames, queue.MaxFrames);
        UpdateMaximum(ref lifetimeConfiguredMaxQueuedBytes, queue.MaxQueuedBytes);
        return true;
    }

    public RuntimeConnectionQueueSnapshot CaptureSnapshot(int maxDetails = 0)
    {
        if (maxDetails < 0 || maxDetails > MaximumDetailCount)
            throw new ArgumentOutOfRangeException(nameof(maxDetails));

        int trackedQueues = 0;
        int slowClients = 0;
        long queuedFrames = 0;
        long queuedBytes = 0;
        long peakQueuedFrames = Interlocked.Read(ref lifetimePeakQueuedFrames);
        long peakQueuedBytes = Interlocked.Read(ref lifetimePeakQueuedBytes);
        long configuredMaxFrames = Interlocked.Read(ref lifetimeConfiguredMaxFrames);
        long configuredMaxQueuedBytes = Interlocked.Read(ref lifetimeConfiguredMaxQueuedBytes);
        long rejectedFrames = 0;
        RuntimeConnectionQueueDetail[] details = maxDetails == 0
            ? []
            : new RuntimeConnectionQueueDetail[maxDetails];
        int detailCount = 0;

        foreach (KeyValuePair<long, TerrariaConnectionOutboundQueue> pair in queues)
        {
            TerrariaConnectionOutboundQueue queue = pair.Value;
            int connectionQueuedFrames = queue.QueuedFrames;
            long connectionQueuedBytes = queue.QueuedBytes;
            long connectionPeakQueuedFrames = queue.PeakQueuedFrames;
            long connectionPeakQueuedBytes = queue.PeakQueuedBytes;
            long connectionRejectedFrames = queue.RejectedFrames;
            bool slowClient = queue.IsSlowClient;

            trackedQueues++;
            queuedFrames += connectionQueuedFrames;
            queuedBytes += connectionQueuedBytes;
            peakQueuedFrames = Math.Max(peakQueuedFrames, connectionPeakQueuedFrames);
            peakQueuedBytes = Math.Max(peakQueuedBytes, connectionPeakQueuedBytes);
            configuredMaxFrames = Math.Max(configuredMaxFrames, queue.MaxFrames);
            configuredMaxQueuedBytes = Math.Max(configuredMaxQueuedBytes, queue.MaxQueuedBytes);
            rejectedFrames += connectionRejectedFrames;
            if (slowClient)
                slowClients++;

            if (maxDetails == 0 ||
                (connectionQueuedFrames == 0 &&
                 connectionPeakQueuedFrames == 0 &&
                 connectionRejectedFrames == 0 &&
                 !slowClient))
            {
                continue;
            }

            InsertDetail(
                details,
                ref detailCount,
                new RuntimeConnectionQueueDetail(
                    ConnectionId: pair.Key,
                    MaxFrames: queue.MaxFrames,
                    MaxQueuedBytes: queue.MaxQueuedBytes,
                    QueuedFrames: connectionQueuedFrames,
                    QueuedBytes: connectionQueuedBytes,
                    PeakQueuedFrames: connectionPeakQueuedFrames,
                    PeakQueuedBytes: connectionPeakQueuedBytes,
                    RejectedFrames: connectionRejectedFrames,
                    SlowClient: slowClient));
        }

        if (detailCount != details.Length)
            Array.Resize(ref details, detailCount);

        return new RuntimeConnectionQueueSnapshot(
            TrackedQueues: trackedQueues,
            ConfiguredMaxFrames: checked((int)Math.Min(int.MaxValue, configuredMaxFrames)),
            ConfiguredMaxQueuedBytes: configuredMaxQueuedBytes,
            QueuedFrames: checked((int)Math.Min(int.MaxValue, queuedFrames)),
            QueuedBytes: queuedBytes,
            PeakQueuedFrames: peakQueuedFrames,
            PeakQueuedBytes: peakQueuedBytes,
            RejectedFrames: rejectedFrames,
            SlowClients: slowClients,
            TopQueues: details.AsMemory());
    }

    private static void InsertDetail(
        RuntimeConnectionQueueDetail[] details,
        ref int count,
        RuntimeConnectionQueueDetail candidate)
    {
        if (details.Length == 0)
            return;

        int insertAt = count;
        for (int i = 0; i < count; i++)
        {
            if (ComesBefore(candidate, details[i]))
            {
                insertAt = i;
                break;
            }
        }

        if (insertAt >= details.Length)
            return;

        int newCount = Math.Min(count + 1, details.Length);
        for (int i = newCount - 1; i > insertAt; i--)
            details[i] = details[i - 1];

        details[insertAt] = candidate;
        count = newCount;
    }

    private static bool ComesBefore(
        RuntimeConnectionQueueDetail left,
        RuntimeConnectionQueueDetail right)
    {
        if (left.SlowClient != right.SlowClient)
            return left.SlowClient;
        if (left.PeakQueuedBytes != right.PeakQueuedBytes)
            return left.PeakQueuedBytes > right.PeakQueuedBytes;
        if (left.PeakQueuedFrames != right.PeakQueuedFrames)
            return left.PeakQueuedFrames > right.PeakQueuedFrames;
        if (left.QueuedBytes != right.QueuedBytes)
            return left.QueuedBytes > right.QueuedBytes;
        if (left.QueuedFrames != right.QueuedFrames)
            return left.QueuedFrames > right.QueuedFrames;
        if (left.RejectedFrames != right.RejectedFrames)
            return left.RejectedFrames > right.RejectedFrames;

        return left.ConnectionId < right.ConnectionId;
    }

    private static void UpdateMaximum(ref long target, long candidate)
    {
        long current = Interlocked.Read(ref target);
        while (candidate > current)
        {
            long observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
                return;

            current = observed;
        }
    }
}

internal readonly record struct RuntimeConnectionQueueDetail(
    long ConnectionId,
    int MaxFrames,
    long MaxQueuedBytes,
    int QueuedFrames,
    long QueuedBytes,
    long PeakQueuedFrames,
    long PeakQueuedBytes,
    long RejectedFrames,
    bool SlowClient);

internal readonly record struct RuntimeConnectionQueueSnapshot(
    int TrackedQueues,
    int ConfiguredMaxFrames,
    long ConfiguredMaxQueuedBytes,
    int QueuedFrames,
    long QueuedBytes,
    long PeakQueuedFrames,
    long PeakQueuedBytes,
    long RejectedFrames,
    int SlowClients,
    ReadOnlyMemory<RuntimeConnectionQueueDetail> TopQueues);
