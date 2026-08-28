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

        RuntimeLogSnapshot snapshot = logs.CaptureSnapshot(
            RuntimeLogLevel.Information,
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

        logs.Publish(RuntimeLogLevel.Information, source, message);
        RuntimeLogEntry entry = logs
            .CaptureSnapshot(RuntimeLogLevel.Debug, source: null, maxEntries: 1)
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
        logs.Publish(RuntimeLogLevel.Information, "Server", "server-1");
        logs.Publish(RuntimeLogLevel.Warning, "Network", "network-1");
        logs.Publish(RuntimeLogLevel.Error, "Server", "server-2");
        logs.Publish(RuntimeLogLevel.Debug, "Runtime", "runtime-1");

        RuntimeLogSnapshot server = logs.CaptureSnapshot(
            RuntimeLogLevel.Debug,
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
}
