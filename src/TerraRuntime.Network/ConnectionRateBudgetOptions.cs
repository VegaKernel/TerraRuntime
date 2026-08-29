namespace TerraRuntime.Network;

public readonly record struct ConnectionRateBudgetOptions
{
    public static ConnectionRateBudgetOptions AccountingOnly { get; } = new(
        window: TimeSpan.FromSeconds(1),
        maxFrames: null,
        maxBytes: null);

    /// <summary>
    /// Conservative connection-wide emergency ceiling. This is intentionally far above normal
    /// Terraria traffic and exists to bound aggregate parser/policy work even when a packet id has
    /// no dedicated gameplay-specific budget yet. Per-message limits remain the tighter first line
    /// for packet classes that can amplify work or fan out to other clients.
    /// </summary>
    public static ConnectionRateBudgetOptions HardAbuse { get; } = new(
        window: TimeSpan.FromSeconds(1),
        maxFrames: 4_096,
        maxBytes: 2L * 1024 * 1024);

    public ConnectionRateBudgetOptions(TimeSpan window, int? maxFrames, long? maxBytes)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        if (maxFrames is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        }

        if (maxBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        Window = window;
        MaxFrames = maxFrames;
        MaxBytes = maxBytes;
    }

    public TimeSpan Window { get; }

    public int? MaxFrames { get; }

    public long? MaxBytes { get; }
}
