using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Diagnostics;

internal sealed record RuntimeHostLoggingOptions
{
    public RuntimeLogLevel MinimumLevel { get; init; } = RuntimeLogLevel.Debug;
    public RuntimeLogLevel ConsoleMinimumLevel { get; init; } = RuntimeLogLevel.Error;
    public int QueueCapacity { get; init; } = RuntimeLogPipelineOptions.DefaultQueueCapacity;
    public int PriorityReserve { get; init; } = RuntimeLogPipelineOptions.DefaultPriorityReserve;
    public bool ConsoleEnabled { get; init; } = true;
    public bool JsonLinesEnabled { get; init; } = true;
    public string JsonLinesDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "logs");
    public long MaximumFileBytes { get; init; } = RuntimeJsonLinesLogSink.DefaultMaximumFileBytes;
    public int MaximumRetainedFiles { get; init; } = RuntimeJsonLinesLogSink.DefaultMaximumRetainedFiles;
    public int FlushEveryRecords { get; init; } = RuntimeJsonLinesLogSink.DefaultFlushEveryRecords;
    public TimeSpan SinkTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public static RuntimeHostLoggingOptions FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var defaults = new RuntimeHostLoggingOptions();
        return (defaults with
        {
            MinimumLevel = ReadLevel(read("TERRARUNTIME_LOG_LEVEL"), defaults.MinimumLevel),
            ConsoleMinimumLevel = ReadLevel(read("TERRARUNTIME_LOG_CONSOLE_LEVEL"), defaults.ConsoleMinimumLevel),
            QueueCapacity = ReadInt(read("TERRARUNTIME_LOG_QUEUE_CAPACITY"), 2, 1_048_576, defaults.QueueCapacity),
            PriorityReserve = ReadInt(read("TERRARUNTIME_LOG_PRIORITY_RESERVE"), 1, 1_048_575, defaults.PriorityReserve),
            ConsoleEnabled = ReadBool(read("TERRARUNTIME_LOG_CONSOLE"), defaults.ConsoleEnabled),
            JsonLinesEnabled = ReadBool(read("TERRARUNTIME_LOG_JSONL"), defaults.JsonLinesEnabled),
            JsonLinesDirectory = ReadPath(read("TERRARUNTIME_LOG_DIRECTORY"), defaults.JsonLinesDirectory),
            MaximumFileBytes = ReadLong(read("TERRARUNTIME_LOG_MAX_FILE_BYTES"), 256, 1L << 40, defaults.MaximumFileBytes),
            MaximumRetainedFiles = ReadInt(read("TERRARUNTIME_LOG_RETAINED_FILES"), 1, 4096, defaults.MaximumRetainedFiles),
            FlushEveryRecords = ReadInt(read("TERRARUNTIME_LOG_FLUSH_RECORDS"), 1, 1_000_000, defaults.FlushEveryRecords),
            SinkTimeout = TimeSpan.FromMilliseconds(ReadInt(read("TERRARUNTIME_LOG_SINK_TIMEOUT_MS"), 1, 60_000, 2000)),
            ShutdownTimeout = TimeSpan.FromMilliseconds(ReadInt(read("TERRARUNTIME_LOG_SHUTDOWN_TIMEOUT_MS"), 1, 120_000, 5000))
        }).NormalizeQueueReserve();
    }

    public RuntimeLogPipelineOptions ToPipelineOptions() => new()
    {
        MinimumLevel = MinimumLevel,
        QueueCapacity = QueueCapacity,
        PriorityReserve = PriorityReserve,
        SinkTimeout = SinkTimeout,
        ShutdownTimeout = ShutdownTimeout
    };

    private RuntimeHostLoggingOptions NormalizeQueueReserve()
    {
        int reserve = Math.Clamp(PriorityReserve, 1, QueueCapacity - 1);
        return reserve == PriorityReserve ? this : this with { PriorityReserve = reserve };
    }

    private static RuntimeLogLevel ReadLevel(string? value, RuntimeLogLevel fallback) =>
        Enum.TryParse(value, ignoreCase: true, out RuntimeLogLevel parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static bool ReadBool(string? value, bool fallback)
    {
        if (string.Equals(value, "1", StringComparison.Ordinal) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(value, "0", StringComparison.Ordinal) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            return false;
        return fallback;
    }

    private static int ReadInt(string? value, int minimum, int maximum, int fallback) =>
        int.TryParse(value, out int parsed) && parsed >= minimum && parsed <= maximum ? parsed : fallback;

    private static long ReadLong(string? value, long minimum, long maximum, long fallback) =>
        long.TryParse(value, out long parsed) && parsed >= minimum && parsed <= maximum ? parsed : fallback;

    private static string ReadPath(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return fallback;
        }
    }
}
