namespace TerraRuntime.Network;

public enum OutboundEnqueueResult : byte
{
    Enqueued = 0,
    FrameTooLarge = 1,
    FrameBudgetExceeded = 2,
    ByteBudgetExceeded = 3,
    Closed = 4
}
