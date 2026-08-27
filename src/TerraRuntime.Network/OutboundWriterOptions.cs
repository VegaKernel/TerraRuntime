namespace TerraRuntime.Network;

public readonly record struct OutboundWriterOptions
{
    public static OutboundWriterOptions Default { get; } = new(
        maxBatchFrames: 16,
        maxBatchBytes: 32 * 1024,
        maxBatchFrameBytes: 1024);

    public OutboundWriterOptions(int maxBatchFrames, int maxBatchBytes, int maxBatchFrameBytes)
    {
        if (maxBatchFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchFrames));
        }

        if (maxBatchBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchBytes));
        }

        if (maxBatchFrameBytes <= 0 || maxBatchFrameBytes > maxBatchBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchFrameBytes));
        }

        MaxBatchFrames = maxBatchFrames;
        MaxBatchBytes = maxBatchBytes;
        MaxBatchFrameBytes = maxBatchFrameBytes;
    }

    public int MaxBatchFrames { get; }

    public int MaxBatchBytes { get; }

    public int MaxBatchFrameBytes { get; }
}
