using System.Text;
using TerraRuntime.Diagnostics;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeHostLogTests
{
    [Fact]
    public async Task Active_terminal_ui_suppresses_console_without_losing_bounded_events()
    {
        var runtimeLogs = new RuntimeLogBuffer(capacity: 8);
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var log = new RuntimeHostLog(runtimeLogs, standardOutput, standardError);

        log.Write(RuntimeLogLevel.Information, "Server", "before");
        log.SetTerminalUiActive(true);
        log.Write(RuntimeLogLevel.Warning, "Network", "hidden-warning", useStandardError: true);
        log.Publish(RuntimeLogLevel.Debug, "Runtime", "buffer-only");

        Assert.True(log.IsTerminalUiActive);
        Assert.False(log.IsPlainConsoleActive);

        log.SetTerminalUiActive(false);
        Assert.False(log.IsTerminalUiActive);
        Assert.True(log.IsPlainConsoleActive);

        log.Publish(RuntimeLogLevel.Information, "Network", "plain-publish");
        log.Write(RuntimeLogLevel.Error, "Runtime", "after", useStandardError: true);

        await log.DisposeAsync();

        Assert.Equal(
            "before" + Environment.NewLine + "plain-publish" + Environment.NewLine,
            standardOutput.ToString());
        Assert.Equal("after" + Environment.NewLine, standardError.ToString());

        RuntimeLogSnapshot snapshot = runtimeLogs.CaptureSnapshot(RuntimeLogLevel.Debug, maxEntries: 8);
        Assert.Equal(5, snapshot.Entries.Length);
        Assert.Equal("hidden-warning", snapshot.Entries.Span[1].Message);
        Assert.Equal("buffer-only", snapshot.Entries.Span[2].Message);
        Assert.Equal("plain-publish", snapshot.Entries.Span[3].Message);
        Assert.Equal("after", snapshot.Entries.Span[4].Message);

        RuntimeLogPipelineMetrics metrics = log.CapturePipelineMetrics();
        Assert.Equal(5, metrics.Accepted);
        Assert.Equal(5, metrics.Drained);
        Assert.Equal(0, metrics.QueueDepth);
    }

    [Fact]
    public async Task Host_log_call_does_not_wait_for_blocked_console_io()
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

        Task producer = Task.Run(() =>
            log.Write(RuntimeLogLevel.Information, "Server", "non-blocking"));

        await producer.WaitAsync(TestContext.Current.CancellationToken);
        await blockedOutput.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, log.CapturePipelineMetrics().Accepted);

        blockedOutput.Release.TrySetResult(true);
        await log.DisposeAsync();

        RuntimeLogSnapshot snapshot = runtimeLogs.CaptureSnapshot(RuntimeLogLevel.Debug, maxEntries: 8);
        Assert.Single(snapshot.Entries.ToArray());
        Assert.Equal("non-blocking", snapshot.Entries.Span[0].Message);
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
