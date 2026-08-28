namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeWorldOperations : IWorldOperations
{
    private readonly RuntimeWorldSnapshot snapshot;
    private readonly RuntimeWorldClockOperationsTelemetry? clockTelemetry;

    public LocalRuntimeWorldOperations(
        RuntimeWorldSnapshot snapshot,
        RuntimeWorldClockOperationsTelemetry? clockTelemetry = null)
    {
        this.snapshot = snapshot;
        this.clockTelemetry = clockTelemetry;
    }

    public RuntimeWorldSnapshot CaptureSnapshot()
    {
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        if (clockTelemetry is null)
            return snapshot with { CapturedAtUtc = capturedAtUtc };

        RuntimeWorldClockTelemetrySnapshot clock = clockTelemetry.CaptureSnapshot();
        return snapshot with
        {
            CapturedAtUtc = capturedAtUtc,
            RuntimeClockAvailable = clock.Available,
            RuntimeTime = clock.Time,
            RuntimeDayTime = clock.DayTime,
            RuntimeMoonPhase = clock.MoonPhase,
            RuntimeSlimeRainTime = clock.SlimeRainTime,
            RuntimeDayRate = clock.DayRate
        };
    }
}
