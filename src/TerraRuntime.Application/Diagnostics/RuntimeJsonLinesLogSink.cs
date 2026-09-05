using System.Buffers;
using System.Text.Json;
using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Application.Diagnostics;

internal sealed class RuntimeJsonLinesLogSink : IRuntimeLogSink
{
    public const long DefaultMaximumFileBytes = 16L * 1024L * 1024L;
    public const int DefaultMaximumRetainedFiles = 8;
    public const int DefaultFlushEveryRecords = 64;

    private static readonly ReadOnlyMemory<byte> NewLine = new byte[] { (byte)'\n' };

    private readonly string directory;
    private readonly string prefix;
    private readonly long maximumFileBytes;
    private readonly int maximumRetainedFiles;
    private readonly int flushEveryRecords;
    private FileStream? stream;
    private DateOnly openedDayUtc;
    private int fileOrdinal;
    private int recordsSinceFlush;

    public RuntimeJsonLinesLogSink(
        string directory,
        string prefix = "runtime",
        long maximumFileBytes = DefaultMaximumFileBytes,
        int maximumRetainedFiles = DefaultMaximumRetainedFiles,
        int flushEveryRecords = DefaultFlushEveryRecords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (maximumFileBytes < 256)
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        if (maximumRetainedFiles < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedFiles));
        if (flushEveryRecords < 1)
            throw new ArgumentOutOfRangeException(nameof(flushEveryRecords));

        this.directory = Path.GetFullPath(directory);
        this.prefix = SanitizePrefix(prefix);
        this.maximumFileBytes = maximumFileBytes;
        this.maximumRetainedFiles = maximumRetainedFiles;
        this.flushEveryRecords = flushEveryRecords;
    }

    public string Name => "jsonl";

    public async ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("sequence", record.Sequence);
            json.WriteString("timestamp_utc", record.TimestampUtc);
            json.WriteString("level", record.Level.ToString());
            json.WriteNumber("event_id", record.EventId.Value);
            json.WriteString("category", record.Category.ToString());
            json.WriteString("subsystem", record.Subsystem);
            json.WriteString("message", record.Message);

            WriteContext(json, record.Context);

            if (!string.IsNullOrEmpty(record.ExceptionType))
                json.WriteString("exception_type", record.ExceptionType);
            if (!string.IsNullOrEmpty(record.ExceptionMessage))
                json.WriteString("exception_message", record.ExceptionMessage);

            json.WriteEndObject();
            json.Flush();
        }

        int requiredBytes = checked(buffer.WrittenCount + 1);
        await EnsureWritableStreamAsync(requiredBytes, record.TimestampUtc.UtcDateTime, cancellationToken)
            .ConfigureAwait(false);

        FileStream current = stream!;
        await current.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await current.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);

        recordsSinceFlush++;
        if (recordsSinceFlush >= flushEveryRecords || record.Level >= RuntimeLogLevel.Error)
            await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (stream is null)
            return;

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        recordsSinceFlush = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (stream is null)
            return;

        await stream.FlushAsync().ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
        stream = null;
    }

    private async ValueTask EnsureWritableStreamAsync(
        int requiredBytes,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        DateOnly eventDay = DateOnly.FromDateTime(timestampUtc);
        if (stream is not null &&
            openedDayUtc == eventDay &&
            stream.Length + requiredBytes <= maximumFileBytes)
        {
            return;
        }

        if (stream is not null)
        {
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
            stream = null;
        }

        Directory.CreateDirectory(directory);

        while (true)
        {
            string name =
                $"{prefix}-{timestampUtc:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}-{fileOrdinal++:D4}.jsonl";
            string path = Path.Combine(directory, name);
            try
            {
                stream = new FileStream(
                    path,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Write,
                        Mode = FileMode.CreateNew,
                        Share = FileShare.Read,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                    });
                break;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        openedDayUtc = eventDay;
        recordsSinceFlush = 0;
        ApplyRetention();
    }

    private void ApplyRetention()
    {
        string[] files = Directory
            .EnumerateFiles(directory, $"{prefix}-*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static path => path, StringComparer.Ordinal)
            .ToArray();

        for (int i = maximumRetainedFiles; i < files.Length; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void WriteContext(Utf8JsonWriter json, RuntimeLogContext context)
    {
        if (string.IsNullOrEmpty(context.CorrelationId) &&
            string.IsNullOrEmpty(context.WorldId) &&
            string.IsNullOrEmpty(context.ConnectionId) &&
            string.IsNullOrEmpty(context.PlayerHandle) &&
            string.IsNullOrEmpty(context.EntityHandle) &&
            string.IsNullOrEmpty(context.PacketDirection) &&
            context.PacketId is null)
        {
            return;
        }

        json.WriteStartObject("context");
        WriteOptional(json, "correlation_id", context.CorrelationId);
        WriteOptional(json, "world_id", context.WorldId);
        WriteOptional(json, "connection_id", context.ConnectionId);
        WriteOptional(json, "player_handle", context.PlayerHandle);
        WriteOptional(json, "entity_handle", context.EntityHandle);
        WriteOptional(json, "packet_direction", context.PacketDirection);
        if (context.PacketId is int packetId)
            json.WriteNumber("packet_id", packetId);
        json.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter json, string propertyName, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            json.WriteString(propertyName, value);
    }

    private static string SanitizePrefix(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Create(value.Length, (value, invalid), static (destination, state) =>
        {
            for (int i = 0; i < state.value.Length; i++)
                destination[i] = state.invalid.Contains(state.value[i]) ? '_' : state.value[i];
        });
    }
}
