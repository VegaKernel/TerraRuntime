namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>Compact immutable structured event emitted by TerraRuntime.</summary>
public readonly record struct RuntimeLogRecord(
    long Sequence,
    DateTimeOffset TimestampUtc,
    RuntimeLogLevel Level,
    RuntimeLogEventId EventId,
    RuntimeLogCategory Category,
    string Subsystem,
    string Message,
    RuntimeLogContext Context,
    string? ExceptionType = null,
    string? ExceptionMessage = null);
