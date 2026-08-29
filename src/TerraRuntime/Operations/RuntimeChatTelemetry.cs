namespace TerraRuntime.Operations;

internal readonly record struct RuntimeChatEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    byte PlayerSlot,
    string Text);

/// <summary>
/// Bounded operator read model for public vanilla chat. This is telemetry only: it never owns chat
/// routing, command handling or authoritative player state.
/// </summary>
internal static class RuntimeChatTelemetry
{
    private const int Capacity = 256;
    private const int MaximumTextLength = 512;

    private static readonly object Gate = new();
    private static readonly RuntimeChatEntry[] Entries = new RuntimeChatEntry[Capacity];
    private static int count;
    private static int nextIndex;
    private static long sequence;

    public static void Publish(byte playerSlot, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalized = Normalize(text);
        if (normalized.Length == 0)
            return;

        lock (Gate)
        {
            Entries[nextIndex] = new RuntimeChatEntry(
                ++sequence,
                DateTimeOffset.UtcNow,
                playerSlot,
                normalized);
            nextIndex = (nextIndex + 1) % Entries.Length;
            if (count < Entries.Length)
                count++;
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
}
