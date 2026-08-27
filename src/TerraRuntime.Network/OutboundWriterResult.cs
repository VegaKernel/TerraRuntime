namespace TerraRuntime.Network;

public readonly record struct OutboundWriterResult(
    OutboundWriterStopReason Reason,
    long FramesWritten,
    long BytesWritten,
    Exception? Error = null);
