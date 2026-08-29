using System.Buffers;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionPolicyTests
{
    [Fact]
    public void Rejects_a_non_hello_first_frame()
    {
        var time = new ManualTimeProvider();
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            time);
        var sink = new CountingSink();
        var policy = new TerrariaConnectionPolicySink(sink, state);
        TerrariaFrame frame = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);

        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in frame));
        Assert.Equal(TerrariaConnectionStopReason.InvalidHandshake, state.StopReason);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void Marks_handshake_complete_only_after_inner_sink_accepts_hello()
    {
        var time = new ManualTimeProvider();
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            time);
        var sink = new CountingSink();
        var policy = new TerrariaConnectionPolicySink(sink, state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        TerrariaFrame next = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);

        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));
        Assert.True(state.HandshakeComplete);
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in next));
        Assert.Equal(2, sink.Count);
        Assert.Equal(TerrariaConnectionStopReason.None, state.StopReason);
    }

    [Fact]
    public void Does_not_complete_handshake_when_inner_sink_rejects_hello()
    {
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            new ManualTimeProvider());
        var sink = new RejectingSink();
        var policy = new TerrariaConnectionPolicySink(sink, state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());

        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in hello));
        Assert.False(state.HandshakeComplete);
        Assert.Equal(TerrariaConnectionStopReason.ApplicationStopped, state.StopReason);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public void Rejects_a_second_hello_after_handshake_before_forwarding_it()
    {
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            new ManualTimeProvider());
        var sink = new CountingSink();
        var policy = new TerrariaConnectionPolicySink(sink, state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());

        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));
        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in hello));
        Assert.Equal(TerrariaConnectionStopReason.InvalidHandshake, state.StopReason);
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public void Expires_a_connection_that_never_handshakes()
    {
        var time = new ManualTimeProvider();
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            time);

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(state.TryExpire(out TerrariaConnectionStopReason reason));
        Assert.Equal(TerrariaConnectionStopReason.HandshakeTimeout, reason);
    }

    [Fact]
    public void Expires_an_idle_connection_after_handshake_when_explicitly_configured()
    {
        var time = new ManualTimeProvider();
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            time);
        var policy = new TerrariaConnectionPolicySink(new CountingSink(), state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(state.TryExpire(out _));
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(state.TryExpire(out TerrariaConnectionStopReason reason));
        Assert.Equal(TerrariaConnectionStopReason.IdleTimeout, reason);
    }

    [Fact]
    public void Default_policy_expires_an_established_connection_after_the_normal_idle_ceiling()
    {
        var time = new ManualTimeProvider();
        var state = new TerrariaConnectionPolicyState(TerrariaConnectionPolicyOptions.Default, time);
        var policy = new TerrariaConnectionPolicySink(new CountingSink(), state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));

        Assert.Equal(TimeSpan.FromMinutes(10), TerrariaConnectionPolicyOptions.DefaultIdleTimeout);
        Assert.Equal(TerrariaConnectionPolicyOptions.DefaultIdleTimeout, state.GetRemainingTimeout());

        time.Advance(TerrariaConnectionPolicyOptions.DefaultIdleTimeout - TimeSpan.FromTicks(1));
        Assert.False(state.TryExpire(out _));
        time.Advance(TimeSpan.FromTicks(1));

        Assert.True(state.TryExpire(out TerrariaConnectionStopReason reason));
        Assert.Equal(TerrariaConnectionStopReason.IdleTimeout, reason);
        Assert.Equal(TerrariaConnectionStopReason.IdleTimeout, state.StopReason);
    }

    [Fact]
    public void Accepted_inbound_activity_refreshes_the_default_idle_deadline()
    {
        var time = new ManualTimeProvider();
        var state = new TerrariaConnectionPolicyState(TerrariaConnectionPolicyOptions.Default, time);
        var policy = new TerrariaConnectionPolicySink(new CountingSink(), state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        TerrariaFrame activity = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));

        time.Advance(TerrariaConnectionPolicyOptions.DefaultIdleTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in activity));
        time.Advance(TerrariaConnectionPolicyOptions.DefaultIdleTimeout - TimeSpan.FromSeconds(1));
        Assert.False(state.TryExpire(out TerrariaConnectionStopReason reason));
        Assert.Equal(TerrariaConnectionStopReason.None, reason);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(state.TryExpire(out reason));
        Assert.Equal(TerrariaConnectionStopReason.IdleTimeout, reason);
    }

    private static TerrariaFrame Decode(byte[] packet)
    {
        var input = new ReadOnlySequence<byte>(packet);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.True(input.IsEmpty);
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

    private sealed class RejectingSink : ITerrariaFrameSink
    {
        public int Count { get; private set; }

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            Count++;
            return TerrariaFrameSinkResult.Stop;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _timestamp, amount.Ticks);
    }
}
