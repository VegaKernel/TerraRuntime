using System.Threading.Channels;

namespace TerraRuntime.Network;

public sealed class BoundedOutboundQueue
{
    private readonly Channel<OutboundFrame> _channel;
    private readonly OutboundQueueOptions _options;
    private int _completed;
    private int _queuedFrames;
    private long _queuedBytes;
    private long _peakQueuedFrames;
    private long _peakQueuedBytes;
    private long _rejectedFrames;
    private readonly object _writeGate = new();

    public BoundedOutboundQueue(OutboundQueueOptions options)
    {
        _options = options;
        _channel = Channel.CreateBounded<OutboundFrame>(new BoundedChannelOptions(options.MaxFrames)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public int MaxFrames => _options.MaxFrames;

    public long MaxQueuedBytes => _options.MaxQueuedBytes;

    public int QueuedFrames => Volatile.Read(ref _queuedFrames);

    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    public long PeakQueuedFrames => Interlocked.Read(ref _peakQueuedFrames);

    public long PeakQueuedBytes => Interlocked.Read(ref _peakQueuedBytes);

    public long RejectedFrames => Interlocked.Read(ref _rejectedFrames);

    public OutboundEnqueueResult TryEnqueue(OutboundFrame frame)
    {
        lock (_writeGate)
            return TryEnqueueLocked(frame);
    }

    /// <summary>
    /// Atomically admits a bounded frame batch. Either every frame is published in order or none is.
    /// Runtime world transfer uses this so a client cannot observe half of a replacement-world bootstrap.
    /// </summary>
    public OutboundEnqueueResult TryEnqueueBatch(ReadOnlySpan<OutboundFrame> frames)
    {
        if (frames.Length == 0)
            return OutboundEnqueueResult.Enqueued;

        lock (_writeGate)
        {
            if (IsCompleted)
            {
                RejectBatch(frames.Length);
                return OutboundEnqueueResult.Closed;
            }

            long addedBytes = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                int length = frames[i].Length;
                if (length <= 0 || length > _options.MaxFrameBytes)
                {
                    RejectBatch(frames.Length);
                    return OutboundEnqueueResult.FrameTooLarge;
                }
                addedBytes = checked(addedBytes + length);
            }

            int currentFrames = Volatile.Read(ref _queuedFrames);
            long currentBytes = Interlocked.Read(ref _queuedBytes);
            if ((long)currentFrames + frames.Length > _options.MaxFrames)
            {
                RejectBatch(frames.Length);
                return OutboundEnqueueResult.FrameBudgetExceeded;
            }
            if (currentBytes + addedBytes > _options.MaxQueuedBytes)
            {
                RejectBatch(frames.Length);
                return OutboundEnqueueResult.ByteBudgetExceeded;
            }

            int reservedFrames = Interlocked.Add(ref _queuedFrames, frames.Length);
            long reservedBytes = Interlocked.Add(ref _queuedBytes, addedBytes);
            int written = 0;
            for (; written < frames.Length; written++)
            {
                if (_channel.Writer.TryWrite(frames[written]))
                    continue;

                // Complete() shares the write gate, so failure here can only be a channel invariant violation.
                // Undo counters for the unpublished suffix and fail closed. Already-published prefix cannot be
                // recalled, therefore throw instead of pretending the batch was rejected cleanly.
                int remaining = frames.Length - written;
                long remainingBytes = 0;
                for (int i = written; i < frames.Length; i++)
                    remainingBytes += frames[i].Length;
                Interlocked.Add(ref _queuedFrames, -remaining);
                Interlocked.Add(ref _queuedBytes, -remainingBytes);
                throw new InvalidOperationException("Outbound channel rejected an already-reserved frame batch.");
            }

            UpdateMaximum(ref _peakQueuedFrames, reservedFrames);
            UpdateMaximum(ref _peakQueuedBytes, reservedBytes);
            return OutboundEnqueueResult.Enqueued;
        }
    }

    private OutboundEnqueueResult TryEnqueueLocked(OutboundFrame frame)
    {
        if (IsCompleted)
        {
            Reject();
            return OutboundEnqueueResult.Closed;
        }

        int length = frame.Length;
        if (length <= 0 || length > _options.MaxFrameBytes)
        {
            Reject();
            return OutboundEnqueueResult.FrameTooLarge;
        }

        long queuedBytes = Interlocked.Add(ref _queuedBytes, length);
        if (queuedBytes > _options.MaxQueuedBytes)
        {
            Interlocked.Add(ref _queuedBytes, -length);
            Reject();
            return OutboundEnqueueResult.ByteBudgetExceeded;
        }

        int queuedFrames = Interlocked.Increment(ref _queuedFrames);
        if (queuedFrames > _options.MaxFrames)
        {
            Interlocked.Decrement(ref _queuedFrames);
            Interlocked.Add(ref _queuedBytes, -length);
            Reject();
            return OutboundEnqueueResult.FrameBudgetExceeded;
        }

        if (!_channel.Writer.TryWrite(frame))
        {
            Interlocked.Decrement(ref _queuedFrames);
            Interlocked.Add(ref _queuedBytes, -length);
            Reject();
            return IsCompleted
                ? OutboundEnqueueResult.Closed
                : OutboundEnqueueResult.FrameBudgetExceeded;
        }

        UpdateMaximum(ref _peakQueuedFrames, queuedFrames);
        UpdateMaximum(ref _peakQueuedBytes, queuedBytes);
        return OutboundEnqueueResult.Enqueued;
    }

    public bool TryPeek(out OutboundFrame frame) => _channel.Reader.TryPeek(out frame);

    public bool TryRead(out OutboundFrame frame)
    {
        if (!_channel.Reader.TryRead(out frame))
        {
            return false;
        }

        Release(frame);
        return true;
    }

    public async ValueTask<OutboundFrame> ReadAsync(CancellationToken cancellationToken = default)
    {
        OutboundFrame frame = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        Release(frame);
        return frame;
    }

    public bool Complete(Exception? error = null)
    {
        lock (_writeGate)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return false;

            return _channel.Writer.TryComplete(error);
        }
    }

    private void Release(OutboundFrame frame)
    {
        Interlocked.Decrement(ref _queuedFrames);
        Interlocked.Add(ref _queuedBytes, -frame.Length);
    }

    private void Reject() => Interlocked.Increment(ref _rejectedFrames);

    private void RejectBatch(int count) => Interlocked.Add(ref _rejectedFrames, count);

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
