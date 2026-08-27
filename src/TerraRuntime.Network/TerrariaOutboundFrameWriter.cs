using System.Threading.Channels;

namespace TerraRuntime.Network;

public static class TerrariaOutboundFrameWriter
{
    public static async ValueTask<OutboundWriterResult> RunAsync(
        Stream stream,
        BoundedOutboundQueue queue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(queue);

        long framesWritten = 0;
        long bytesWritten = 0;

        while (true)
        {
            OutboundFrame frame;
            try
            {
                frame = await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new OutboundWriterResult(
                    OutboundWriterStopReason.Cancelled,
                    framesWritten,
                    bytesWritten);
            }
            catch (ChannelClosedException ex) when (ex.InnerException is null)
            {
                return new OutboundWriterResult(
                    OutboundWriterStopReason.Completed,
                    framesWritten,
                    bytesWritten);
            }
            catch (ChannelClosedException ex)
            {
                return new OutboundWriterResult(
                    OutboundWriterStopReason.QueueFailure,
                    framesWritten,
                    bytesWritten,
                    ex.InnerException ?? ex);
            }

            try
            {
                await stream.WriteAsync(frame.Bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new OutboundWriterResult(
                    OutboundWriterStopReason.Cancelled,
                    framesWritten,
                    bytesWritten);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return new OutboundWriterResult(
                    OutboundWriterStopReason.IoFailure,
                    framesWritten,
                    bytesWritten,
                    ex);
            }

            framesWritten++;
            bytesWritten += frame.Length;
        }
    }
}
