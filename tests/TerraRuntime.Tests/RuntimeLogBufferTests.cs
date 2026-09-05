using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Application.Operations;
using StructuredLogLevel = TerraRuntime.Contracts.Diagnostics.RuntimeLogLevel;
using OperationsLogLevel = TerraRuntime.Application.Operations.OperationsLogLevel;

namespace TerraRuntime.Tests;

public sealed class RuntimeLogBufferTests
{
    [Fact]
    public void Buffer_is_bounded_and_returns_latest_matching_entries_in_order()
    {
        var logs = new RuntimeLogBuffer(capacity: 3);
        logs.Publish(OperationsLogLevel.Debug, "Runtime", "debug-1");
        logs.Publish(OperationsLogLevel.Information, "Server", "info-2");
        logs.Publish(OperationsLogLevel.Warning, "Network", "warn-3");
        logs.Publish(OperationsLogLevel.Error, "Network", "error-4");

        RuntimeLogSnapshot snapshot = logs.CaptureSnapshot(
            OperationsLogLevel.Information,
            source: null,
            maxEntries: 2);

        Assert.Equal(4L, snapshot.PublishedEntries);
        Assert.Equal(1L, snapshot.OverwrittenEntries);
        Assert.Equal(2, snapshot.Entries.Length);
        Assert.Equal("warn-3", snapshot.Entries.Span[0].Message);
        Assert.Equal("error-4", snapshot.Entries.Span[1].Message);
    }

    [Fact]
    public void Buffer_normalizes_control_characters_and_bounds_payloads()
    {
        var logs = new RuntimeLogBuffer(capacity: 2);
        string source = new('s', RuntimeLogBuffer.MaximumSourceLength + 5);
        string message = "line1\nline2\t" + new string('x', RuntimeLogBuffer.MaximumMessageLength);

        logs.Publish(OperationsLogLevel.Information, source, message);
        RuntimeLogEntry entry = logs
            .CaptureSnapshot(OperationsLogLevel.Debug, source: null, maxEntries: 1)
            .Entries.Span[0];

        Assert.Equal(RuntimeLogBuffer.MaximumSourceLength, entry.Source.Length);
        Assert.Equal(RuntimeLogBuffer.MaximumMessageLength, entry.Message.Length);
        Assert.DoesNotContain('\n', entry.Message);
        Assert.DoesNotContain('\t', entry.Message);
    }

    [Fact]
    public void Buffer_filters_exact_sources_and_enumerates_retained_sources()
    {
        var logs = new RuntimeLogBuffer(capacity: 4);
        logs.Publish(OperationsLogLevel.Information, "Server", "server-1");
        logs.Publish(OperationsLogLevel.Warning, "Network", "network-1");
        logs.Publish(OperationsLogLevel.Error, "Server", "server-2");
        logs.Publish(OperationsLogLevel.Debug, "Runtime", "runtime-1");

        RuntimeLogSnapshot server = logs.CaptureSnapshot(
            OperationsLogLevel.Debug,
            source: "Server",
            maxEntries: 10);

        Assert.Equal(2, server.Entries.Length);
        Assert.All(server.Entries.ToArray(), entry => Assert.Equal("Server", entry.Source));
        Assert.Equal("server-1", server.Entries.Span[0].Message);
        Assert.Equal("server-2", server.Entries.Span[1].Message);
        Assert.Equal(
            new[] { "Network", "Runtime", "Server" },
            logs.CaptureSources(maxSources: 10).ToArray());
        Assert.Single(logs.CaptureSources(maxSources: 1).ToArray());
    }

    [Fact]
    public async Task Structured_sink_records_feed_the_existing_operations_read_model_without_a_second_ring()
    {
        var logs = new RuntimeLogBuffer(capacity: 4);
        await logs.WriteAsync(
            new RuntimeLogRecord(
                Sequence: 77,
                TimestampUtc: DateTimeOffset.UtcNow,
                Level: StructuredLogLevel.Warning,
                EventId: RuntimeLogEventIds.NetworkConnectionAccepted,
                Category: RuntimeLogCategory.Network,
                Subsystem: "Network",
                Message: "structured-warning",
                Context: new RuntimeLogContext(ConnectionId: "12")),
            TestContext.Current.CancellationToken);

        RuntimeLogSnapshot snapshot = logs.CaptureSnapshot(OperationsLogLevel.Debug, maxEntries: 4);
        RuntimeLogEntry entry = Assert.Single(snapshot.Entries.ToArray());
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(OperationsLogLevel.Warning, entry.Level);
        Assert.Equal("Network", entry.Source);
        Assert.Equal("structured-warning", entry.Message);
        Assert.Equal(1, snapshot.PublishedEntries);
        Assert.Equal(0, snapshot.OverwrittenEntries);
    }

    [Fact]
    public async Task Structured_records_keep_the_legacy_operations_projection_bounds()
    {
        var logs = new RuntimeLogBuffer(capacity: 2);
        string message = new('x', RuntimeLogBuffer.MaximumMessageLength + 100);

        await logs.WriteAsync(
            new RuntimeLogRecord(
                Sequence: 1,
                TimestampUtc: DateTimeOffset.UtcNow,
                Level: StructuredLogLevel.Information,
                EventId: RuntimeLogEventIds.LifecycleInformation,
                Category: RuntimeLogCategory.Lifecycle,
                Subsystem: "Server",
                Message: message,
                Context: default),
            TestContext.Current.CancellationToken);

        RuntimeLogEntry entry = Assert.Single(
            logs.CaptureSnapshot(OperationsLogLevel.Debug, maxEntries: 2).Entries.ToArray());
        Assert.Equal(RuntimeLogBuffer.MaximumMessageLength, entry.Message.Length);
    }
}
