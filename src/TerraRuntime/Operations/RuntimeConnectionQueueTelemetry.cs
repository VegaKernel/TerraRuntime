using System.Collections.Concurrent;
using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class RuntimeConnectionQueueTelemetry
{
    private const int MaximumDetailCount = 64;
    private readonly ConcurrentDictionary<long, TerrariaConnectionOutboundQueue> queues = new();

    public bool TryRegister(long connectionId, TerrariaConnectionOutboundQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return connectionId > 0 && queues.TryAdd(connectionId, queue);
    }

    public bool TryUnregister(long connectionId) => queues.TryRemove(connectionId, out _);

    public RuntimeConnectionQueueSnapshot CaptureSnapshot(int maxDetails = 0)
    {
        if (maxDetails < 0 || maxDetails > MaximumDetailCount)
            throw new ArgumentOutOfRangeException(nameof(maxDetails));

        int trackedQueues = 0;
        int slowClients = 0;
        long queuedFrames = 0;
        long queuedBytes = 0;
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
            long connectionRejectedFrames = queue.RejectedFrames;
            bool slowClient = queue.IsSlowClient;

            trackedQueues++;
            queuedFrames += connectionQueuedFrames;
            queuedBytes += connectionQueuedBytes;
            rejectedFrames += connectionRejectedFrames;
            if (slowClient)
                slowClients++;

            if (maxDetails == 0 ||
                (connectionQueuedFrames == 0 && connectionRejectedFrames == 0 && !slowClient))
            {
                continue;
            }

            InsertDetail(
                details,
                ref detailCount,
                new RuntimeConnectionQueueDetail(
                    ConnectionId: pair.Key,
                    QueuedFrames: connectionQueuedFrames,
                    QueuedBytes: connectionQueuedBytes,
                    RejectedFrames: connectionRejectedFrames,
                    SlowClient: slowClient));
        }

        if (detailCount != details.Length)
            Array.Resize(ref details, detailCount);

        return new RuntimeConnectionQueueSnapshot(
            TrackedQueues: trackedQueues,
            QueuedFrames: queuedFrames,
            QueuedBytes: queuedBytes,
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
        if (left.QueuedBytes != right.QueuedBytes)
            return left.QueuedBytes > right.QueuedBytes;
        if (left.QueuedFrames != right.QueuedFrames)
            return left.QueuedFrames > right.QueuedFrames;
        if (left.RejectedFrames != right.RejectedFrames)
            return left.RejectedFrames > right.RejectedFrames;

        return left.ConnectionId < right.ConnectionId;
    }
}

internal readonly record struct RuntimeConnectionQueueDetail(
    long ConnectionId,
    int QueuedFrames,
    long QueuedBytes,
    long RejectedFrames,
    bool SlowClient);

internal readonly record struct RuntimeConnectionQueueSnapshot(
    int TrackedQueues,
    long QueuedFrames,
    long QueuedBytes,
    long RejectedFrames,
    int SlowClients,
    ReadOnlyMemory<RuntimeConnectionQueueDetail> TopQueues);
