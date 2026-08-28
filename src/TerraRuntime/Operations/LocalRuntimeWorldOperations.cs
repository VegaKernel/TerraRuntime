namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeWorldOperations : IWorldOperations
{
    private readonly RuntimeWorldSnapshot snapshot;

    public LocalRuntimeWorldOperations(RuntimeWorldSnapshot snapshot)
    {
        this.snapshot = snapshot;
    }

    public RuntimeWorldSnapshot CaptureSnapshot() =>
        snapshot with { CapturedAtUtc = DateTimeOffset.UtcNow };
}
