namespace TerraRuntime.Network;

public enum TerrariaPipePumpResult : byte
{
    Completed = 0,
    SinkStopped = 1,
    Cancelled = 2,
    TruncatedFrame = 3,
    InvalidFrameLength = 4,
    FrameTooLarge = 5
}
