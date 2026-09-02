using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Diagnostics;

/// <summary>
/// Host-local delivery hint carried beside a structured record inside the bounded pipeline.
/// It is deliberately not part of RuntimeLogRecord: semantic identity and sink routing are separate concerns.
/// </summary>
internal enum RuntimeLogDelivery : byte
{
    Buffered = 0,
    StandardOutput = 1,
    StandardError = 2
}

/// <summary>
/// Optional internal sink capability for consumers that need the host-local delivery hint.
/// Ordinary structured sinks continue to receive only RuntimeLogRecord.
/// </summary>
internal interface IRuntimeLogDeliverySink : IRuntimeLogSink
{
    ValueTask WriteAsync(
        RuntimeLogRecord record,
        RuntimeLogDelivery delivery,
        CancellationToken cancellationToken);
}
