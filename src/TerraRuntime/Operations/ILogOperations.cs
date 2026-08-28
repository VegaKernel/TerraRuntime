namespace TerraRuntime.Operations;

internal interface ILogOperations
{
    RuntimeLogSnapshot CaptureSnapshot(RuntimeLogLevel minimumLevel, int maxEntries);
}
