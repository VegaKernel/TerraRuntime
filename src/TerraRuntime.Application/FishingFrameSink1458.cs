using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed record CompleteAnglerQuestRuntimeCommand(ConnectionHandle Connection) : RuntimeCommand;
internal sealed record PublishAnglerStateRuntimeCommand(ConnectionHandle Connection) : RuntimeCommand;
internal sealed record OpenFishingCrateRuntimeCommand(ConnectionHandle Connection, short Slot,
    ulong ExpectedInventorySerial, TaskCompletionSource<bool>? Completion = null) : RuntimeCommand;

/// <summary>Packet75 is an intent to submit the real fish; packet76 is presentation, never quest authority.</summary>
internal sealed class FishingFrameSink1458(GameCommandSourceId source, PlayerBootstrapFrameSink bootstrap,
    ITerrariaFrameSink inner, IGameCommandIngress<RuntimeCommand> ingress) : ITerrariaFrameSink,
    ITerrariaFrameRejectionSource, ITerrariaConnectionStopReasonSource
{
    private TerrariaFrameRejectionCategory rejection;
    public TerrariaFrameRejectionCategory RejectionCategory => rejection != TerrariaFrameRejectionCategory.None
        ? rejection : (inner as ITerrariaFrameRejectionSource)?.RejectionCategory ?? TerrariaFrameRejectionCategory.None;
    public TerrariaConnectionStopReason ConnectionStopReason =>
        (inner as ITerrariaConnectionStopReasonSource)?.ConnectionStopReason ?? TerrariaConnectionStopReason.None;

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (rejection != TerrariaFrameRejectionCategory.None) return TerrariaFrameSinkResult.Stop;
        if (frame.MessageId is not (74 or 75 or 76)) return inner.OnFrame(frame);
        if (bootstrap.JoinState != PlayerJoinState.Playing || bootstrap.AssignedPlayerHandle is not PlayerHandle player)
        { rejection = TerrariaFrameRejectionCategory.InvalidState; return TerrariaFrameSinkResult.Stop; }
        long expected = frame.MessageId switch { 74 => 2, 75 => 0, _ => 9 };
        if (frame.Payload.Length != expected)
        { rejection = TerrariaFrameRejectionCategory.MalformedProtocol; return TerrariaFrameSinkResult.Stop; }
        var connection = new ConnectionHandle(source, player);
        // Source74 is server-to-client only. Client76 counters cannot alter the
        // paid quest state or its progression; correct that occupation's view.
        RuntimeCommand command = frame.MessageId == 75
            ? new CompleteAnglerQuestRuntimeCommand(connection) : new PublishAnglerStateRuntimeCommand(connection);
        _ = ingress.TryPost(source, command);
        return TerrariaFrameSinkResult.Continue;
    }
}
