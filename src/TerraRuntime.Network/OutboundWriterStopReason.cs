namespace TerraRuntime.Network;

public enum OutboundWriterStopReason : byte
{
    Completed = 0,
    Cancelled = 1,
    IoFailure = 2,
    QueueFailure = 3
}
