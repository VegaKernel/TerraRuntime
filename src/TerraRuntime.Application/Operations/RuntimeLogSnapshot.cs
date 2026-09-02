using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Operations;

internal enum RuntimeLogLevel : byte
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3
}

internal readonly record struct RuntimeLogEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    RuntimeLogLevel Level,
    string Source,
    string Message,
    int EventId = 0,
    RuntimeLogCategory Category = RuntimeLogCategory.Operations,
    string? CorrelationId = null);

internal readonly record struct RuntimeLogQuery(
    RuntimeLogLevel MinimumLevel,
    int MaxEntries,
    string? Source = null,
    RuntimeLogCategory? Category = null,
    int? EventId = null,
    string? CorrelationId = null);

internal readonly record struct RuntimeLogSinkSnapshot(
    string Name,
    long Failures,
    int ConsecutiveFailures,
    bool Quarantined);

internal readonly record struct RuntimeLogDiagnosticsSnapshot(
    long Accepted,
    long Filtered,
    long DroppedTrace,
    long DroppedDebug,
    long DroppedInformation,
    long DroppedWarning,
    long DroppedError,
    long DroppedCritical,
    long Drained,
    long SinkFailures,
    int QueueDepth,
    int QueueHighWaterMark,
    long RecentPublished,
    long RecentOverwritten,
    ReadOnlyMemory<RuntimeLogSinkSnapshot> Sinks)
{
    public long DroppedTotal =>
        DroppedTrace + DroppedDebug + DroppedInformation + DroppedWarning + DroppedError + DroppedCritical;
}

internal readonly record struct RuntimeLogSnapshot(
    ReadOnlyMemory<RuntimeLogEntry> Entries,
    long PublishedEntries,
    long OverwrittenEntries,
    RuntimeLogLevel MinimumLevel,
    DateTimeOffset CapturedAtUtc,
    RuntimeLogDiagnosticsSnapshot Diagnostics = default);
