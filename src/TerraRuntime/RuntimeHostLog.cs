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
    private readonly string correlationId;
    private readonly RuntimePlainConsoleChatSink? plainConsoleChatSink;
    private readonly IDisposable? plainConsoleChatSubscription;
    private string? worldId;
    private int processExitRegistered = 1;
    private int terminalUiActive;

    public RuntimeHostLog(RuntimeLogBuffer runtimeLogs)
        : this(
            runtimeLogs,
            Console.Out,
            Console.Error,
            RuntimeHostLoggingOptions.FromEnvironment(),
            additionalSinks: null,
            correlationId: null,
            enablePlainConsoleChat: true)
    {
    }

    internal RuntimeHostLog(
        RuntimeLogBuffer runtimeLogs,
        TextWriter standardOutput,
        TextWriter standardError,
        RuntimeLogPipelineOptions? pipelineOptions = null,
        IReadOnlyList<IRuntimeLogSink>? additionalSinks = null,
        string? correlationId = null)
        : this(
            runtimeLogs,
            standardOutput,
            standardError,
            RuntimeHostLoggingOptions.ForCompatibilityTests(pipelineOptions),
            additionalSinks,
            correlationId,
            enablePlainConsoleChat: false)
    {
    }

    internal RuntimeHostLog(
        RuntimeLogBuffer runtimeLogs,
        TextWriter standardOutput,
        TextWriter standardError,
        RuntimeHostLoggingOptions loggingOptions)
        : this(
            runtimeLogs,
            standardOutput,
            standardError,
            loggingOptions,
            additionalSinks: null,
            correlationId: null,
            enablePlainConsoleChat: false)
    {
    }

    private RuntimeHostLog(
        RuntimeLogBuffer runtimeLogs,
        TextWriter standardOutput,
        TextWriter standardError,
        RuntimeHostLoggingOptions loggingOptions,
        IReadOnlyList<IRuntimeLogSink>? additionalSinks,
        string? correlationId,
        bool enablePlainConsoleChat)
    {
        ArgumentNullException.ThrowIfNull(runtimeLogs);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(loggingOptions);

        this.correlationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var sinks = new List<IRuntimeLogSink>(3 + (additionalSinks?.Count ?? 0))
        {
            runtimeLogs
        };

        if (loggingOptions.ConsoleEnabled)
        {
            sinks.Add(new RuntimeHostConsoleSink(
                standardOutput,
                standardError,
                loggingOptions.ConsoleMinimumLevel));
        }

        if (loggingOptions.JsonLinesEnabled)
        {
            sinks.Add(new RuntimeJsonLinesLogSink(
                loggingOptions.JsonLinesDirectory,
                maximumFileBytes: loggingOptions.MaximumFileBytes,
                maximumRetainedFiles: loggingOptions.MaximumRetainedFiles,
                flushEveryRecords: loggingOptions.FlushEveryRecords));
        }

        if (additionalSinks is not null)
            sinks.AddRange(additionalSinks);

        pipeline = new RuntimeLogPipeline(sinks, loggingOptions.ToPipelineOptions());
        runtimeLogs.AttachPipelineDiagnostics(pipeline.CaptureMetrics, pipeline.CaptureSinkHealth);

        if (enablePlainConsoleChat)
        {
            plainConsoleChatSink = new RuntimePlainConsoleChatSink(
                () => IsPlainConsoleActive,
                standardOutput);
            plainConsoleChatSubscription = RuntimeChatTelemetry.Subscribe(plainConsoleChatSink.TryPublish);
        }

        processExitHandler = (_, _) => DisposeForProcessExit();
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;
    }

    public bool IsTerminalUiActive => Volatile.Read(ref terminalUiActive) != 0;

    // Plain-console routing is the complement of terminal-UI routing. Keeping this derived query avoids
    // parallel mutable state and lets logging/chat projection share one terminal-ownership contract.
    public bool IsPlainConsoleActive => !IsTerminalUiActive;

    internal RuntimeLogPipelineMetrics CapturePipelineMetrics() => pipeline.CaptureMetrics();

    public void SetTerminalUiActive(bool active) =>
        Volatile.Write(ref terminalUiActive, active ? 1 : 0);

    public void SetWorldId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Volatile.Write(ref worldId, value);
    }

    /// <summary>
    /// Publishes one semantic host event. Event identity/category remain independent from the local
    /// stdout/stderr delivery hint carried beside the record inside the bounded pipeline.
    /// </summary>
    public void Log(
        OperationsLogLevel level,
        RuntimeLogEventId eventId,
        RuntimeLogCategory category,
        string source,
        string message,
        RuntimeLogContext context = default,
        bool useStandardError = false,
        bool bufferedOnly = false)
    {
        RuntimeLogDelivery delivery = bufferedOnly || IsTerminalUiActive
            ? RuntimeLogDelivery.Buffered
            : useStandardError
                ? RuntimeLogDelivery.StandardError
                : RuntimeLogDelivery.StandardOutput;

        TryPublish(level, source, message, eventId, category, MergeContext(context), delivery);
    }

    public async ValueTask DisposeAsync()
    {
        UnregisterProcessExitHandler();
        plainConsoleChatSubscription?.Dispose();
        if (plainConsoleChatSink is not null)
            await plainConsoleChatSink.DisposeAsync().ConfigureAwait(false);
        await pipeline.DisposeAsync().ConfigureAwait(false);
    }

    private RuntimeLogContext MergeContext(RuntimeLogContext context) =>
        context with
        {
            CorrelationId = context.CorrelationId ?? correlationId,
            WorldId = context.WorldId ?? Volatile.Read(ref worldId)
        };

    private void TryPublish(
        OperationsLogLevel level,
        string source,
        string message,
        RuntimeLogEventId eventId,
        RuntimeLogCategory category,
        RuntimeLogContext context,
        RuntimeLogDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(message);

        pipeline.TryPublish(
            MapLevel(level),
            eventId,
            category,
            source,
            message,
            context,
            delivery: delivery);
    }

    private void DisposeForProcessExit()
    {
        UnregisterProcessExitHandler();
        plainConsoleChatSubscription?.Dispose();
        try
        {
            if (plainConsoleChatSink is not null)
                plainConsoleChatSink.DisposeAsync().AsTask().GetAwaiter().GetResult();
            pipeline.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Process shutdown is already in progress. Both output paths bound their own waits;
            // observability must never turn process exit into an unhandled failure.
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

    private sealed class RuntimeHostConsoleSink(
        TextWriter standardOutput,
        TextWriter standardError,
        StructuredLogLevel minimumLevel) : IRuntimeLogDeliverySink
    {
        public string Name => "host-console";

        public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken) =>
            WriteAsync(record, RuntimeLogDelivery.Buffered, cancellationToken);

        public ValueTask WriteAsync(
            RuntimeLogRecord record,
            RuntimeLogDelivery delivery,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.Level < minimumLevel)
                return ValueTask.CompletedTask;

            if (delivery == RuntimeLogDelivery.StandardOutput)
                standardOutput.WriteLine(record.Message);
            else if (delivery == RuntimeLogDelivery.StandardError)
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
