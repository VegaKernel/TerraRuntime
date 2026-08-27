using System.Net;
using System.Net.Sockets;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaSocketConnectionTests
{
    [Fact]
    public async Task Pumps_fragmented_ingress_and_outbound_frames_over_a_real_tcp_socket()
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
        byte[] response = [3, 0, (byte)TerrariaMessageId.Kick];
        Assert.Equal(OutboundEnqueueResult.Enqueued, outbound.TryEnqueue(new OutboundFrame(response)));
        var sink = new HelloCountingSink();

        ValueTask<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            cancellationToken);

        byte[] hello = CurrentHelloPacket();
        Assert.Equal(1, await client.SendAsync(hello.AsMemory(0, 1), SocketFlags.None, cancellationToken));
        Assert.Equal(hello.Length - 1, await client.SendAsync(hello.AsMemory(1), SocketFlags.None, cancellationToken));
        client.Shutdown(SocketShutdown.Send);

        byte[] received = new byte[response.Length];
        int receivedCount = 0;
        while (receivedCount < received.Length)
        {
            int count = await client.ReceiveAsync(received.AsMemory(receivedCount), SocketFlags.None, cancellationToken);
            Assert.True(count > 0);
            receivedCount += count;
        }

        TerrariaSocketRunResult result = await run;

        Assert.Equal(response, received);
        Assert.Equal(1, sink.HelloCount);
        Assert.Equal(TerrariaPipePumpResult.Completed, result.Inbound);
        Assert.Equal(OutboundWriterStopReason.Completed, result.Outbound.Reason);
        Assert.Equal(TerrariaConnectionStopReason.PeerClosed, result.StopReason);
        Assert.Equal(1, result.Outbound.FramesWritten);
        Assert.Equal(response.Length, result.Outbound.BytesWritten);
    }

    [Fact]
    public async Task Disconnects_when_the_connection_outbound_queue_overflows()
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

        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1, 16, 8));
        Assert.Equal(OutboundEnqueueResult.Enqueued, outbound.TryEnqueue(new OutboundFrame(new byte[] { 3, 0, (byte)TerrariaMessageId.Kick })));
        Assert.Equal(OutboundEnqueueResult.FrameBudgetExceeded, outbound.TryEnqueue(new OutboundFrame(new byte[] { 3, 0, (byte)TerrariaMessageId.Kick })));
        Assert.True(outbound.IsSlowClient);

        TerrariaSocketRunResult result = await TerrariaSocketConnection.RunAsync(
            serverSocket,
            new HelloCountingSink(),
            outbound,
            TerrariaFrameDecoderOptions.Default,
            cancellationToken);

        Assert.Equal(TerrariaConnectionStopReason.SlowClient, result.StopReason);
    }

    private static byte[] CurrentHelloPacket() =>
    [
        15, 0,
        (byte)TerrariaMessageId.Hello,
        11,
        (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
        (byte)'3', (byte)'2', (byte)'6'
    ];

    private sealed class HelloCountingSink : ITerrariaFrameSink
    {
        public int HelloCount { get; private set; }

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            if (frame.MessageId == (byte)TerrariaMessageId.Hello)
            {
                HelloCount++;
            }

            return TerrariaFrameSinkResult.Continue;
        }
    }
}
