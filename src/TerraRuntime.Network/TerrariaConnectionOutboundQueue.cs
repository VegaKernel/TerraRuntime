namespace TerraRuntime.Network;

public sealed class TerrariaConnectionOutboundQueue
{
    private readonly BoundedOutboundQueue _queue;
    private readonly SlowClientPolicy _slowClientPolicy;
    private readonly TaskCompletionSource<bool> _slowClientSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _slowClient;

    public TerrariaConnectionOutboundQueue(
        OutboundQueueOptions options,
        SlowClientPolicy slowClientPolicy = SlowClientPolicy.DisconnectOnQueueOverflow)
    {
        _queue = new BoundedOutboundQueue(options);
        _slowClientPolicy = slowClientPolicy;
    }

    public SlowClientPolicy SlowClientPolicy => _slowClientPolicy;

    public bool IsSlowClient => Volatile.Read(ref _slowClient) != 0;

    public bool IsCompleted => _queue.IsCompleted;

    public int MaxFrames => _queue.MaxFrames;

    public long MaxQueuedBytes => _queue.MaxQueuedBytes;

    public int QueuedFrames => _queue.QueuedFrames;

    public long QueuedBytes => _queue.QueuedBytes;

    public long PeakQueuedFrames => _queue.PeakQueuedFrames;

    public long PeakQueuedBytes => _queue.PeakQueuedBytes;

    public long RejectedFrames => _queue.RejectedFrames;

    internal BoundedOutboundQueue InnerQueue => _queue;

    internal Task SlowClientSignal => _slowClientSignal.Task;

    public OutboundEnqueueResult TryEnqueue(OutboundFrame frame)
    {
        OutboundEnqueueResult result = _queue.TryEnqueue(frame);
        if (_slowClientPolicy == SlowClientPolicy.DisconnectOnQueueOverflow &&
            result is OutboundEnqueueResult.FrameBudgetExceeded or OutboundEnqueueResult.ByteBudgetExceeded)
        {
            SignalSlowClient();
        }

        return result;
    }

    /// <summary>
    /// Attempts to enqueue replaceable/opportunistic state without permanently classifying the peer as a slow
    /// client when the per-connection queue is momentarily full. Use only for state that can be recomputed and
    /// retried later (for example post-join section streaming), never for ordered bootstrap/control traffic.
    /// </summary>
    public OutboundEnqueueResult TryEnqueueOpportunistic(OutboundFrame frame) =>
        _queue.TryEnqueue(frame);

    public OutboundEnqueueResult TryEnqueueBatch(ReadOnlySpan<OutboundFrame> frames)
    {
        OutboundEnqueueResult result = _queue.TryEnqueueBatch(frames);
        if (_slowClientPolicy == SlowClientPolicy.DisconnectOnQueueOverflow &&
            result is OutboundEnqueueResult.FrameBudgetExceeded or OutboundEnqueueResult.ByteBudgetExceeded)
        {
            SignalSlowClient();
        }

        return result;
    }

    public bool Complete(Exception? error = null) => _queue.Complete(error);

    private void SignalSlowClient()
    {
        if (Interlocked.Exchange(ref _slowClient, 1) == 0)
        {
            _slowClientSignal.TrySetResult(true);
        }
    }
}
