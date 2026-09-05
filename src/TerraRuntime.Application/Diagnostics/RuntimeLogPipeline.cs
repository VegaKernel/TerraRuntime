using System.Threading.Channels;
using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Application.Diagnostics;

/// <summary>
/// Runtime-owned MPSC logging pipeline. Producers only normalize bounded scalar text and use TryWrite;
/// sink work is isolated to one background drain worker.
/// </summary>
internal sealed class RuntimeLogPipeline : IAsyncDisposable
{
    private readonly RuntimeLogPipelineOptions options;
    private readonly Channel<RuntimeLogEnvelope> channel;
    private readonly SinkState[] sinks;
    private readonly CancellationTokenSource stop = new();
    private readonly Task drainTask;
    private readonly int normalCapacity;

    private long nextSequence;
    private long accepted;
    private long filtered;
    private long droppedTrace;
    private long droppedDebug;
    private long droppedInformation;
    private long droppedWarning;
    private long droppedError;
    private long droppedCritical;
    private long drained;
    private long sinkFailures;
    private int queued;
    private int queueHighWaterMark;
    private int normalQueued;
    private int accepting = 1;
    private int disposeStarted;

    public RuntimeLogPipeline(
        IEnumerable<IRuntimeLogSink> sinks,
        RuntimeLogPipelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        this.options = options ?? new RuntimeLogPipelineOptions();
        this.options.Validate();
        this.sinks = sinks.Select(static sink =>
        {
            ArgumentNullException.ThrowIfNull(sink);
            return new SinkState(sink);
        }).ToArray();

        if (this.sinks.Length == 0)
            throw new ArgumentException("At least one runtime log sink is required.", nameof(sinks));

        normalCapacity = this.options.QueueCapacity - this.options.PriorityReserve;
        channel = Channel.CreateBounded<RuntimeLogEnvelope>(
            new BoundedChannelOptions(this.options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        drainTask = Task.Run(DrainAsync);
    }

    public bool TryPublish(
        RuntimeLogLevel level,
        RuntimeLogEventId eventId,
        RuntimeLogCategory category,
        string subsystem,
        string message,
        RuntimeLogContext context = default,
        Exception? exception = null,
        RuntimeLogDelivery delivery = RuntimeLogDelivery.Buffered)
    {
        if (level < options.MinimumLevel)
        {
            Interlocked.Increment(ref filtered);
            return false;
        }

        if (Volatile.Read(ref accepting) == 0)
        {
            IncrementDrop(level);
            return false;
        }

        bool normal = level < RuntimeLogLevel.Warning;
        if (normal && !TryAcquireNormalSlot())
        {
            IncrementDrop(level);
            return false;
        }

        RuntimeLogRecord record = CreateRecord(level, eventId, category, subsystem, message, context, exception);

        // Reserve queue accounting before publication. A fast reader may consume a TryWrite result immediately;
        // publishing first would let the reader decrement queued before the producer increments it, producing
        // transient negative depth and losing the real high-water mark.
        int depth = Interlocked.Increment(ref queued);
        if (!channel.Writer.TryWrite(new RuntimeLogEnvelope(record, delivery)))
        {
            Interlocked.Decrement(ref queued);
            if (normal)
                Interlocked.Decrement(ref normalQueued);

            IncrementDrop(level);
            return false;
        }

        Interlocked.Increment(ref accepted);
        UpdateHighWaterMark(depth);
        return true;
    }

    public RuntimeLogPipelineMetrics CaptureMetrics() =>
        new(
            Accepted: Interlocked.Read(ref accepted),
            Filtered: Interlocked.Read(ref filtered),
            DroppedTrace: Interlocked.Read(ref droppedTrace),
            DroppedDebug: Interlocked.Read(ref droppedDebug),
            DroppedInformation: Interlocked.Read(ref droppedInformation),
            DroppedWarning: Interlocked.Read(ref droppedWarning),
            DroppedError: Interlocked.Read(ref droppedError),
            DroppedCritical: Interlocked.Read(ref droppedCritical),
            Drained: Interlocked.Read(ref drained),
            SinkFailures: Interlocked.Read(ref sinkFailures),
            QueueDepth: Volatile.Read(ref queued),
            QueueHighWaterMark: Volatile.Read(ref queueHighWaterMark));

    public RuntimeLogSinkHealth[] CaptureSinkHealth()
    {
        var result = new RuntimeLogSinkHealth[sinks.Length];
        for (int i = 0; i < sinks.Length; i++)
        {
            SinkState state = sinks[i];
            result[i] = new RuntimeLogSinkHealth(
                state.Sink.Name,
                Interlocked.Read(ref state.Failures),
                Volatile.Read(ref state.ConsecutiveFailures),
                Volatile.Read(ref state.Quarantined) != 0);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
            return;

        Volatile.Write(ref accepting, 0);
        channel.Writer.TryComplete();

        try
        {
            await drainTask.WaitAsync(options.ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            stop.Cancel();
            try
            {
                await drainTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            stop.Cancel();
        }

        foreach (SinkState state in sinks)
        {
            if (Volatile.Read(ref state.Quarantined) == 0)
                await TryFlushAsync(state).ConfigureAwait(false);

            await TryDisposeSinkAsync(state).ConfigureAwait(false);
        }

        stop.Dispose();
    }

    private RuntimeLogRecord CreateRecord(
        RuntimeLogLevel level,
        RuntimeLogEventId eventId,
        RuntimeLogCategory category,
        string subsystem,
        string message,
        RuntimeLogContext context,
        Exception? exception)
    {
        long sequence = Interlocked.Increment(ref nextSequence);
        return new RuntimeLogRecord(
            sequence,
            DateTimeOffset.UtcNow,
            level,
            eventId,
            category,
            Normalize(subsystem, options.MaximumSubsystemLength, "Runtime"),
            Normalize(message, options.MaximumMessageLength, string.Empty),
            NormalizeContext(context),
            exception is null
                ? null
                : Normalize(exception.GetType().FullName, options.MaximumExceptionFieldLength, "Exception"),
            exception is null
                ? null
                : Normalize(exception.Message, options.MaximumExceptionFieldLength, string.Empty));
    }

    private RuntimeLogContext NormalizeContext(RuntimeLogContext context) =>
        new(
            CorrelationId: NormalizeOptional(context.CorrelationId, options.MaximumContextLength),
            WorldId: NormalizeOptional(context.WorldId, options.MaximumContextLength),
            ConnectionId: NormalizeOptional(context.ConnectionId, options.MaximumContextLength),
            PlayerHandle: NormalizeOptional(context.PlayerHandle, options.MaximumContextLength),
            EntityHandle: NormalizeOptional(context.EntityHandle, options.MaximumContextLength),
            PacketDirection: NormalizeOptional(context.PacketDirection, options.MaximumContextLength),
            PacketId: context.PacketId);

    private async Task DrainAsync()
    {
        try
        {
            await foreach (RuntimeLogEnvelope envelope in channel.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                RuntimeLogRecord record = envelope.Record;
                if (record.Level < RuntimeLogLevel.Warning)
                    Interlocked.Decrement(ref normalQueued);

                Interlocked.Decrement(ref queued);
                Interlocked.Increment(ref drained);

                foreach (SinkState sink in sinks)
                {
                    if (Volatile.Read(ref sink.Quarantined) != 0)
                        continue;

                    await WriteToSinkAsync(sink, envelope).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private async ValueTask WriteToSinkAsync(SinkState state, RuntimeLogEnvelope envelope)
    {
        try
        {
            ValueTask write = state.Sink is IRuntimeLogDeliverySink deliverySink
                ? deliverySink.WriteAsync(envelope.Record, envelope.Delivery, stop.Token)
                : state.Sink.WriteAsync(envelope.Record, stop.Token);
            if (!write.IsCompletedSuccessfully)
                await write.AsTask().WaitAsync(options.SinkTimeout, stop.Token).ConfigureAwait(false);
            else
                write.GetAwaiter().GetResult();

            Volatile.Write(ref state.ConsecutiveFailures, 0);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            RegisterSinkFailure(state);
        }
    }

    private async ValueTask TryFlushAsync(SinkState state)
    {
        try
        {
            ValueTask flush = state.Sink.FlushAsync(CancellationToken.None);
            if (!flush.IsCompletedSuccessfully)
                await flush.AsTask().WaitAsync(options.SinkTimeout).ConfigureAwait(false);
            else
                flush.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            RegisterSinkFailure(state);
        }
    }

    private async ValueTask TryDisposeSinkAsync(SinkState state)
    {
        try
        {
            ValueTask dispose = state.Sink.DisposeAsync();
            if (!dispose.IsCompletedSuccessfully)
                await dispose.AsTask().WaitAsync(options.SinkTimeout).ConfigureAwait(false);
            else
                dispose.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            RegisterSinkFailure(state);
        }
    }

    private void RegisterSinkFailure(SinkState state)
    {
        Interlocked.Increment(ref state.Failures);
        Interlocked.Increment(ref sinkFailures);
        int consecutive = Interlocked.Increment(ref state.ConsecutiveFailures);
        if (consecutive >= options.SinkFailureThreshold)
            Volatile.Write(ref state.Quarantined, 1);
    }

    private bool TryAcquireNormalSlot()
    {
        while (true)
        {
            int current = Volatile.Read(ref normalQueued);
            if (current >= normalCapacity)
                return false;

            if (Interlocked.CompareExchange(ref normalQueued, current + 1, current) == current)
                return true;
        }
    }

    private void IncrementDrop(RuntimeLogLevel level)
    {
        switch (level)
        {
            case RuntimeLogLevel.Trace:
                Interlocked.Increment(ref droppedTrace);
                break;
            case RuntimeLogLevel.Debug:
                Interlocked.Increment(ref droppedDebug);
                break;
            case RuntimeLogLevel.Information:
                Interlocked.Increment(ref droppedInformation);
                break;
            case RuntimeLogLevel.Warning:
                Interlocked.Increment(ref droppedWarning);
                break;
            case RuntimeLogLevel.Error:
                Interlocked.Increment(ref droppedError);
                break;
            case RuntimeLogLevel.Critical:
                Interlocked.Increment(ref droppedCritical);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(level));
        }
    }

    private void UpdateHighWaterMark(int depth)
    {
        int current = Volatile.Read(ref queueHighWaterMark);
        while (depth > current)
        {
            int observed = Interlocked.CompareExchange(ref queueHighWaterMark, depth, current);
            if (observed == current)
                return;

            current = observed;
        }
    }

    private static string? NormalizeOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Normalize(value, maximumLength, string.Empty);

    private static string Normalize(string? value, int maximumLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        int length = Math.Min(value.Length, maximumLength);
        bool requiresCopy = length != value.Length;
        for (int i = 0; i < length && !requiresCopy; i++)
            requiresCopy = char.IsControl(value[i]);

        if (!requiresCopy)
            return value;

        return string.Create(length, (value, length), static (destination, state) =>
        {
            for (int i = 0; i < state.length; i++)
            {
                char c = state.value[i];
                destination[i] = char.IsControl(c) ? ' ' : c;
            }
        });
    }

    private readonly record struct RuntimeLogEnvelope(
        RuntimeLogRecord Record,
        RuntimeLogDelivery Delivery);

    private sealed class SinkState(IRuntimeLogSink sink)
    {
        public IRuntimeLogSink Sink { get; } = sink;

        public long Failures;

        public int ConsecutiveFailures;

        public int Quarantined;
    }
}
