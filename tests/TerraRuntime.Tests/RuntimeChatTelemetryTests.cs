using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeChatTelemetryTests
{
    [Fact]
    public void ChatTelemetry_IsProjectedOnlyThroughReservedDashboardSource()
    {
        string marker = $"chat-{Guid.NewGuid():N}";
        RuntimeChatTelemetry.Publish(7, marker);

        ILogOperations operations = new RuntimeLogBuffer(capacity: 16);
        RuntimeLogSnapshot chat = operations.CaptureSnapshot(
            OperationsLogLevel.Debug,
            source: "Chat",
            maxEntries: 32);

        Assert.Contains(
            chat.Entries.ToArray(),
            entry =>
                entry.Source == "Chat" &&
                entry.Message.Contains(marker, StringComparison.Ordinal) &&
                entry.Message.StartsWith("#7: ", StringComparison.Ordinal));

        RuntimeLogSnapshot ordinaryLogs = operations.CaptureSnapshot(OperationsLogLevel.Debug, maxEntries: 32);
        Assert.DoesNotContain(
            ordinaryLogs.Entries.ToArray(),
            entry => entry.Message.Contains(marker, StringComparison.Ordinal));
    }
}
