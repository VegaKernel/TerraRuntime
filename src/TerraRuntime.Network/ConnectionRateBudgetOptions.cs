namespace TerraRuntime.Network;

public readonly record struct ConnectionRateBudgetOptions
{
    public static ConnectionRateBudgetOptions AccountingOnly { get; } = new(
        window: TimeSpan.FromSeconds(1),
        maxFrames: null,
        maxBytes: null);

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
