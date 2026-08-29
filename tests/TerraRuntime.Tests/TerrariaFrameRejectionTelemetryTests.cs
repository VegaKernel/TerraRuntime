using System.Buffers;
using System.Net;
using System.Net.Sockets;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaFrameRejectionTelemetryTests
{
    [Theory]
    [InlineData(TerrariaFrameRejectionCategory.MalformedProtocol)]
    [InlineData(TerrariaFrameRejectionCategory.InvalidState)]
    [InlineData(TerrariaFrameRejectionCategory.GameplayRejected)]
    [InlineData(TerrariaFrameRejectionCategory.Backpressure)]
    public void Policy_records_normalized_inner_rejection_category(TerrariaFrameRejectionCategory category)
    {
        TerrariaFrameRejectionTelemetrySnapshot before = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        Assert.True(state.TryCompleteHandshake());
        var policy = new TerrariaConnectionPolicySink(new RejectingSink(category), state);
        TerrariaFrame frame = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);

        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in frame));

        TerrariaFrameRejectionTelemetrySnapshot after = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        Assert.True(Count(after, category) - Count(before, category) >= 1);
        Assert.Equal(TerrariaConnectionStopReason.FrameRejected, state.StopReason);
    }

    [Fact]
    public void Policy_records_rate_limit_separately_from_other_rejections()
    {
        TerrariaFrameRejectionTelemetrySnapshot before = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        var options = new TerrariaConnectionPolicyOptions(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null));
        var state = new TerrariaConnectionPolicyState(options);
        var sink = new ContinueSink();
        var policy = new TerrariaConnectionPolicySink(sink, state);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        TerrariaFrame next = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);

        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));
        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in next));

        TerrariaFrameRejectionTelemetrySnapshot after = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        Assert.True(after.RateLimited - before.RateLimited >= 1);
        Assert.Equal(TerrariaConnectionStopReason.RateLimited, state.StopReason);
    }

    [Fact]
    public async Task Malformed_framing_is_recorded_before_the_gameplay_sink()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TerrariaFrameRejectionTelemetrySnapshot before = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endpoint = (IPEndPoint)listener.LocalEndPoint!;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ValueTask connectTask = client.ConnectAsync(endpoint, cancellationToken);
        Socket serverSocket = await listener.AcceptAsync(cancellationToken);
        await connectTask;

        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(4, 64, 32));
        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            new ContinueSink(),
            outbound,
            TerrariaFrameDecoderOptions.Default,
            cancellationToken).AsTask();

        Assert.Equal(2, await client.SendAsync(new byte[] { 0, 0 }, SocketFlags.None, cancellationToken));
        TerrariaSocketRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);

        TerrariaFrameRejectionTelemetrySnapshot after = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        Assert.Equal(TerrariaConnectionStopReason.ProtocolFailure, result.StopReason);
        Assert.True(after.MalformedProtocol - before.MalformedProtocol >= 1);
    }

    private static long Count(TerrariaFrameRejectionTelemetrySnapshot snapshot, TerrariaFrameRejectionCategory category) =>
        category switch
        {
            TerrariaFrameRejectionCategory.MalformedProtocol => snapshot.MalformedProtocol,
            TerrariaFrameRejectionCategory.RateLimited => snapshot.RateLimited,
            TerrariaFrameRejectionCategory.InvalidState => snapshot.InvalidState,
            TerrariaFrameRejectionCategory.GameplayRejected => snapshot.GameplayRejected,
            TerrariaFrameRejectionCategory.Backpressure => snapshot.Backpressure,
            _ => 0
        };

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

    private sealed class RejectingSink(TerrariaFrameRejectionCategory category) : ITerrariaFrameSink, ITerrariaFrameRejectionSource
    {
        public TerrariaFrameRejectionCategory RejectionCategory { get; } = category;

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Stop;
    }

    private sealed class ContinueSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }
}
