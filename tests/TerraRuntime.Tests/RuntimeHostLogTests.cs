using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeHostLogTests
{
    [Fact]
    public void Active_terminal_ui_suppresses_console_without_losing_bounded_events()
    {
        var runtimeLogs = new RuntimeLogBuffer(capacity: 8);
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var log = new RuntimeHostLog(runtimeLogs, standardOutput, standardError);

        log.Write(RuntimeLogLevel.Information, "Server", "before");
        log.SetTerminalUiActive(true);
        log.Write(RuntimeLogLevel.Warning, "Network", "hidden-warning", useStandardError: true);
        log.Publish(RuntimeLogLevel.Debug, "Runtime", "buffer-only");

        Assert.Equal("before" + Environment.NewLine, standardOutput.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.True(log.IsTerminalUiActive);
        Assert.False(log.IsPlainConsoleActive);

        RuntimeLogSnapshot snapshot = runtimeLogs.CaptureSnapshot(RuntimeLogLevel.Debug, maxEntries: 8);
        Assert.Equal(3, snapshot.Entries.Length);
        Assert.Equal("hidden-warning", snapshot.Entries.Span[1].Message);
        Assert.Equal("buffer-only", snapshot.Entries.Span[2].Message);

        log.SetTerminalUiActive(false);
        Assert.False(log.IsTerminalUiActive);
        Assert.True(log.IsPlainConsoleActive);

        log.Publish(RuntimeLogLevel.Information, "Network", "plain-publish");
        log.Write(RuntimeLogLevel.Error, "Runtime", "after", useStandardError: true);

        Assert.Equal(
            "before" + Environment.NewLine + "plain-publish" + Environment.NewLine,
            standardOutput.ToString());
        Assert.Equal("after" + Environment.NewLine, standardError.ToString());
    }
}
