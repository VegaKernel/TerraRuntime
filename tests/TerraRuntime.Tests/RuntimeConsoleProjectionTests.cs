using TerraRuntime.Diagnostics;
using TerraRuntime.Operations;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;
using StructuredLogLevel = TerraRuntime.Contracts.Diagnostics.RuntimeLogLevel;
using OperationsLogLevel = TerraRuntime.Operations.RuntimeLogLevel;

namespace TerraRuntime.Tests;

public sealed class RuntimeConsoleProjectionTests
{
    [Fact]
    public async Task Console_threshold_filters_only_console_delivery()
    {
        var runtimeLogs = new RuntimeLogBuffer(capacity: 16);
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var loggingOptions = new RuntimeHostLoggingOptions
        {
            MinimumLevel = StructuredLogLevel.Debug,
            ConsoleMinimumLevel = StructuredLogLevel.Warning,
            JsonLinesEnabled = false
        };
        var log = new RuntimeHostLog(runtimeLogs, standardOutput, standardError, loggingOptions);

        log.Log(
            OperationsLogLevel.Information,
            StructuredLogEventIds.LifecycleInformation,
            StructuredLogCategory.Lifecycle,
            "Server",
            "hidden-info");
        log.Log(
            OperationsLogLevel.Warning,
            StructuredLogEventIds.LifecycleWarning,
            StructuredLogCategory.Lifecycle,
            "Server",
            "visible-warning");
        log.Log(
            OperationsLogLevel.Error,
            StructuredLogEventIds.LifecycleError,
            StructuredLogCategory.Lifecycle,
            "Server",
            "visible-error",
            useStandardError: true);

        await log.DisposeAsync();

        Assert.Equal("visible-warning" + Environment.NewLine, standardOutput.ToString());
        Assert.Equal("visible-error" + Environment.NewLine, standardError.ToString());

        RuntimeLogSnapshot snapshot = runtimeLogs.CaptureSnapshot(
            OperationsLogLevel.Debug,
            maxEntries: 16);
        Assert.Equal(3, snapshot.Entries.Length);
        Assert.Contains(snapshot.Entries.ToArray(), entry => entry.Message == "hidden-info");
    }

    [Fact]
    public async Task Plain_console_chat_projection_writes_only_while_console_is_active()
    {
        string visibleMarker = $"visible-{Guid.NewGuid():N}";
        using var visibleOutput = new StringWriter();
        var visibleSink = new RuntimePlainConsoleChatSink(() => true, visibleOutput);
        using (RuntimeChatTelemetry.Subscribe(visibleSink.TryPublish))
            RuntimeChatTelemetry.Publish(4, visibleMarker);
        await visibleSink.DisposeAsync();

        Assert.Contains(
            $"[chat] #4: {visibleMarker}",
            visibleOutput.ToString(),
            StringComparison.Ordinal);

        string hiddenMarker = $"hidden-{Guid.NewGuid():N}";
        using var hiddenOutput = new StringWriter();
        var hiddenSink = new RuntimePlainConsoleChatSink(() => false, hiddenOutput);
        using (RuntimeChatTelemetry.Subscribe(hiddenSink.TryPublish))
            RuntimeChatTelemetry.Publish(5, hiddenMarker);
        await hiddenSink.DisposeAsync();

        Assert.DoesNotContain(hiddenMarker, hiddenOutput.ToString(), StringComparison.Ordinal);
    }
}
