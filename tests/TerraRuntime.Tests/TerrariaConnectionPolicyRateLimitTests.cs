using System.Buffers;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionPolicyRateLimitTests
{
    [Fact]
    public void Stops_before_forwarding_a_frame_that_exceeds_the_configured_rate_budget()
    {
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        var accountant = new TerrariaConnectionRateAccountant(
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null));
        var inner = new CountingSink();
        var policy = new TerrariaConnectionPolicySink(inner, state, accountant);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        TerrariaFrame next = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);

        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));
        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in next));
        Assert.Equal(TerrariaConnectionStopReason.RateLimited, state.StopReason);
        Assert.Equal(1, inner.Count);
        Assert.Equal(1, accountant.Snapshot.RejectedFrames);
    }

    private static TerrariaFrame Decode(byte[] packet)
    {
        var input = new ReadOnlySequence<byte>(packet);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        return frame;
    }

    private static byte[] CurrentHelloPacket() =>
    [
        15, 0,
        (byte)TerrariaMessageId.Hello,
        11,
        (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
        (byte)'3', (byte)'2', (byte)'6'
    ];

    private sealed class CountingSink : ITerrariaFrameSink
    {
        public int Count { get; private set; }

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            Count++;
            return TerrariaFrameSinkResult.Continue;
        }
    }
}
