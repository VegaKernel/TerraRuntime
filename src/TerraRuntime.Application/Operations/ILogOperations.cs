namespace TerraRuntime.Application.Operations;

internal interface ILogOperations
{
    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogQuery query);

    RuntimeLogSnapshot CaptureSnapshot(OperationsLogLevel minimumLevel, string? source, int maxEntries) =>
        CaptureSnapshot(new RuntimeLogQuery(minimumLevel, maxEntries, source));

    RuntimeLogSnapshot CaptureSnapshot(OperationsLogLevel minimumLevel, int maxEntries) =>
        CaptureSnapshot(new RuntimeLogQuery(minimumLevel, maxEntries));

    ReadOnlyMemory<string> CaptureSources(int maxSources);
}
