namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>
/// Asynchronous consumer of immutable runtime records. Sinks are called only by the runtime drain worker,
/// never directly by authoritative simulation producers.
/// </summary>
public interface IRuntimeLogSink : IAsyncDisposable
{
    string Name { get; }

    ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken);

    ValueTask FlushAsync(CancellationToken cancellationToken);
}
