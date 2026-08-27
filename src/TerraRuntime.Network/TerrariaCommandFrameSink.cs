using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

/// <summary>
/// Synchronously decodes transient network frames into owned typed commands and submits them to the
/// authoritative command ingress. Raw frame buffers never cross the game-loop boundary.
/// </summary>
public sealed class TerrariaCommandFrameSink<TCommand> : ITerrariaFrameSink
{
    private readonly GameCommandSourceId source;
    private readonly ITerrariaCommandDecoder<TCommand> decoder;
    private readonly IGameCommandIngress<TCommand> ingress;

    public TerrariaCommandFrameSink(
        GameCommandSourceId source,
        ITerrariaCommandDecoder<TCommand> decoder,
        IGameCommandIngress<TCommand> ingress)
    {
        if (source.IsSystem)
        {
            throw new ArgumentException("Network frame sources must identify a connection.", nameof(source));
        }

        this.source = source;
        this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public TerrariaCommandFrameSinkStopReason StopReason { get; private set; }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        TerrariaCommandDecodeResult result = decoder.TryDecode(in frame, out TCommand command);
        switch (result)
        {
            case TerrariaCommandDecodeResult.Ignored:
                return TerrariaFrameSinkResult.Continue;

            case TerrariaCommandDecodeResult.Decoded:
                if (ingress.TryPost(source, command))
                {
                    return TerrariaFrameSinkResult.Continue;
                }

                StopReason = TerrariaCommandFrameSinkStopReason.GameLoopBackpressure;
                return TerrariaFrameSinkResult.Stop;

            case TerrariaCommandDecodeResult.Malformed:
                StopReason = TerrariaCommandFrameSinkStopReason.MalformedCommand;
                return TerrariaFrameSinkResult.Stop;

            default:
                throw new InvalidOperationException($"Unknown command decode result: {result}.");
        }
    }
}
