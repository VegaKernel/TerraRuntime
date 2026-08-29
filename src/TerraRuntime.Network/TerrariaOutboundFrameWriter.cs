using System.Buffers;
using System.Threading.Channels;

namespace TerraRuntime.Network;

public static class TerrariaOutboundFrameWriter
{
    public static ValueTask<OutboundWriterResult> RunAsync(
        Stream stream,
        BoundedOutboundQueue queue,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(stream, queue, OutboundWriterOptions.Default, cancellationToken);
    }

    public static async ValueTask<OutboundWriterResult> RunAsync(
        Stream stream,
        BoundedOutboundQueue queue,
        OutboundWriterOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(queue);

        long framesWritten = 0;
        long bytesWritten = 0;

        while (true)
        {
            OutboundFrame firstFrame;
            try
            {
                firstFrame = await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
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

            int batchFrames = 1;
            int batchBytes = firstFrame.Length;
            byte[]? rentedBuffer = null;
            ReadOnlyMemory<byte> writeBuffer = firstFrame.Bytes;

            if (CanBatch(firstFrame, options) &&
                queue.TryPeek(out OutboundFrame nextFrame) &&
                CanAppend(nextFrame, batchFrames, batchBytes, options))
            {
                rentedBuffer = ArrayPool<byte>.Shared.Rent(options.MaxBatchBytes);
                firstFrame.Bytes.Span.CopyTo(rentedBuffer);

                while (batchFrames < options.MaxBatchFrames &&
                    queue.TryPeek(out nextFrame) &&
                    CanAppend(nextFrame, batchFrames, batchBytes, options) &&
                    queue.TryRead(out OutboundFrame dequeued))
                {
                    dequeued.Bytes.Span.CopyTo(rentedBuffer.AsSpan(batchBytes));
                    batchFrames++;
                    batchBytes += dequeued.Length;
                }

                writeBuffer = rentedBuffer.AsMemory(0, batchBytes);
            }

            try
            {
                await stream.WriteAsync(writeBuffer, cancellationToken).ConfigureAwait(false);
                TerrariaMessageTrafficTelemetry.Shared.ObserveEncodedOutbound(writeBuffer.Span);
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
            finally
            {
                if (rentedBuffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }

            framesWritten += batchFrames;
            bytesWritten += batchBytes;
        }
    }

    private static bool CanBatch(OutboundFrame frame, OutboundWriterOptions options) =>
        options.MaxBatchFrames > 1 && frame.Length <= options.MaxBatchFrameBytes;

    private static bool CanAppend(
        OutboundFrame frame,
        int batchFrames,
        int batchBytes,
        OutboundWriterOptions options) =>
        batchFrames < options.MaxBatchFrames &&
        frame.Length <= options.MaxBatchFrameBytes &&
        batchBytes <= options.MaxBatchBytes - frame.Length;
}
