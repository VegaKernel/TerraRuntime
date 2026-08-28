using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeLogBufferTests
{
    [Fact]
    public void Buffer_is_bounded_and_returns_latest_matching_entries_in_order()
    {
        var logs = new RuntimeLogBuffer(capacity: 3);
        logs.Publish(RuntimeLogLevel.Debug, "Runtime", "debug-1");
        logs.Publish(RuntimeLogLevel.Information, "Server", "info-2");
        logs.Publish(RuntimeLogLevel.Warning, "Network", "warn-3");
        logs.Publish(RuntimeLogLevel.Error, "Network", "error-4");

        RuntimeLogSnapshot snapshot = logs.CaptureSnapshot(RuntimeLogLevel.Information, maxEntries: 2);

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

        logs.Publish(RuntimeLogLevel.Information, source, message);
        RuntimeLogEntry entry = logs
            .CaptureSnapshot(RuntimeLogLevel.Debug, maxEntries: 1)
            .Entries.Span[0];

        Assert.Equal(RuntimeLogBuffer.MaximumSourceLength, entry.Source.Length);
        Assert.Equal(RuntimeLogBuffer.MaximumMessageLength, entry.Message.Length);
        Assert.DoesNotContain('\n', entry.Message);
        Assert.DoesNotContain('\t', entry.Message);
    }
}
