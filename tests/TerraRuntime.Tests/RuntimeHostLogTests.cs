using System.Text;
using TerraRuntime.Diagnostics;
using TerraRuntime.Operations;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogContext = TerraRuntime.Contracts.Diagnostics.RuntimeLogContext;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;
using StructuredLogRecord = TerraRuntime.Contracts.Diagnostics.RuntimeLogRecord;

namespace TerraRuntime.Tests;

public sealed class RuntimeHostLogTests
{
    [Fact]
    public async Task Active_terminal_ui_suppresses_semantic_console_delivery_without_losing_bounded_events()
    {
        var runtimeLogs = new RuntimeLogBuffer(capacity: 8);
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var log = new RuntimeHostLog(runtimeLogs, standardOutput, standardError);

        log.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.LifecycleInformation,
            StructuredLogCategory.Lifecycle,
            "Server",
            "before");
        log.SetTerminalUiActive(true);
        log.Log(
            RuntimeLogLevel.Warning,
            StructuredLogEventIds.NetworkAcceptFailed,
            StructuredLogCategory.Network,
            "Network",
            "hidden-warning",
            useStandardError: true);
        log.Log(
            RuntimeLogLevel.Debug,
            StructuredLogEventIds.OperationsReadModelMessage,
            StructuredLogCategory.Operations,
            "Runtime",
            "buffer-only",
            bufferedOnly: true);

        Assert.True(log.IsTerminalUiActive);

        log.SetTerminalUiActive(false);
        Assert.False(log.IsTerminalUiActive);

        log.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.NetworkConnectionAccepted,
            StructuredLogCategory.Network,
            "Network",
            "plain-semantic");
        log.Log(
            RuntimeLogLevel.Error,
            StructuredLogEventIds.LifecycleError,
            StructuredLogCategory.Lifecycle,
            "Runtime",
            "after",
            useStandardError: true);

        await log.DisposeAsync();

        Assert.Equal(
            "before" + Environment.NewLine + "plain-semantic" + Environment.NewLine,
            standardOutput.ToString());
        Assert.Equal("after" + Environment.NewLine, standardError.ToString());

        RuntimeLogSnapshot snapshot = runtimeLogs.CaptureSnapshot(RuntimeLogLevel.Debug, maxEntries: 8);
        Assert.Equal(5, snapshot.Entries.Length);
        Assert.Equal("hidden-warning", snapshot.Entries.Span[1].Message);
        Assert.Equal("buffer-only", snapshot.Entries.Span[2].Message);
        Assert.Equal("plain-semantic", snapshot.Entries.Span[3].Message);
        Assert.Equal("after", snapshot.Entries.Span[4].Message);

        RuntimeLogPipelineMetrics metrics = log.CapturePipelineMetrics();
        Assert.Equal(5, metrics.Accepted);
        Assert.Equal(5, metrics.Drained);
        Assert.Equal(0, metrics.QueueDepth);
    }

    [Fact]
    public async Task Semantic_host_log_call_does_not_wait_for_blocked_console_io()
    {
        var runtimeLogs = new RuntimeLogBuffer(capacity: 8);
        var blockedOutput = new BlockingTextWriter();
        using var standardError = new StringWriter();
        var log = new RuntimeHostLog(
            runtimeLogs,
            blockedOutput,
            standardError,
            new RuntimeLogPipelineOptions
            {
                SinkTimeout = TimeSpan.FromSeconds(10),
                ShutdownTimeout = TimeSpan.FromSeconds(10)
            });

        Task producer = Task.Run(
            () => log.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.LifecycleInformation,
                StructuredLogCategory.Lifecycle,
                "Server",
                "non-blocking"),
            TestContext.Current.CancellationToken);

        await producer.WaitAsync(TestContext.Current.CancellationToken);
        await blockedOutput.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, log.CapturePipelineMetrics().Accepted);

        blockedOutput.Release.TrySetResult(true);
        await log.DisposeAsync();

        RuntimeLogSnapshot snapshot = runtimeLogs.CaptureSnapshot(RuntimeLogLevel.Debug, maxEntries: 8);
        Assert.Single(snapshot.Entries.ToArray());
        Assert.Equal("non-blocking", snapshot.Entries.Span[0].Message);
    }

    [Fact]
    public async Task Semantic_event_keeps_identity_context_and_delivery_separate()
    {
        var runtimeLogs = new RuntimeLogBuffer(capacity: 8);
        var recent = new RuntimeRecentLogStore(capacity: 8);
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var log = new RuntimeHostLog(
            runtimeLogs,
            standardOutput,
            standardError,
            additionalSinks: [recent],
            correlationId: "run-42");

        log.SetWorldId("world-17");
        log.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.NetworkConnectionAccepted,
            StructuredLogCategory.Network,
            "Network",
            "accepted",
            new StructuredLogContext(
                CorrelationId: "connection-7",
                ConnectionId: "7"),
            bufferedOnly: true);

        await log.DisposeAsync();

        StructuredLogRecord record = Assert.Single(recent.Capture());
        Assert.Equal(StructuredLogEventIds.NetworkConnectionAccepted, record.EventId);
        Assert.Equal(StructuredLogCategory.Network, record.Category);
        Assert.Equal("connection-7", record.Context.CorrelationId);
        Assert.Equal("world-17", record.Context.WorldId);
        Assert.Equal("7", record.Context.ConnectionId);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
    }

    private sealed class BlockingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void WriteLine(string? value)
        {
            Entered.TrySetResult(true);
            Release.Task.GetAwaiter().GetResult();
        }
    }
}
