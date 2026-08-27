namespace TerraRuntime.Network;

public readonly record struct OutboundQueueOptions
{
    public OutboundQueueOptions(int maxFrames, long maxQueuedBytes, int maxFrameBytes)
    {
        if (maxFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        }

        if (maxQueuedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueuedBytes));
        }

        if (maxFrameBytes <= 0 || maxFrameBytes > maxQueuedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
        }

        MaxFrames = maxFrames;
        MaxQueuedBytes = maxQueuedBytes;
        MaxFrameBytes = maxFrameBytes;
    }

    public int MaxFrames { get; }

    public long MaxQueuedBytes { get; }

    public int MaxFrameBytes { get; }
}
