namespace TerraRuntime.Network;

public readonly record struct ConnectionRateSnapshot(
    long TotalFrames,
    long TotalBytes,
    long RejectedFrames);
