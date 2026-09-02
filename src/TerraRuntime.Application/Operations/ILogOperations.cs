namespace TerraRuntime.Operations;

internal interface ILogOperations
{
    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogQuery query);

    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, string? source, int maxEntries) =>
        CaptureSnapshot(new RuntimeLogQuery(minimumLevel, maxEntries, source));

    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, int maxEntries) =>
        CaptureSnapshot(new RuntimeLogQuery(minimumLevel, maxEntries));

    ReadOnlyMemory<string> CaptureSources(int maxSources);
}
