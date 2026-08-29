using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Diagnostics;
using StructuredLogLevel = TerraRuntime.Contracts.Diagnostics.RuntimeLogLevel;

namespace TerraRuntime.Operations;

/// <summary>
/// Bounded operations read model backed by the same structured recent-log store used by runtime logging.
/// The reserved read-only source "Chat" projects separate bounded public-chat telemetry without turning
/// chat routing itself into logging.
/// </summary>
internal sealed class RuntimeLogBuffer : ILogOperations, IRuntimeLogSink
{
    public const int DefaultCapacity = RuntimeRecentLogStore.DefaultCapacity;
    public const int MaximumCapacity = RuntimeRecentLogStore.MaximumCapacity;
    public const int MaximumSourceLength = 64;
    public const int MaximumMessageLength = 2_048;

    private const string ChatSource = "Chat";

    private readonly RuntimeRecentLogStore store;
    private long localSequence;

    public RuntimeLogBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        store = new RuntimeRecentLogStore(capacity);
    }

    public string Name => "operations-recent";

    public int Capacity => store.Capacity;

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
        CaptureSnapshot(minimumLevel, source: null, maxEntries);

    public RuntimeLogSnapshot CaptureSnapshot(
        RuntimeLogLevel minimumLevel,
        string? source,
        int maxEntries)
    {
        if (minimumLevel < RuntimeLogLevel.Debug || minimumLevel > RuntimeLogLevel.Error)
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        if (maxEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        if (string.Equals(source, ChatSource, StringComparison.Ordinal))
            return CaptureChatSnapshot(minimumLevel, maxEntries);

        if (maxEntries == 0)
            return EmptySnapshot(minimumLevel);

        RuntimeLogRecord[] records = store.Capture(
            ToStructuredLevel(minimumLevel),
            category: null,
            maximumEntries: source is null ? maxEntries : store.Capacity);

        RuntimeLogEntry[] snapshot;
        if (source is null)
        {
            snapshot = new RuntimeLogEntry[records.Length];
            for (int i = 0; i < records.Length; i++)
                snapshot[i] = ToOperationsEntry(records[i]);
        }
        else
        {
            var newest = new RuntimeLogEntry[Math.Min(maxEntries, records.Length)];
            int found = 0;
            for (int i = records.Length - 1; i >= 0 && found < newest.Length; i--)
            {
                RuntimeLogRecord record = records[i];
                if (!string.Equals(record.Subsystem, source, StringComparison.Ordinal))
                    continue;

                newest[found++] = ToOperationsEntry(record);
            }

            if (found != newest.Length)
                Array.Resize(ref newest, found);
            Array.Reverse(newest);
            snapshot = newest;
        }

        return new RuntimeLogSnapshot(
            snapshot.AsMemory(),
            store.Published,
            store.Overwritten,
            minimumLevel,
            DateTimeOffset.UtcNow);
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
        new(
            ReadOnlyMemory<RuntimeLogEntry>.Empty,
            store.Published,
            store.Overwritten,
            minimumLevel,
            DateTimeOffset.UtcNow);

    private static RuntimeLogSnapshot CaptureChatSnapshot(
        RuntimeLogLevel minimumLevel,
        int maxEntries)
    {
        ReadOnlySpan<RuntimeChatEntry> chat = RuntimeChatTelemetry.Capture(maxEntries).Span;
        if (chat.Length == 0 || minimumLevel > RuntimeLogLevel.Information)
        {
            return new RuntimeLogSnapshot(
                ReadOnlyMemory<RuntimeLogEntry>.Empty,
                0,
                0,
                minimumLevel,
                DateTimeOffset.UtcNow);
        }

        var snapshot = new RuntimeLogEntry[chat.Length];
        for (int i = 0; i < chat.Length; i++)
        {
            RuntimeChatEntry entry = chat[i];
            snapshot[i] = new RuntimeLogEntry(
                entry.Sequence,
                entry.TimestampUtc,
                RuntimeLogLevel.Information,
                ChatSource,
                $"#{entry.PlayerSlot}: {entry.Text}");
        }

        long published = chat[^1].Sequence;
        return new RuntimeLogSnapshot(
            snapshot.AsMemory(),
            published,
            0,
            minimumLevel,
            DateTimeOffset.UtcNow);
    }

    private static RuntimeLogEntry ToOperationsEntry(RuntimeLogRecord record) =>
        new(
            record.Sequence,
            record.TimestampUtc,
            ToOperationsLevel(record.Level),
            Normalize(record.Subsystem, MaximumSourceLength, "Runtime"),
            Normalize(record.Message, MaximumMessageLength, string.Empty));

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
        {
            if (char.IsControl(buffer[i]))
                buffer[i] = ' ';
        }

        return new string(buffer);
    }
}
