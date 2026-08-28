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
    string Message);

internal readonly record struct RuntimeLogSnapshot(
    ReadOnlyMemory<RuntimeLogEntry> Entries,
    long PublishedEntries,
    long OverwrittenEntries,
    RuntimeLogLevel MinimumLevel,
    DateTimeOffset CapturedAtUtc);
