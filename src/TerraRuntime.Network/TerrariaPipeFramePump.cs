using System.Buffers;
using System.IO.Pipelines;
using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

public static class TerrariaPipeFramePump
{
    public static ValueTask<TerrariaPipePumpResult> RunAsync(
        PipeReader reader,
        ITerrariaFrameSink sink,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(reader, sink, TerrariaFrameDecoderOptions.Default, cancellationToken);
    }

    public static async ValueTask<TerrariaPipePumpResult> RunAsync(
        PipeReader reader,
        ITerrariaFrameSink sink,
        TerrariaFrameDecoderOptions decoderOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sink);

        while (true)
        {
            ReadResult readResult;
            try
            {
                readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return TerrariaPipePumpResult.Cancelled;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return TerrariaPipePumpResult.IoFailure;
            }

            ReadOnlySequence<byte> buffer = readResult.Buffer;
            if (readResult.IsCanceled)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                return TerrariaPipePumpResult.Cancelled;
            }

            while (true)
            {
                TerrariaFrameReadResult frameResult = TerrariaFrameDecoder.TryRead(
                    ref buffer,
                    decoderOptions,
                    out TerrariaFrame frame);

                switch (frameResult)
                {
                    case TerrariaFrameReadResult.Frame:
                        if (sink.OnFrame(in frame) == TerrariaFrameSinkResult.Stop)
                        {
                            reader.AdvanceTo(buffer.Start, readResult.Buffer.End);
                            return TerrariaPipePumpResult.SinkStopped;
                        }

                        continue;

                    case TerrariaFrameReadResult.NeedMoreData:
                        if (readResult.IsCompleted)
                        {
                            bool truncated = !buffer.IsEmpty;
                            reader.AdvanceTo(readResult.Buffer.End);
                            return truncated
                                ? TerrariaPipePumpResult.TruncatedFrame
                                : TerrariaPipePumpResult.Completed;
                        }

                        reader.AdvanceTo(buffer.Start, readResult.Buffer.End);
                        break;

                    case TerrariaFrameReadResult.InvalidLength:
                        reader.AdvanceTo(readResult.Buffer.End);
                        return TerrariaPipePumpResult.InvalidFrameLength;

                    case TerrariaFrameReadResult.FrameTooLarge:
                        reader.AdvanceTo(readResult.Buffer.End);
                        return TerrariaPipePumpResult.FrameTooLarge;

                    default:
                        throw new InvalidOperationException($"Unknown frame decode result: {frameResult}.");
                }

                break;
            }
        }
    }
}
