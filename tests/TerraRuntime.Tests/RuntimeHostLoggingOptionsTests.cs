using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Diagnostics;

namespace TerraRuntime.Tests;

public sealed class RuntimeHostLoggingOptionsTests
{
    [Fact]
    public void Production_console_defaults_to_error_and_critical_without_narrowing_other_sinks()
    {
        var options = new RuntimeHostLoggingOptions();

        Assert.Equal(RuntimeLogLevel.Debug, options.MinimumLevel);
        Assert.Equal(RuntimeLogLevel.Error, options.ConsoleMinimumLevel);
    }

    [Fact]
    public void Environment_configuration_is_bounded_and_normalizes_priority_reserve()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TERRARUNTIME_LOG_LEVEL"] = "Debug",
            ["TERRARUNTIME_LOG_CONSOLE_LEVEL"] = "Warning",
            ["TERRARUNTIME_LOG_QUEUE_CAPACITY"] = "16",
            ["TERRARUNTIME_LOG_PRIORITY_RESERVE"] = "99",
            ["TERRARUNTIME_LOG_CONSOLE"] = "off",
            ["TERRARUNTIME_LOG_JSONL"] = "yes",
            ["TERRARUNTIME_LOG_RETAINED_FILES"] = "12",
            ["TERRARUNTIME_LOG_FLUSH_RECORDS"] = "7",
            ["TERRARUNTIME_LOG_SINK_TIMEOUT_MS"] = "250",
            ["TERRARUNTIME_LOG_SHUTDOWN_TIMEOUT_MS"] = "900"
        };

        RuntimeHostLoggingOptions options = RuntimeHostLoggingOptions.FromEnvironment(
            name => values.GetValueOrDefault(name));

        Assert.Equal(RuntimeLogLevel.Debug, options.MinimumLevel);
        Assert.Equal(RuntimeLogLevel.Warning, options.ConsoleMinimumLevel);
        Assert.Equal(16, options.QueueCapacity);
        Assert.Equal(15, options.PriorityReserve);
        Assert.False(options.ConsoleEnabled);
        Assert.True(options.JsonLinesEnabled);
        Assert.Equal(12, options.MaximumRetainedFiles);
        Assert.Equal(7, options.FlushEveryRecords);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.SinkTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(900), options.ShutdownTimeout);
        options.ToPipelineOptions().Validate();
    }

    [Fact]
    public void Malformed_environment_values_fall_back_to_safe_defaults()
    {
        RuntimeHostLoggingOptions defaults = new();
        RuntimeHostLoggingOptions options = RuntimeHostLoggingOptions.FromEnvironment(
            _ => "definitely-not-a-valid-value");

        Assert.Equal(defaults.MinimumLevel, options.MinimumLevel);
        Assert.Equal(defaults.ConsoleMinimumLevel, options.ConsoleMinimumLevel);
        Assert.Equal(defaults.QueueCapacity, options.QueueCapacity);
        Assert.Equal(defaults.PriorityReserve, options.PriorityReserve);
        Assert.Equal(defaults.ConsoleEnabled, options.ConsoleEnabled);
        Assert.Equal(defaults.JsonLinesEnabled, options.JsonLinesEnabled);
        Assert.Equal(defaults.MaximumFileBytes, options.MaximumFileBytes);
        Assert.Equal(defaults.MaximumRetainedFiles, options.MaximumRetainedFiles);
    }
}
