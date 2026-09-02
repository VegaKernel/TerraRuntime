using TerraRuntime.Contracts.Diagnostics;

namespace TerraRuntime.Diagnostics;

internal sealed class RuntimeRecentLogStore : IRuntimeLogSink
{
    public const int DefaultCapacity = 512;
    public const int MaximumCapacity = 8192;

    private readonly object gate = new();
    private readonly RuntimeLogRecord[] entries;
    private int next;
    private int count;
    private long published;
    private long overwritten;

    public RuntimeRecentLogStore(int capacity = DefaultCapacity)
    {
        if (capacity < 1 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        entries = new RuntimeLogRecord[capacity];
    }

    public string Name => "recent";

    public int Capacity => entries.Length;

    public long Published => Interlocked.Read(ref published);

    public long Overwritten => Interlocked.Read(ref overwritten);

    public ValueTask WriteAsync(RuntimeLogRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            Interlocked.Increment(ref published);
            if (count == entries.Length)
                Interlocked.Increment(ref overwritten);
            else
                count++;

            entries[next] = record;
            next = (next + 1) % entries.Length;
        }

        return ValueTask.CompletedTask;
    }

    public RuntimeLogRecord[] Capture(
        RuntimeLogLevel minimumLevel = RuntimeLogLevel.Trace,
        RuntimeLogCategory? category = null,
        int maximumEntries = DefaultCapacity)
    {
        if (maximumEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));

        lock (gate)
        {
            int available = Math.Min(count, maximumEntries);
            var newest = new RuntimeLogRecord[available];
            int copied = 0;

            for (int offset = 1; offset <= count && copied < available; offset++)
            {
                int index = next - offset;
                if (index < 0)
                    index += entries.Length;

                RuntimeLogRecord entry = entries[index];
                if (entry.Level < minimumLevel || (category is not null && entry.Category != category.Value))
                    continue;

                newest[copied++] = entry;
            }

            if (copied != newest.Length)
                Array.Resize(ref newest, copied);

            Array.Reverse(newest);
            return newest;
        }
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
