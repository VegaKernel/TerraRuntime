namespace TerraRuntime.Operations;

/// <summary>
/// Bounded operations read model for recent runtime log events. This is not the public logging API;
/// it is a small sink that a future structured logging pipeline can feed without redirecting Console.Out.
/// </summary>
internal sealed class RuntimeLogBuffer : ILogOperations
{
    public const int DefaultCapacity = 512;
    public const int MaximumCapacity = 8_192;
    public const int MaximumSourceLength = 64;
    public const int MaximumMessageLength = 2_048;

    private readonly object gate = new();
    private readonly RuntimeLogEntry[] entries;
    private int count;
    private int nextIndex;
    private long publishedEntries;
    private long overwrittenEntries;

    public RuntimeLogBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        entries = new RuntimeLogEntry[capacity];
    }

    public int Capacity => entries.Length;

    public void Publish(RuntimeLogLevel level, string source, string message)
    {
        if (level < RuntimeLogLevel.Debug || level > RuntimeLogLevel.Error)
            throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(message);

        string boundedSource = Normalize(source, MaximumSourceLength, "Runtime");
        string boundedMessage = Normalize(message, MaximumMessageLength, string.Empty);

        lock (gate)
        {
            long sequence = ++publishedEntries;
            entries[nextIndex] = new RuntimeLogEntry(
                sequence,
                DateTimeOffset.UtcNow,
                level,
                boundedSource,
                boundedMessage);
            nextIndex = (nextIndex + 1) % entries.Length;

            if (count < entries.Length)
                count++;
            else
                overwrittenEntries++;
        }
    }

    public RuntimeLogSnapshot CaptureSnapshot(
        RuntimeLogLevel minimumLevel,
        string? source,
        int maxEntries)
    {
        if (minimumLevel < RuntimeLogLevel.Debug || minimumLevel > RuntimeLogLevel.Error)
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        if (maxEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        lock (gate)
        {
            int maximum = Math.Min(maxEntries, count);
            if (maximum == 0)
            {
                return new RuntimeLogSnapshot(
                    ReadOnlyMemory<RuntimeLogEntry>.Empty,
                    publishedEntries,
                    overwrittenEntries,
                    minimumLevel,
                    DateTimeOffset.UtcNow);
            }

            RuntimeLogEntry[] snapshot = new RuntimeLogEntry[maximum];
            int found = 0;
            for (int offset = 0; offset < count && found < maximum; offset++)
            {
                int index = nextIndex - 1 - offset;
                if (index < 0)
                    index += entries.Length;

                RuntimeLogEntry entry = entries[index];
                if (entry.Level < minimumLevel ||
                    (source is not null && !string.Equals(entry.Source, source, StringComparison.Ordinal)))
                {
                    continue;
                }

                snapshot[found++] = entry;
            }

            Array.Reverse(snapshot, 0, found);
            if (found != snapshot.Length)
                Array.Resize(ref snapshot, found);

            return new RuntimeLogSnapshot(
                snapshot.AsMemory(),
                publishedEntries,
                overwrittenEntries,
                minimumLevel,
                DateTimeOffset.UtcNow);
        }
    }

    public ReadOnlyMemory<string> CaptureSources(int maxSources)
    {
        if (maxSources < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSources));
        if (maxSources == 0)
            return ReadOnlyMemory<string>.Empty;

        lock (gate)
        {
            if (count == 0)
                return ReadOnlyMemory<string>.Empty;

            var sources = new HashSet<string>(StringComparer.Ordinal);
            int oldestIndex = count == entries.Length ? nextIndex : 0;
            for (int offset = 0; offset < count && sources.Count < maxSources; offset++)
            {
                int index = (oldestIndex + offset) % entries.Length;
                sources.Add(entries[index].Source);
            }

            string[] snapshot = sources.ToArray();
            Array.Sort(snapshot, StringComparer.Ordinal);
            return snapshot.AsMemory();
        }
    }

    private static string Normalize(string value, int maximumLength, string fallback)
    {
        if (value.Length == 0)
            return fallback;

        int length = Math.Min(value.Length, maximumLength);
        bool requiresCopy = value.Length > maximumLength;
        for (int i = 0; i < length && !requiresCopy; i++)
        {
            requiresCopy = char.IsControl(value[i]);
        }

        if (!requiresCopy)
            return value;

        char[] buffer = value.AsSpan(0, length).ToArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            if (char.IsControl(buffer[i]))
                buffer[i] = ' ';
        }

        return new string(buffer);
    }
}
