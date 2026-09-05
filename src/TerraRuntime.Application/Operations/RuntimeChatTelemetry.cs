namespace TerraRuntime.Application.Operations;

internal readonly record struct RuntimeChatEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    byte PlayerSlot,
    string Text);

/// <summary>
/// Bounded operator read model for public vanilla chat. This is telemetry only: it never owns chat
/// routing, command handling or authoritative player state. Console observers are notification-only
/// and must stay non-blocking; authoritative relay success never depends on them.
/// </summary>
internal static class RuntimeChatTelemetry
{
    private const int Capacity = 256;
    private const int MaximumTextLength = 512;

    private static readonly object Gate = new();
    private static readonly RuntimeChatEntry[] Entries = new RuntimeChatEntry[Capacity];
    private static Action<RuntimeChatEntry>? observers;
    private static int count;
    private static int nextIndex;
    private static long sequence;

    public static void Publish(byte playerSlot, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalized = Normalize(text);
        if (normalized.Length == 0)
            return;

        RuntimeChatEntry entry;
        Action<RuntimeChatEntry>? observerSnapshot;
        lock (Gate)
        {
            entry = new RuntimeChatEntry(
                ++sequence,
                DateTimeOffset.UtcNow,
                playerSlot,
                normalized);
            Entries[nextIndex] = entry;
            nextIndex = (nextIndex + 1) % Entries.Length;
            if (count < Entries.Length)
                count++;
            observerSnapshot = observers;
        }

        if (observerSnapshot is null)
            return;

        try
        {
            observerSnapshot(entry);
        }
        catch (Exception)
        {
            // Operator projections must never interfere with authoritative chat routing.
        }
    }

    public static ReadOnlyMemory<RuntimeChatEntry> Capture(int maxEntries)
    {
        if (maxEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        lock (Gate)
        {
            int take = Math.Min(maxEntries, count);
            if (take == 0)
                return ReadOnlyMemory<RuntimeChatEntry>.Empty;

            var snapshot = new RuntimeChatEntry[take];
            int first = (nextIndex - take + Entries.Length) % Entries.Length;
            for (int i = 0; i < take; i++)
                snapshot[i] = Entries[(first + i) % Entries.Length];
            return snapshot.AsMemory();
        }
    }

    internal static IDisposable Subscribe(Action<RuntimeChatEntry> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (Gate)
            observers += observer;
        return new Subscription(observer);
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            Array.Clear(Entries);
            count = 0;
            nextIndex = 0;
            sequence = 0;
        }
    }

    private static void Unsubscribe(Action<RuntimeChatEntry> observer)
    {
        lock (Gate)
            observers -= observer;
    }

    private static string Normalize(string text)
    {
        ReadOnlySpan<char> source = text.AsSpan(0, Math.Min(text.Length, MaximumTextLength));
        char[]? copy = null;
        for (int i = 0; i < source.Length; i++)
        {
            if (!char.IsControl(source[i]))
                continue;

            copy ??= source.ToArray();
            copy[i] = ' ';
        }

        return copy is null ? source.ToString() : new string(copy);
    }

    private sealed class Subscription(Action<RuntimeChatEntry> observer) : IDisposable
    {
        private Action<RuntimeChatEntry>? observer = observer;

        public void Dispose()
        {
            Action<RuntimeChatEntry>? current = Interlocked.Exchange(ref observer, null);
            if (current is not null)
                Unsubscribe(current);
        }
    }
}
