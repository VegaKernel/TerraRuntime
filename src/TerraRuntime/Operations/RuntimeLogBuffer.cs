using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Diagnostics;
using StructuredLogLevel = TerraRuntime.Contracts.Diagnostics.RuntimeLogLevel;

namespace TerraRuntime.Operations;

internal sealed class RuntimeLogBuffer : ILogOperations, IRuntimeLogSink
{
    public const int DefaultCapacity = RuntimeRecentLogStore.DefaultCapacity;
    public const int MaximumCapacity = RuntimeRecentLogStore.MaximumCapacity;
    public const int MaximumSourceLength = 64;
    public const int MaximumMessageLength = 2_048;
    private const string ChatSource = "Chat";
    private readonly RuntimeRecentLogStore store;
    private Func<RuntimeLogPipelineMetrics>? metricsProvider;
    private Func<RuntimeLogSinkHealth[]>? sinkHealthProvider;
    private long localSequence;

    public RuntimeLogBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        store = new RuntimeRecentLogStore(capacity);
    }

    public string Name => "operations-recent";
    public int Capacity => store.Capacity;

    internal void AttachPipelineDiagnostics(
        Func<RuntimeLogPipelineMetrics> metricsProvider,
        Func<RuntimeLogSinkHealth[]> sinkHealthProvider)
    {
        ArgumentNullException.ThrowIfNull(metricsProvider);
        ArgumentNullException.ThrowIfNull(sinkHealthProvider);
        Volatile.Write(ref this.metricsProvider, metricsProvider);
        Volatile.Write(ref this.sinkHealthProvider, sinkHealthProvider);
    }

    public void Publish(RuntimeLogLevel level, string source, string message)
    {
        if (level < RuntimeLogLevel.Debug || level > RuntimeLogLevel.Error)
            throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(message);
        var record = new RuntimeLogRecord(
            Sequence: 0,
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: ToStructuredLevel(level),
            EventId: RuntimeLogEventIds.OperationsReadModelMessage,
            Category: RuntimeLogCategory.Operations,
            Subsystem: Normalize(source, MaximumSourceLength, "Runtime"),
            Message: Normalize(message, MaximumMessageLength, string.Empty),
            Context: default);
        WriteAsync(record, CancellationToken.None).GetAwaiter().GetResult();
    }

    public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
    {
        long sequence = Interlocked.Increment(ref localSequence);
        return store.WriteAsync(record with { Sequence = sequence }, cancellationToken);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken) => store.FlushAsync(cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, int maxEntries) =>
        CaptureSnapshot(new RuntimeLogQuery(minimumLevel, maxEntries));

    public RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, string? source, int maxEntries) =>
        CaptureSnapshot(new RuntimeLogQuery(minimumLevel, maxEntries, source));

    public RuntimeLogSnapshot CaptureSnapshot(RuntimeLogQuery query)
    {
        ValidateQuery(query);
        if (string.Equals(query.Source, ChatSource, StringComparison.Ordinal))
            return CaptureChatSnapshot(query);
        if (query.MaxEntries == 0)
            return EmptySnapshot(query.MinimumLevel);

        RuntimeLogRecord[] records = store.Capture(
            ToStructuredLevel(query.MinimumLevel),
            query.Category,
            store.Capacity);
        var newest = new RuntimeLogEntry[Math.Min(query.MaxEntries, records.Length)];
        int found = 0;
        for (int i = records.Length - 1; i >= 0 && found < newest.Length; i--)
        {
            RuntimeLogRecord record = records[i];
            if (!Matches(record, query))
                continue;
            newest[found++] = ToOperationsEntry(record);
        }

        if (found != newest.Length)
            Array.Resize(ref newest, found);
        Array.Reverse(newest);
        return CreateSnapshot(newest.AsMemory(), query.MinimumLevel);
    }

    public ReadOnlyMemory<string> CaptureSources(int maxSources)
    {
        if (maxSources < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSources));
        if (maxSources == 0 || store.Published == 0)
            return ReadOnlyMemory<string>.Empty;
        RuntimeLogRecord[] records = store.Capture(maximumEntries: store.Capacity);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < records.Length && sources.Count < maxSources; i++)
            sources.Add(Normalize(records[i].Subsystem, MaximumSourceLength, "Runtime"));
        string[] snapshot = sources.ToArray();
        Array.Sort(snapshot, StringComparer.Ordinal);
        return snapshot.AsMemory();
    }

    private RuntimeLogSnapshot EmptySnapshot(RuntimeLogLevel minimumLevel) =>
        CreateSnapshot(ReadOnlyMemory<RuntimeLogEntry>.Empty, minimumLevel);

    private RuntimeLogSnapshot CaptureChatSnapshot(RuntimeLogQuery query)
    {
        if (query.Category is not null || query.EventId is not null || query.CorrelationId is not null)
            return EmptySnapshot(query.MinimumLevel);
        ReadOnlySpan<RuntimeChatEntry> chat = RuntimeChatTelemetry.Capture(query.MaxEntries).Span;
        if (chat.Length == 0 || query.MinimumLevel > RuntimeLogLevel.Information)
            return EmptySnapshot(query.MinimumLevel);
        var snapshot = new RuntimeLogEntry[chat.Length];
        for (int i = 0; i < chat.Length; i++)
        {
            RuntimeChatEntry entry = chat[i];
            snapshot[i] = new RuntimeLogEntry(
                entry.Sequence,
                entry.TimestampUtc,
                RuntimeLogLevel.Information,
                ChatSource,
                $"#{entry.PlayerSlot}: {entry.Text}",
                RuntimeLogEventIds.OperationsReadModelMessage.Value,
                RuntimeLogCategory.Operations);
        }
        long published = chat[^1].Sequence;
        return new RuntimeLogSnapshot(
            snapshot.AsMemory(),
            published,
            0,
            query.MinimumLevel,
            DateTimeOffset.UtcNow,
            CaptureDiagnostics());
    }

    private RuntimeLogSnapshot CreateSnapshot(ReadOnlyMemory<RuntimeLogEntry> entries, RuntimeLogLevel minimumLevel) =>
        new(
            entries,
            store.Published,
            store.Overwritten,
            minimumLevel,
            DateTimeOffset.UtcNow,
            CaptureDiagnostics());

    private RuntimeLogDiagnosticsSnapshot CaptureDiagnostics()
    {
        Func<RuntimeLogPipelineMetrics>? metrics = Volatile.Read(ref metricsProvider);
        Func<RuntimeLogSinkHealth[]>? health = Volatile.Read(ref sinkHealthProvider);
        if (metrics is null || health is null)
        {
            return new RuntimeLogDiagnosticsSnapshot(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                store.Published,
                store.Overwritten,
                ReadOnlyMemory<RuntimeLogSinkSnapshot>.Empty);
        }

        RuntimeLogPipelineMetrics pipeline = metrics();
        RuntimeLogSinkHealth[] pipelineHealth = health();
        var sinks = new RuntimeLogSinkSnapshot[pipelineHealth.Length];
        for (int i = 0; i < pipelineHealth.Length; i++)
        {
            RuntimeLogSinkHealth sink = pipelineHealth[i];
            sinks[i] = new RuntimeLogSinkSnapshot(
                sink.Name,
                sink.Failures,
                sink.ConsecutiveFailures,
                sink.Quarantined);
        }

        return new RuntimeLogDiagnosticsSnapshot(
            pipeline.Accepted,
            pipeline.Filtered,
            pipeline.DroppedTrace,
            pipeline.DroppedDebug,
            pipeline.DroppedInformation,
            pipeline.DroppedWarning,
            pipeline.DroppedError,
            pipeline.DroppedCritical,
            pipeline.Drained,
            pipeline.SinkFailures,
            pipeline.QueueDepth,
            pipeline.QueueHighWaterMark,
            store.Published,
            store.Overwritten,
            sinks.AsMemory());
    }

    private static void ValidateQuery(RuntimeLogQuery query)
    {
        if (query.MinimumLevel < RuntimeLogLevel.Debug || query.MinimumLevel > RuntimeLogLevel.Error)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.MaxEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.EventId is < 0)
            throw new ArgumentOutOfRangeException(nameof(query));
    }

    private static bool Matches(RuntimeLogRecord record, RuntimeLogQuery query)
    {
        if (query.Source is not null && !string.Equals(record.Subsystem, query.Source, StringComparison.Ordinal))
            return false;
        if (query.EventId is int eventId && record.EventId.Value != eventId)
            return false;
        if (query.CorrelationId is not null &&
            !string.Equals(record.Context.CorrelationId, query.CorrelationId, StringComparison.Ordinal))
            return false;
        return true;
    }

    private static RuntimeLogEntry ToOperationsEntry(RuntimeLogRecord record) =>
        new(
            record.Sequence,
            record.TimestampUtc,
            ToOperationsLevel(record.Level),
            Normalize(record.Subsystem, MaximumSourceLength, "Runtime"),
            Normalize(record.Message, MaximumMessageLength, string.Empty),
            record.EventId.Value,
            record.Category,
            record.Context.CorrelationId);

    private static StructuredLogLevel ToStructuredLevel(RuntimeLogLevel level) => level switch
    {
        RuntimeLogLevel.Debug => StructuredLogLevel.Debug,
        RuntimeLogLevel.Information => StructuredLogLevel.Information,
        RuntimeLogLevel.Warning => StructuredLogLevel.Warning,
        RuntimeLogLevel.Error => StructuredLogLevel.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private static RuntimeLogLevel ToOperationsLevel(StructuredLogLevel level) => level switch
    {
        StructuredLogLevel.Trace or StructuredLogLevel.Debug => RuntimeLogLevel.Debug,
        StructuredLogLevel.Information => RuntimeLogLevel.Information,
        StructuredLogLevel.Warning => RuntimeLogLevel.Warning,
        StructuredLogLevel.Error or StructuredLogLevel.Critical => RuntimeLogLevel.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private static string Normalize(string value, int maximumLength, string fallback)
    {
        if (value.Length == 0)
            return fallback;
        int length = Math.Min(value.Length, maximumLength);
        bool requiresCopy = value.Length > maximumLength;
        for (int i = 0; i < length && !requiresCopy; i++)
            requiresCopy = char.IsControl(value[i]);
        if (!requiresCopy)
            return value;
        char[] buffer = value.AsSpan(0, length).ToArray();
        for (int i = 0; i < buffer.Length; i++)
            if (char.IsControl(buffer[i])) buffer[i] = ' ';
        return new string(buffer);
    }
}
