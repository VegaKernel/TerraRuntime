using System.Buffers;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionPolicyRejectionTests
{
    [Theory]
    [InlineData(TerrariaFrameRejectionCategory.MalformedProtocol, TerrariaConnectionStopReason.FrameRejected)]
    [InlineData(TerrariaFrameRejectionCategory.InvalidState, TerrariaConnectionStopReason.FrameRejected)]
    [InlineData(TerrariaFrameRejectionCategory.GameplayRejected, TerrariaConnectionStopReason.FrameRejected)]
    [InlineData(TerrariaFrameRejectionCategory.Backpressure, TerrariaConnectionStopReason.FrameRejected)]
    [InlineData(TerrariaFrameRejectionCategory.RateLimited, TerrariaConnectionStopReason.RateLimited)]
    public void Normalizes_rejection_source_stop_without_claiming_application_shutdown(
        TerrariaFrameRejectionCategory category,
        TerrariaConnectionStopReason expected)
    {
        var state = new TerrariaConnectionPolicyState(new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(10),
            idleTimeout: Timeout.InfiniteTimeSpan,
            rateBudget: ConnectionRateBudgetOptions.AccountingOnly,
            messageRateLimits: ConnectionMessageRateLimits.None,
            joinTimeout: Timeout.InfiniteTimeSpan));
        var sink = new TerrariaConnectionPolicySink(new RejectingSink(category), state);

        TerrariaFrameSinkResult result = sink.OnFrame(HelloFrame());

        Assert.Equal(TerrariaFrameSinkResult.Stop, result);
        Assert.Equal(expected, state.StopReason);
        Assert.NotEqual(TerrariaConnectionStopReason.ApplicationStopped, state.StopReason);
    }

    [Fact]
    public void Preserves_application_stopped_for_unclassified_inner_stop()
    {
        var state = new TerrariaConnectionPolicyState(new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(10),
            idleTimeout: Timeout.InfiniteTimeSpan,
            rateBudget: ConnectionRateBudgetOptions.AccountingOnly,
            messageRateLimits: ConnectionMessageRateLimits.None,
            joinTimeout: Timeout.InfiniteTimeSpan));
        var sink = new TerrariaConnectionPolicySink(new UnclassifiedStoppingSink(), state);

        TerrariaFrameSinkResult result = sink.OnFrame(HelloFrame());

        Assert.Equal(TerrariaFrameSinkResult.Stop, result);
        Assert.Equal(TerrariaConnectionStopReason.ApplicationStopped, state.StopReason);
    }

    private static TerrariaFrame HelloFrame() => new(
        PacketLength: 3,
        MessageId: (byte)TerrariaMessageId.Hello,
        Packet: ReadOnlySequence<byte>.Empty,
        Payload: ReadOnlySequence<byte>.Empty);

    private sealed class RejectingSink(TerrariaFrameRejectionCategory category) :
        ITerrariaFrameSink,
        ITerrariaFrameRejectionSource
    {
        public TerrariaFrameRejectionCategory RejectionCategory { get; } = category;

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Stop;
    }

    private sealed class UnclassifiedStoppingSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Stop;
    }
}
