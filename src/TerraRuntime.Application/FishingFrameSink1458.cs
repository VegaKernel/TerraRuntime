using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record CompleteAnglerQuestRuntimeCommand(ConnectionHandle Connection) : RuntimeCommand
{
    internal bool ValidateClientReports { get; init; }
}
internal sealed record PublishAnglerStateRuntimeCommand(ConnectionHandle Connection) : RuntimeCommand;
internal sealed record PlayerCharacterProgressRuntimeCommand(ConnectionHandle Connection,
    int AnglerQuestsFinished, int GolferScoreAccumulated) : RuntimeCommand;
internal sealed record OpenFishingCrateRuntimeCommand(ConnectionHandle Connection, short Slot,
    ulong ExpectedInventorySerial, TaskCompletionSource<bool>? Completion = null) : RuntimeCommand;
internal sealed record FishingNpcClientRequestRuntimeCommand(ConnectionHandle Connection,
    TerrariaFishingNpcRequest1458 Request) : RuntimeCommand;

/// <summary>Packet75 requests a paid quest; packet76 imports character counters without completing a quest.</summary>
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
        if (frame.MessageId is not (61 or 74 or 75 or 76 or 130)) return inner.OnFrame(frame);
        TerrariaFishingNpcRequest1458 npcRequest = default;
        if (frame.MessageId is 61 or 130)
        {
            if (!TerrariaFishingNpcRequestCodec1458.TryDecode(frame, out npcRequest))
            { rejection = TerrariaFrameRejectionCategory.MalformedProtocol; return TerrariaFrameSinkResult.Stop; }
            if (frame.MessageId == 61 && npcRequest.NpcType != 370) return inner.OnFrame(frame);
        }
        if (bootstrap.JoinState != PlayerJoinState.Playing || bootstrap.AssignedPlayerHandle is not PlayerHandle player)
        { rejection = TerrariaFrameRejectionCategory.InvalidState; return TerrariaFrameSinkResult.Stop; }
        var connection = new ConnectionHandle(source, player);
        if (frame.MessageId is 61 or 130)
        {
            _ = ingress.TryPost(source, new FishingNpcClientRequestRuntimeCommand(connection, npcRequest));
            return TerrariaFrameSinkResult.Continue;
        }
        if (frame.MessageId == TerrariaPlayerCharacterProgressCodec1458.MessageId)
        {
            if (TerrariaPlayerCharacterProgressCodec1458.TryDecode(frame, out var progress) !=
                TerrariaPlayerCharacterProgressDecodeResult1458.Decoded)
            { rejection = TerrariaFrameRejectionCategory.MalformedProtocol; return TerrariaFrameSinkResult.Stop; }
            // Source76 ignores the dedicated server's myPlayer=255 before substituting whoAmI.
            // Negative counters are a bounded semantic refusal here, not a malformed wire image.
            if (progress.PlayerSlot == byte.MaxValue || progress.AnglerQuestsFinished < 0 || progress.GolferScoreAccumulated < 0)
                return TerrariaFrameSinkResult.Continue;
            _ = ingress.TryPost(source, new PlayerCharacterProgressRuntimeCommand(connection,
                progress.AnglerQuestsFinished, progress.GolferScoreAccumulated));
            return TerrariaFrameSinkResult.Continue;
        }
        long expected = frame.MessageId switch { 74 => 2, 75 => 0, _ => 9 };
        if (frame.Payload.Length != expected)
        { rejection = TerrariaFrameRejectionCategory.MalformedProtocol; return TerrariaFrameSinkResult.Stop; }
        // Source74 is server-to-client only. Its imported daily image never completes a quest.
        RuntimeCommand command = frame.MessageId == 75
            ? new CompleteAnglerQuestRuntimeCommand(connection) { ValidateClientReports = true } : new PublishAnglerStateRuntimeCommand(connection);
        _ = ingress.TryPost(source, command);
        return TerrariaFrameSinkResult.Continue;
    }
}
