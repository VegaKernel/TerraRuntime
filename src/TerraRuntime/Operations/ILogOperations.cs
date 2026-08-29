namespace TerraRuntime.Operations;

internal interface ILogOperations
{
    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, string? source, int maxEntries);

    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, int maxEntries) =>
        CaptureSnapshot(minimumLevel, source: null, maxEntries);

    ReadOnlyMemory<string> CaptureSources(int maxSources);
}
