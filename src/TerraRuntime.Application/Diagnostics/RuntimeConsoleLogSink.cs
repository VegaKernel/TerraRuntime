using System.Text;
using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Application.Diagnostics;

internal sealed class RuntimeConsoleLogSink(TextWriter? writer = null) : IRuntimeLogSink
{
    private readonly TextWriter writer = writer ?? Console.Out;

    public string Name => "console";

    public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var line = new StringBuilder(192);
        line.Append('[').Append(record.TimestampUtc.ToString("O")).Append("] [")
            .Append(record.Level).Append("] [")
            .Append(record.EventId.Value).Append("] [")
            .Append(record.Category).Append("] [")
            .Append(record.Subsystem).Append("] ")
            .Append(record.Message);

        if (!string.IsNullOrEmpty(record.Context.CorrelationId))
            line.Append(" correlation=").Append(record.Context.CorrelationId);
        if (!string.IsNullOrEmpty(record.Context.ConnectionId))
            line.Append(" connection=").Append(record.Context.ConnectionId);
        if (!string.IsNullOrEmpty(record.Context.PlayerHandle))
            line.Append(" player=").Append(record.Context.PlayerHandle);
        if (record.Context.PacketId is int packetId)
            line.Append(" packet=").Append(packetId);

        return new ValueTask(writer.WriteLineAsync(line.ToString()));
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(writer.FlushAsync());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
