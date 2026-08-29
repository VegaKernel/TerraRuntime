using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Diagnostics;
using TerraRuntime.Operations;
using StructuredLogLevel = TerraRuntime.Contracts.Diagnostics.RuntimeLogLevel;
using OperationsLogLevel = TerraRuntime.Operations.RuntimeLogLevel;

namespace TerraRuntime;

internal sealed class RuntimeHostLog : IAsyncDisposable
{
    private readonly RuntimeLogPipeline pipeline;
    private readonly EventHandler processExitHandler;
    private int processExitRegistered = 1;
    private int terminalUiActive;
    private int terminalUiSeen;
    private int plainConsoleActive;

    public RuntimeHostLog(RuntimeLogBuffer runtimeLogs)
        : this(runtimeLogs, Console.Out, Console.Error)
    {
    }

    internal RuntimeHostLog(
        RuntimeLogBuffer runtimeLogs,
        TextWriter standardOutput,
        TextWriter standardError,
        RuntimeLogPipelineOptions? pipelineOptions = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeLogs);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        pipeline = new RuntimeLogPipeline(
            [
                new RuntimeOperationsLogSink(runtimeLogs),
                new RuntimeHostConsoleSink(standardOutput, standardError)
            ],
            pipelineOptions);

        processExitHandler = (_, _) => DisposeForProcessExit();
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;
    }

    public bool IsTerminalUiActive => Volatile.Read(ref terminalUiActive) != 0;

    public bool IsPlainConsoleActive => Volatile.Read(ref plainConsoleActive) != 0;

    internal RuntimeLogPipelineMetrics CapturePipelineMetrics() => pipeline.CaptureMetrics();

    public void SetTerminalUiActive(bool active)
    {
        if (active)
        {
            Volatile.Write(ref terminalUiSeen, 1);
            Volatile.Write(ref plainConsoleActive, 0);
            Volatile.Write(ref terminalUiActive, 1);
            return;
        }

        Volatile.Write(ref terminalUiActive, 0);
        if (Volatile.Read(ref terminalUiSeen) != 0)
            Volatile.Write(ref plainConsoleActive, 1);
    }

    public void Publish(OperationsLogLevel level, string source, string message)
    {
        RuntimeLogEventId eventId = IsPlainConsoleActive
            ? RuntimeLogEventIds.HostBridgeStandardOutput
            : RuntimeLogEventIds.HostBridgeBuffered;
        TryPublish(level, source, message, eventId);
    }

    public void Write(
        OperationsLogLevel level,
        string source,
        string message,
        bool useStandardError = false)
    {
        RuntimeLogEventId eventId = IsTerminalUiActive
            ? RuntimeLogEventIds.HostBridgeBuffered
            : useStandardError
                ? RuntimeLogEventIds.HostBridgeStandardError
                : RuntimeLogEventIds.HostBridgeStandardOutput;
        TryPublish(level, source, message, eventId);
    }

    public async ValueTask DisposeAsync()
    {
        UnregisterProcessExitHandler();
        await pipeline.DisposeAsync().ConfigureAwait(false);
    }

    private void TryPublish(
        OperationsLogLevel level,
        string source,
        string message,
        RuntimeLogEventId eventId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(message);

        pipeline.TryPublish(
            MapLevel(level),
            eventId,
            RuntimeLogCategory.Operations,
            source,
            message);
    }

    private void DisposeForProcessExit()
    {
        UnregisterProcessExitHandler();
        try
        {
            pipeline.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Process shutdown is already in progress. The pipeline has bounded its own drain/sink waits;
            // logging must never turn process exit into an unhandled failure.
        }
    }

    private void UnregisterProcessExitHandler()
    {
        if (Interlocked.Exchange(ref processExitRegistered, 0) != 0)
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
    }

    private static StructuredLogLevel MapLevel(OperationsLogLevel level) => level switch
    {
        OperationsLogLevel.Debug => StructuredLogLevel.Debug,
        OperationsLogLevel.Information => StructuredLogLevel.Information,
        OperationsLogLevel.Warning => StructuredLogLevel.Warning,
        OperationsLogLevel.Error => StructuredLogLevel.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private sealed class RuntimeOperationsLogSink(RuntimeLogBuffer runtimeLogs) : IRuntimeLogSink
    {
        public string Name => "operations-read-model";

        public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runtimeLogs.Publish(MapLevel(record.Level), record.Subsystem, record.Message);
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static OperationsLogLevel MapLevel(StructuredLogLevel level) => level switch
        {
            StructuredLogLevel.Trace or StructuredLogLevel.Debug => OperationsLogLevel.Debug,
            StructuredLogLevel.Information => OperationsLogLevel.Information,
            StructuredLogLevel.Warning => OperationsLogLevel.Warning,
            StructuredLogLevel.Error or StructuredLogLevel.Critical => OperationsLogLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
    }

    private sealed class RuntimeHostConsoleSink(
        TextWriter standardOutput,
        TextWriter standardError) : IRuntimeLogSink
    {
        public string Name => "host-console";

        public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.EventId == RuntimeLogEventIds.HostBridgeStandardOutput)
                standardOutput.WriteLine(record.Message);
            else if (record.EventId == RuntimeLogEventIds.HostBridgeStandardError)
                standardError.WriteLine(record.Message);

            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            standardOutput.Flush();
            standardError.Flush();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
