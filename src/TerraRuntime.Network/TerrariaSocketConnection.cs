using System.IO.Pipelines;
using System.Net.Sockets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

public static class TerrariaSocketConnection
{
    public static ValueTask<TerrariaSocketRunResult> RunAsync(
        Socket socket,
        ITerrariaFrameSink sink,
        TerrariaConnectionOutboundQueue outboundQueue,
        TerrariaFrameDecoderOptions decoderOptions,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            socket,
            sink,
            outboundQueue,
            decoderOptions,
            TerrariaConnectionPolicyOptions.Default,
            cancellationToken);
    }

    public static async ValueTask<TerrariaSocketRunResult> RunAsync(
        Socket socket,
        ITerrariaFrameSink sink,
        TerrariaConnectionOutboundQueue outboundQueue,
        TerrariaFrameDecoderOptions decoderOptions,
        TerrariaConnectionPolicyOptions policyOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(outboundQueue);

        socket.NoDelay = true;

        using var stream = new NetworkStream(socket, ownsSocket: false);
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var policyState = new TerrariaConnectionPolicyState(policyOptions);
        var policySink = new TerrariaConnectionPolicySink(sink, policyState);

        Task<TerrariaPipePumpResult> inboundTask = TerrariaPipeFramePump
            .RunAsync(reader, policySink, decoderOptions, linkedCancellation.Token)
            .AsTask();
        Task<OutboundWriterResult> outboundTask = TerrariaOutboundFrameWriter
            .RunAsync(stream, outboundQueue.InnerQueue, linkedCancellation.Token)
            .AsTask();
        Task<TerrariaConnectionStopReason> watchdogTask = RunWatchdogAsync(
            policyState,
            linkedCancellation.Token);
        Task slowClientTask = outboundQueue.SlowClientSignal;

        TerrariaPipePumpResult inboundResult = TerrariaPipePumpResult.Cancelled;
        OutboundWriterResult outboundResult = new(OutboundWriterStopReason.Cancelled, 0, 0);
        TerrariaConnectionStopReason stopReason = TerrariaConnectionStopReason.None;

        try
        {
            Task first = await Task.WhenAny(inboundTask, outboundTask, watchdogTask, slowClientTask).ConfigureAwait(false);
            if (ReferenceEquals(first, slowClientTask))
            {
                policyState.TryStop(TerrariaConnectionStopReason.SlowClient);
                stopReason = TerrariaConnectionStopReason.SlowClient;
                linkedCancellation.Cancel();
            }
            else if (ReferenceEquals(first, watchdogTask))
            {
                stopReason = await watchdogTask.ConfigureAwait(false);
                linkedCancellation.Cancel();
            }
            else if (ReferenceEquals(first, inboundTask))
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
                outboundResult = await outboundTask.ConfigureAwait(false);
                linkedCancellation.Cancel();
            }

            inboundResult = await inboundTask.ConfigureAwait(false);
            outboundResult = await outboundTask.ConfigureAwait(false);
            linkedCancellation.Cancel();
            await watchdogTask.ConfigureAwait(false);

            if (stopReason == TerrariaConnectionStopReason.None)
            {
                stopReason = policyState.StopReason;
            }

            if (stopReason == TerrariaConnectionStopReason.None)
            {
                stopReason = MapStopReason(inboundResult, outboundResult, cancellationToken.IsCancellationRequested);
            }

            return new TerrariaSocketRunResult(inboundResult, outboundResult, stopReason);
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

    private static async Task<TerrariaConnectionStopReason> RunWatchdogAsync(
        TerrariaConnectionPolicyState state,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                TimeSpan remaining = state.GetRemainingTimeout();
                if (remaining == Timeout.InfiniteTimeSpan)
                {
                    return TerrariaConnectionStopReason.None;
                }

                if (remaining <= TimeSpan.Zero)
                {
                    if (state.TryExpire(out TerrariaConnectionStopReason reason))
                    {
                        return reason;
                    }

                    continue;
                }

                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TerrariaConnectionStopReason.None;
        }
    }

    private static TerrariaConnectionStopReason MapStopReason(
        TerrariaPipePumpResult inbound,
        OutboundWriterResult outbound,
        bool externallyCancelled)
    {
        if (externallyCancelled)
        {
            return TerrariaConnectionStopReason.Cancelled;
        }

        if (inbound == TerrariaPipePumpResult.Completed)
        {
            return TerrariaConnectionStopReason.PeerClosed;
        }

        if (inbound == TerrariaPipePumpResult.IoFailure)
        {
            return TerrariaConnectionStopReason.InboundIoFailure;
        }

        if (inbound is TerrariaPipePumpResult.InvalidFrameLength or
            TerrariaPipePumpResult.FrameTooLarge or
            TerrariaPipePumpResult.TruncatedFrame)
        {
            return TerrariaConnectionStopReason.ProtocolFailure;
        }

        if (outbound.Reason is OutboundWriterStopReason.IoFailure or OutboundWriterStopReason.QueueFailure)
        {
            return TerrariaConnectionStopReason.OutboundFailure;
        }

        return TerrariaConnectionStopReason.Cancelled;
    }
}
