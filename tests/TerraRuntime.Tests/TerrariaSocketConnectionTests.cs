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
        Assert.True(outbound.IsCompleted);
    }

    [Fact]
    public async Task Keeps_connection_alive_after_handshake_when_idle_timeout_is_infinite()
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
        var sink = new HelloCountingSink();
        var policy = new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromMilliseconds(50),
            idleTimeout: Timeout.InfiniteTimeSpan);

        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            policy,
            cancellationToken).AsTask();

        byte[] hello = CurrentHelloPacket();
        Assert.Equal(hello.Length, await client.SendAsync(hello, SocketFlags.None, cancellationToken));

        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        Assert.False(run.IsCompleted);
        Assert.Equal(1, sink.HelloCount);

        client.Shutdown(SocketShutdown.Send);
        TerrariaSocketRunResult result = await run;

        Assert.Equal(TerrariaPipePumpResult.Completed, result.Inbound);
        Assert.Equal(OutboundWriterStopReason.Completed, result.Outbound.Reason);
        Assert.Equal(TerrariaConnectionStopReason.PeerClosed, result.StopReason);
        Assert.True(outbound.IsCompleted);
    }

    [Fact]
    public async Task Disconnects_when_handshake_deadline_expires()
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
        var sink = new HelloCountingSink();
        var policy = new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromMilliseconds(200),
            idleTimeout: Timeout.InfiniteTimeSpan);
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            policy,
            connectionCancellation.Token).AsTask();

        TerrariaSocketRunResult result;
        try
        {
            result = await run.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        }
        finally
        {
            if (!run.IsCompleted)
            {
                connectionCancellation.Cancel();
                await run;
            }
        }

        Assert.Equal(0, sink.HelloCount);
        Assert.Equal(TerrariaPipePumpResult.Cancelled, result.Inbound);
        Assert.Equal(OutboundWriterStopReason.Cancelled, result.Outbound.Reason);
        Assert.Equal(TerrariaConnectionStopReason.HandshakeTimeout, result.StopReason);
        Assert.True(outbound.IsCompleted);
        await AssertPeerClosedAsync(client, cancellationToken);
    }

    [Fact]
    public async Task Starts_idle_deadline_immediately_after_handshake()
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
        var sink = new HelloCountingSink();
        var policy = new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(10),
            idleTimeout: TimeSpan.FromMilliseconds(250));
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            policy,
            connectionCancellation.Token).AsTask();

        byte[] hello = CurrentHelloPacket();
        Assert.Equal(hello.Length, await client.SendAsync(hello, SocketFlags.None, cancellationToken));

        TerrariaSocketRunResult result;
        try
        {
            result = await run.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        }
        finally
        {
            if (!run.IsCompleted)
            {
                connectionCancellation.Cancel();
                await run;
            }
        }

        Assert.Equal(1, sink.HelloCount);
        Assert.Equal(TerrariaPipePumpResult.Cancelled, result.Inbound);
        Assert.Equal(OutboundWriterStopReason.Cancelled, result.Outbound.Reason);
        Assert.Equal(TerrariaConnectionStopReason.IdleTimeout, result.StopReason);
        Assert.True(outbound.IsCompleted);
        await AssertPeerClosedAsync(client, cancellationToken);
    }

    [Fact]
    public async Task Cancellation_completes_the_outbound_queue_and_closes_the_peer_socket()
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
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            new HelloCountingSink(),
            outbound,
            TerrariaFrameDecoderOptions.Default,
            connectionCancellation.Token).AsTask();

        connectionCancellation.Cancel();
        TerrariaSocketRunResult result = await run;

        Assert.Equal(TerrariaPipePumpResult.Cancelled, result.Inbound);
        Assert.Equal(OutboundWriterStopReason.Cancelled, result.Outbound.Reason);
        Assert.Equal(TerrariaConnectionStopReason.Cancelled, result.StopReason);
        Assert.True(outbound.IsCompleted);
        await AssertPeerClosedAsync(client, cancellationToken);
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
        Assert.True(outbound.IsCompleted);
    }

    private static async Task AssertPeerClosedAsync(Socket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1];
        try
        {
            int received = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
            Assert.Equal(0, received);
        }
        catch (SocketException exception) when (exception.SocketErrorCode is
            SocketError.ConnectionReset or
            SocketError.ConnectionAborted or
            SocketError.Shutdown)
        {
            // Shutdown/dispose may surface as EOF or a reset depending on the platform TCP stack.
        }
    }

    [Fact]
    public async Task A_departed_peer_cannot_hold_the_connection_open_by_never_draining_its_backlog()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var endpoint = (IPEndPoint)listener.LocalEndPoint!;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        // Tiny windows on both sides so the pending backlog cannot simply disappear into kernel buffers.
        client.ReceiveBufferSize = 512;
        ValueTask connectTask = client.ConnectAsync(endpoint, cancellationToken);
        Socket serverSocket = await listener.AcceptAsync(cancellationToken);
        await connectTask;
        serverSocket.SendBufferSize = 512;

        const int frameBytes = 8 * 1024;
        const int frameCount = 2048;
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(frameCount, (long)frameCount * frameBytes, frameBytes));
        byte[] payload = new byte[frameBytes];
        payload[0] = unchecked((byte)(frameBytes - 2));
        payload[1] = (byte)((frameBytes - 2) >> 8);
        payload[2] = (byte)TerrariaMessageId.Kick;
        for (int i = 0; i < frameCount; i++)
        {
            // Every enqueue must succeed: a rejected frame would classify the peer as slow and take a
            // different teardown branch, which is not what this test is about.
            Assert.Equal(OutboundEnqueueResult.Enqueued, outbound.TryEnqueue(new OutboundFrame(payload)));
        }

        var sink = new HelloCountingSink();
        ValueTask<TerrariaSocketRunResult> run = TerrariaSocketConnection.RunAsync(
            serverSocket,
            sink,
            outbound,
            TerrariaFrameDecoderOptions.Default,
            cancellationToken);

        byte[] hello = CurrentHelloPacket();
        Assert.Equal(hello.Length, await client.SendAsync(hello, SocketFlags.None, cancellationToken));
        // The peer closes its send side and then never reads another byte, exactly like a client whose process
        // is gone while the server still holds a large outbound backlog for it.
        client.Shutdown(SocketShutdown.Send);

        Task<TerrariaSocketRunResult> runTask = run.AsTask();
        Task completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(20), cancellationToken));
        Assert.True(
            ReferenceEquals(completed, runTask),
            "A peer that stopped reading must not keep the connection - and with it the player slot and the " +
            "reserved player name - alive by refusing to drain its outbound backlog.");

        TerrariaSocketRunResult result = await runTask;
        Assert.Equal(TerrariaConnectionStopReason.PeerClosed, result.StopReason);
        Assert.Equal(TerrariaPipePumpResult.Completed, result.Inbound);
        Assert.True(result.Outbound.BytesWritten < (long)frameCount * frameBytes);
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
