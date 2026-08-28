using System.Collections.Concurrent;
using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class RuntimeConnectionQueueTelemetry
{
    private readonly ConcurrentDictionary<long, TerrariaConnectionOutboundQueue> queues = new();

    public bool TryRegister(long connectionId, TerrariaConnectionOutboundQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return connectionId > 0 && queues.TryAdd(connectionId, queue);
    }

    public bool TryUnregister(long connectionId) => queues.TryRemove(connectionId, out _);

    public RuntimeConnectionQueueSnapshot CaptureSnapshot()
    {
        int trackedQueues = 0;
        int slowClients = 0;
        long queuedFrames = 0;
        long queuedBytes = 0;
        long rejectedFrames = 0;

        foreach (TerrariaConnectionOutboundQueue queue in queues.Values)
        {
            trackedQueues++;
            queuedFrames += queue.QueuedFrames;
            queuedBytes += queue.QueuedBytes;
            rejectedFrames += queue.RejectedFrames;
            if (queue.IsSlowClient)
                slowClients++;
        }

        return new RuntimeConnectionQueueSnapshot(
            TrackedQueues: trackedQueues,
            QueuedFrames: queuedFrames,
            QueuedBytes: queuedBytes,
            RejectedFrames: rejectedFrames,
            SlowClients: slowClients);
    }
}

internal readonly record struct RuntimeConnectionQueueSnapshot(
    int TrackedQueues,
    long QueuedFrames,
    long QueuedBytes,
    long RejectedFrames,
    int SlowClients);
