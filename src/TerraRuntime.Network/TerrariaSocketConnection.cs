using System.IO.Pipelines;
using System.Net.Sockets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

public static class TerrariaSocketConnection
{
    public static async ValueTask<TerrariaSocketRunResult> RunAsync(
        Socket socket,
        ITerrariaFrameSink sink,
        BoundedOutboundQueue outboundQueue,
        TerrariaFrameDecoderOptions decoderOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(outboundQueue);

        socket.NoDelay = true;

        using var stream = new NetworkStream(socket, ownsSocket: false);
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<TerrariaPipePumpResult> inboundTask = TerrariaPipeFramePump
            .RunAsync(reader, sink, decoderOptions, linkedCancellation.Token)
            .AsTask();
        Task<OutboundWriterResult> outboundTask = TerrariaOutboundFrameWriter
            .RunAsync(stream, outboundQueue, linkedCancellation.Token)
            .AsTask();

        TerrariaPipePumpResult inboundResult;
        OutboundWriterResult outboundResult;

        try
        {
            Task first = await Task.WhenAny(inboundTask, outboundTask).ConfigureAwait(false);
            if (ReferenceEquals(first, inboundTask))
            {
                inboundResult = await inboundTask.ConfigureAwait(false);
                if (inboundResult is TerrariaPipePumpResult.Completed or TerrariaPipePumpResult.SinkStopped)
                {
                    outboundQueue.Complete();
                }
                else
                {
                    linkedCancellation.Cancel();
                }
            }
            else
            {
                linkedCancellation.Cancel();
            }

            inboundResult = await inboundTask.ConfigureAwait(false);
            outboundResult = await outboundTask.ConfigureAwait(false);
            return new TerrariaSocketRunResult(inboundResult, outboundResult);
        }
        finally
        {
            outboundQueue.Complete();
            linkedCancellation.Cancel();
            await reader.CompleteAsync().ConfigureAwait(false);

            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }

            socket.Dispose();
        }
    }
}
