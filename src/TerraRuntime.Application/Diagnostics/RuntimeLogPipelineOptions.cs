using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Application.Diagnostics;

internal sealed record RuntimeLogPipelineOptions
{
    public const int DefaultQueueCapacity = 2048;
    public const int DefaultPriorityReserve = 256;
    public const int DefaultMaximumSubsystemLength = 64;
    public const int DefaultMaximumMessageLength = 4096;
    public const int DefaultMaximumContextLength = 128;
    public const int DefaultMaximumExceptionFieldLength = 2048;

    public RuntimeLogLevel MinimumLevel { get; init; } = RuntimeLogLevel.Trace;

    public int QueueCapacity { get; init; } = DefaultQueueCapacity;

    public int PriorityReserve { get; init; } = DefaultPriorityReserve;

    public TimeSpan SinkTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int SinkFailureThreshold { get; init; } = 3;

    public int MaximumSubsystemLength { get; init; } = DefaultMaximumSubsystemLength;

    public int MaximumMessageLength { get; init; } = DefaultMaximumMessageLength;

    public int MaximumContextLength { get; init; } = DefaultMaximumContextLength;

    public int MaximumExceptionFieldLength { get; init; } = DefaultMaximumExceptionFieldLength;

    public void Validate()
    {
        if (QueueCapacity < 2)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (PriorityReserve < 1 || PriorityReserve >= QueueCapacity)
            throw new ArgumentOutOfRangeException(nameof(PriorityReserve));
        if (SinkTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SinkTimeout));
        if (ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));
        if (SinkFailureThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(SinkFailureThreshold));
        if (MaximumSubsystemLength < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumSubsystemLength));
        if (MaximumMessageLength < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageLength));
        if (MaximumContextLength < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumContextLength));
        if (MaximumExceptionFieldLength < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumExceptionFieldLength));
    }
}
