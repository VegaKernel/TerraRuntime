using System.Collections.Concurrent;
using System.Text.Json;
using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Diagnostics;

namespace TerraRuntime.Tests;

public sealed class RuntimeLogPipelineTests
{
    [Fact]
    public async Task Shutdown_drains_accepted_records_in_fifo_order()
    {
        var sink = new RecordingSink();
        var pipeline = new RuntimeLogPipeline([sink]);

        for (int i = 0; i < 128; i++)
        {
            Assert.True(pipeline.TryPublish(
                RuntimeLogLevel.Information,
                RuntimeLogEventIds.LifecycleInformation,
                RuntimeLogCategory.Lifecycle,
                "host",
                $"message-{i}"));
        }

        await pipeline.DisposeAsync();

        RuntimeLogRecord[] records = sink.Records.ToArray();
        Assert.Equal(128, records.Length);
        Assert.Equal(Enumerable.Range(1, 128).Select(static value => (long)value), records.Select(static record => record.Sequence));
        Assert.Equal(0, pipeline.CaptureMetrics().QueueDepth);
    }

    [Fact]
    public async Task Saturation_preserves_warning_and_error_reserve_and_counts_drops()
    {
        var sink = new BlockingSink();
        var pipeline = new RuntimeLogPipeline(
            [sink],
            new RuntimeLogPipelineOptions
            {
                QueueCapacity = 8,
                PriorityReserve = 2,
                SinkTimeout = TimeSpan.FromSeconds(10),
                ShutdownTimeout = TimeSpan.FromSeconds(10)
            });

        try
        {
            Assert.True(pipeline.TryPublish(
                RuntimeLogLevel.Information,
                RuntimeLogEventIds.LifecycleInformation,
                RuntimeLogCategory.Lifecycle,
                "host",
                "first"));
            await sink.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

            int normalAccepted = 0;
            while (pipeline.TryPublish(
                       RuntimeLogLevel.Information,
                       RuntimeLogEventIds.LifecycleInformation,
                       RuntimeLogCategory.Lifecycle,
                       "host",
                       "normal"))
            {
                normalAccepted++;
            }

            Assert.Equal(6, normalAccepted);
            Assert.True(pipeline.TryPublish(
                RuntimeLogLevel.Warning,
                RuntimeLogEventIds.LifecycleWarning,
                RuntimeLogCategory.Lifecycle,
                "host",
                "warning"));
            Assert.True(pipeline.TryPublish(
                RuntimeLogLevel.Error,
                RuntimeLogEventIds.LifecycleError,
                RuntimeLogCategory.Lifecycle,
                "host",
                "error"));
            Assert.False(pipeline.TryPublish(
                RuntimeLogLevel.Critical,
                new RuntimeLogEventId(RuntimeLogEventIds.LifecycleBase + 3),
                RuntimeLogCategory.Lifecycle,
                "host",
                "full"));

            RuntimeLogPipelineMetrics metrics = pipeline.CaptureMetrics();
            Assert.Equal(1, metrics.DroppedInformation);
            Assert.Equal(1, metrics.DroppedCritical);
            Assert.Equal(8, metrics.QueueHighWaterMark);
        }
        finally
        {
            sink.Release.TrySetResult(true);
            await pipeline.DisposeAsync();
        }
    }

    [Fact]
    public async Task Repeated_sink_failure_quarantines_only_the_failed_sink()
    {
        var failing = new ThrowingSink();
        var healthy = new RecordingSink();
        var pipeline = new RuntimeLogPipeline(
            [failing, healthy],
            new RuntimeLogPipelineOptions { SinkFailureThreshold = 1 });

        Assert.True(pipeline.TryPublish(
            RuntimeLogLevel.Warning,
            new RuntimeLogEventId(RuntimeLogEventIds.SecurityBase),
            RuntimeLogCategory.Security,
            "security",
            "bounded"));
        Assert.True(pipeline.TryPublish(
            RuntimeLogLevel.Warning,
            new RuntimeLogEventId(RuntimeLogEventIds.SecurityBase + 1),
            RuntimeLogCategory.Security,
            "security",
            "still-running"));

        await pipeline.DisposeAsync();

        Assert.Equal(2, healthy.Records.Count);
        Assert.True(pipeline.CaptureMetrics().SinkFailures >= 1);
        RuntimeLogSinkHealth health = Assert.Single(
            pipeline.CaptureSinkHealth().Where(static state => state.Name == "throwing"));
        Assert.True(health.Quarantined);
    }

    [Fact]
    public async Task Jsonl_sink_rotates_retains_and_emits_parseable_structured_records()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terraruntime-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var sink = new RuntimeJsonLinesLogSink(
                directory,
                maximumFileBytes: 512,
                maximumRetainedFiles: 2,
                flushEveryRecords: 1);

            for (int i = 0; i < 12; i++)
            {
                await sink.WriteAsync(
                    new RuntimeLogRecord(
                        i + 1,
                        DateTimeOffset.UtcNow,
                        RuntimeLogLevel.Information,
                        RuntimeLogEventIds.LifecycleInformation,
                        RuntimeLogCategory.Lifecycle,
                        "host",
                        new string('x', 220),
                        new RuntimeLogContext(CorrelationId: $"corr-{i}")),
                    TestContext.Current.CancellationToken);
            }

            await sink.DisposeAsync();

            string[] files = Directory.GetFiles(directory, "runtime-*.jsonl");
            Assert.InRange(files.Length, 1, 2);
            foreach (string file in files)
            {
                foreach (string line in File.ReadLines(file))
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    Assert.True(document.RootElement.TryGetProperty("event_id", out _));
                    Assert.True(document.RootElement.TryGetProperty("category", out _));
                    Assert.True(document.RootElement.TryGetProperty("subsystem", out _));
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Recent_store_is_bounded_and_keeps_latest_matching_records()
    {
        var store = new RuntimeRecentLogStore(3);
        for (int i = 1; i <= 5; i++)
        {
            await store.WriteAsync(
                new RuntimeLogRecord(
                    i,
                    DateTimeOffset.UtcNow,
                    RuntimeLogLevel.Information,
                    new RuntimeLogEventId(RuntimeLogEventIds.OperationsBase),
                    RuntimeLogCategory.Operations,
                    "ops",
                    $"m{i}",
                    default),
                TestContext.Current.CancellationToken);
        }

        RuntimeLogRecord[] records = store.Capture(maximumEntries: 3);
        Assert.Equal([3L, 4L, 5L], records.Select(static record => record.Sequence).ToArray());
        Assert.Equal(2, store.Overwritten);
    }

    [Fact]
    public async Task Producer_bounds_and_sanitizes_free_form_text_before_queueing()
    {
        var sink = new RecordingSink();
        var pipeline = new RuntimeLogPipeline(
            [sink],
            new RuntimeLogPipelineOptions
            {
                MaximumSubsystemLength = 4,
                MaximumMessageLength = 8,
                MaximumContextLength = 5
            });

        Assert.True(pipeline.TryPublish(
            RuntimeLogLevel.Information,
            RuntimeLogEventIds.LifecycleInformation,
            RuntimeLogCategory.Lifecycle,
            "ab\ncdef",
            "12\r34567890",
            new RuntimeLogContext(ConnectionId: "123456789")));

        await pipeline.DisposeAsync();

        RuntimeLogRecord record = Assert.Single(sink.Records);
        Assert.Equal("ab c", record.Subsystem);
        Assert.Equal("12 34567", record.Message);
        Assert.Equal("12345", record.Context.ConnectionId);
    }

    private sealed class RecordingSink : IRuntimeLogSink
    {
        public string Name => "recording";

        public ConcurrentQueue<RuntimeLogRecord> Records { get; } = new();

        public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
        {
            Records.Enqueue(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingSink : IRuntimeLogSink
    {
        public string Name => "blocking";

        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSink : IRuntimeLogSink
    {
        public string Name => "throwing";

        public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("expected test failure"));

        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
