namespace TerraRuntime.Operations;

internal interface ILogOperations
{
    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, string? source, int maxEntries);

    ReadOnlyMemory<string> CaptureSources(int maxSources);
}
