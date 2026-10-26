using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>Owns authenticated packet59 decoding before receive-buffer disposal.</summary>
internal sealed class HitSwitchFrameSink1458(GameCommandSourceId source, PlayerBootstrapFrameSink bootstrap,
    ITerrariaFrameSink inner, IWiringNetworkIngress1458 ingress) : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    public TerrariaFrameRejectionCategory RejectionCategory { get; private set; }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (RejectionCategory != TerrariaFrameRejectionCategory.None) return TerrariaFrameSinkResult.Stop;
        if (frame.MessageId != (byte)TerrariaMessageId.HitSwitch) return inner.OnFrame(in frame);
        if (!TerrariaHitSwitchCodec1458.TryDecode(in frame, out var state))
            RejectionCategory = TerrariaFrameRejectionCategory.MalformedProtocol;
        else if (bootstrap.JoinState != PlayerJoinState.Playing || bootstrap.AssignedPlayerHandle is not PlayerHandle)
            RejectionCategory = TerrariaFrameRejectionCategory.InvalidState;
        else
        {
            _ = ingress.TryPostHitSwitch(new ConnectionHandle(source, bootstrap.AssignedPlayerHandle.Value), in state);
            return TerrariaFrameSinkResult.Continue;
        }
        return TerrariaFrameSinkResult.Stop;
    }
}
