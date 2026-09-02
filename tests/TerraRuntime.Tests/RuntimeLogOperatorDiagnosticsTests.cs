using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Diagnostics;
using TerraRuntime.Operations;
using OperationsLogLevel = TerraRuntime.Operations.RuntimeLogLevel;
using StructuredLogLevel = TerraRuntime.Contracts.Diagnostics.RuntimeLogLevel;

namespace TerraRuntime.Tests;

public sealed class RuntimeLogOperatorDiagnosticsTests
{
    [Fact]
    public async Task Structured_query_filters_by_semantic_identity_and_correlation()
    {
        var logs = new RuntimeLogBuffer(capacity: 8);
        await logs.WriteAsync(
            new RuntimeLogRecord(
                Sequence: 1,
                TimestampUtc: DateTimeOffset.UtcNow,
                Level: StructuredLogLevel.Information,
                EventId: RuntimeLogEventIds.NetworkConnectionAccepted,
                Category: RuntimeLogCategory.Network,
                Subsystem: "Network",
                Message: "accepted",
                Context: new RuntimeLogContext(CorrelationId: "connection-1")),
            TestContext.Current.CancellationToken);
        await logs.WriteAsync(
            new RuntimeLogRecord(
                Sequence: 2,
                TimestampUtc: DateTimeOffset.UtcNow,
                Level: StructuredLogLevel.Warning,
                EventId: RuntimeLogEventIds.NetworkAcceptFailed,
                Category: RuntimeLogCategory.Network,
                Subsystem: "Network",
                Message: "failed",
                Context: new RuntimeLogContext(CorrelationId: "connection-2")),
            TestContext.Current.CancellationToken);
        await logs.WriteAsync(
            new RuntimeLogRecord(
                Sequence: 3,
                TimestampUtc: DateTimeOffset.UtcNow,
                Level: StructuredLogLevel.Error,
                EventId: RuntimeLogEventIds.LifecycleError,
                Category: RuntimeLogCategory.Lifecycle,
                Subsystem: "Runtime",
                Message: "lifecycle",
                Context: new RuntimeLogContext(CorrelationId: "connection-2")),
            TestContext.Current.CancellationToken);

        RuntimeLogSnapshot snapshot = logs.CaptureSnapshot(
            new RuntimeLogQuery(
                OperationsLogLevel.Debug,
                MaxEntries: 8,
                Source: "Network",
                Category: RuntimeLogCategory.Network,
                EventId: RuntimeLogEventIds.NetworkAcceptFailed.Value,
                CorrelationId: "connection-2"));

        RuntimeLogEntry entry = Assert.Single(snapshot.Entries.ToArray());
        Assert.Equal("failed", entry.Message);
        Assert.Equal(RuntimeLogEventIds.NetworkAcceptFailed.Value, entry.EventId);
        Assert.Equal(RuntimeLogCategory.Network, entry.Category);
        Assert.Equal("connection-2", entry.CorrelationId);
    }

    [Fact]
    public async Task Operations_snapshot_exposes_live_pipeline_and_sink_health_without_second_store()
    {
        var logs = new RuntimeLogBuffer(capacity: 8);
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var host = new RuntimeHostLog(
            logs,
            standardOutput,
            standardError,
            new RuntimeHostLoggingOptions { JsonLinesEnabled = false },
            correlationId: "run-42");

        host.Log(
            OperationsLogLevel.Information,
            RuntimeLogEventIds.NetworkConnectionAccepted,
            RuntimeLogCategory.Network,
            "Network",
            "accepted",
            bufferedOnly: true);
        host.Log(
            OperationsLogLevel.Warning,
            RuntimeLogEventIds.NetworkAcceptFailed,
            RuntimeLogCategory.Network,
            "Network",
            "warning",
            bufferedOnly: true);

        await host.DisposeAsync();

        RuntimeLogSnapshot snapshot = logs.CaptureSnapshot(OperationsLogLevel.Debug, maxEntries: 8);
        Assert.Equal(2, snapshot.Diagnostics.Accepted);
        Assert.Equal(2, snapshot.Diagnostics.Drained);
        Assert.Equal(0, snapshot.Diagnostics.QueueDepth);
        Assert.True(snapshot.Diagnostics.QueueHighWaterMark >= 1);
        Assert.Equal(2, snapshot.Diagnostics.RecentPublished);
        Assert.Equal(0, snapshot.Diagnostics.RecentOverwritten);
        Assert.NotEmpty(snapshot.Diagnostics.Sinks.ToArray());
        Assert.Contains(snapshot.Diagnostics.Sinks.ToArray(), sink => sink.Name == "operations-recent");
    }
}
