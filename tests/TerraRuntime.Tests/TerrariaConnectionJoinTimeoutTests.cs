using System.Buffers;
using System.Net;
using System.Net.Sockets;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionJoinTimeoutTests
{
    [Fact]
    public void Policy_state_distinguishes_handshake_join_and_ready_deadlines()
    {
        var time = new ManualTimeProvider();
        var options = new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(10),
            idleTimeout: Timeout.InfiniteTimeSpan,
            ConnectionRateBudgetOptions.AccountingOnly,
            ConnectionMessageRateLimits.None,
            joinTimeout: TimeSpan.FromSeconds(30));
        var state = new TerrariaConnectionPolicyState(options, time);

        Assert.Equal(TimeSpan.FromSeconds(10), state.GetRemainingTimeout(connectionReady: false));
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(state.TryCompleteHandshake());
        Assert.Equal(TimeSpan.FromSeconds(30), state.GetRemainingTimeout(connectionReady: false));

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(TimeSpan.FromSeconds(1), state.GetRemainingTimeout(connectionReady: false));
        Assert.Equal(Timeout.InfiniteTimeSpan, state.GetRemainingTimeout(connectionReady: true));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(state.TryExpire(connectionReady: false, out TerrariaConnectionStopReason reason));
        Assert.Equal(TerrariaConnectionStopReason.JoinTimeout, reason);
    }

    [Fact]
    public async Task Socket_disconnects_when_hello_completes_but_readiness_never_arrives()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endpoint = (IPEndPoint)listener.LocalEndPoint!;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ValueTask connectTask = client.ConnectAsync(endpoint, cancellationToken);
        Socket serverSocket = await listener.AcceptAsync(cancellationToken);
        await connectTask;

        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(4, 64, 32));
        var sink = new ReadinessSink(connectionReady: false);
        var policy = new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(2),
            idleTimeout: Timeout.InfiniteTimeSpan,
            ConnectionRateBudgetOptions.AccountingOnly,
            ConnectionMessageRateLimits.None,
            joinTimeout: TimeSpan.FromMilliseconds(200));

        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            policy,
            cancellationToken).AsTask();

        byte[] hello = CurrentHelloPacket();
        Assert.Equal(hello.Length, await client.SendAsync(hello, SocketFlags.None, cancellationToken));

        TerrariaSocketRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);

        Assert.Equal(1, sink.Frames);
        Assert.Equal(TerrariaPipePumpResult.Cancelled, result.Inbound);
        Assert.Equal(OutboundWriterStopReason.Cancelled, result.Outbound.Reason);
        Assert.Equal(TerrariaConnectionStopReason.JoinTimeout, result.StopReason);
        Assert.True(outbound.IsCompleted);
    }

    [Fact]
    public async Task Ready_connection_is_not_subject_to_join_timeout()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endpoint = (IPEndPoint)listener.LocalEndPoint!;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ValueTask connectTask = client.ConnectAsync(endpoint, cancellationToken);
        Socket serverSocket = await listener.AcceptAsync(cancellationToken);
        await connectTask;

        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(4, 64, 32));
        var sink = new ReadinessSink(connectionReady: true);
        var policy = new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(2),
            idleTimeout: Timeout.InfiniteTimeSpan,
            ConnectionRateBudgetOptions.AccountingOnly,
            ConnectionMessageRateLimits.None,
            joinTimeout: TimeSpan.FromMilliseconds(100));

        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            policy,
            cancellationToken).AsTask();

        byte[] hello = CurrentHelloPacket();
        Assert.Equal(hello.Length, await client.SendAsync(hello, SocketFlags.None, cancellationToken));
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        Assert.False(run.IsCompleted);

        client.Shutdown(SocketShutdown.Send);
        TerrariaSocketRunResult result = await run;
        Assert.Equal(TerrariaConnectionStopReason.PeerClosed, result.StopReason);
    }

    private static byte[] CurrentHelloPacket() =>
    [
        15, 0,
        (byte)TerrariaMessageId.Hello,
        11,
        (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
        (byte)'3', (byte)'2', (byte)'6'
    ];

    private sealed class ReadinessSink(bool connectionReady) :
        ITerrariaFrameSink,
        ITerrariaConnectionReadinessSource
    {
        public int Frames { get; private set; }

        public bool ConnectionReady { get; } = connectionReady;

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            Frames++;
            return TerrariaFrameSinkResult.Continue;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref timestamp);

        public void Advance(TimeSpan amount) => Interlocked.Add(ref timestamp, amount.Ticks);
    }
}
