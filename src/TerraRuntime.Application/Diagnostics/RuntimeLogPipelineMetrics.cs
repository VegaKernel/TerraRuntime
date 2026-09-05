namespace TerraRuntime.Application.Diagnostics;

internal readonly record struct RuntimeLogPipelineMetrics(
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
    int QueueHighWaterMark);

internal readonly record struct RuntimeLogSinkHealth(
    string Name,
    long Failures,
    int ConsecutiveFailures,
    bool Quarantined);
