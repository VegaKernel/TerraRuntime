using System.Buffers;
using System.IO.Pipelines;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--loop-smoke", StringComparer.Ordinal))
        {
            return RunLoopSmoke();
        }

        if (args.Contains("--protocol-smoke", StringComparer.Ordinal))
        {
            return RunProtocolSmoke();
        }

        if (args.Contains("--network-smoke", StringComparer.Ordinal))
        {
            return RunNetworkSmokeAsync().GetAwaiter().GetResult();
        }

        Console.WriteLine("TerraRuntime .NET 11 NativeAOT-first runtime scaffold. Use --loop-smoke, --protocol-smoke or --network-smoke for smoke tests.");
        return 0;
    }

    private static int RunLoopSmoke()
    {
        var state = new ServerRuntimeState();
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());

        loop.Start();
        if (!loop.TryPost(new ProbeCommand()))
        {
            Console.Error.WriteLine("Failed to enqueue loop smoke command.");
            return 2;
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (loop.Snapshot.Tick < 3 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        loop.Stop(TimeSpan.FromSeconds(1));
        var snapshot = loop.Snapshot;
        if (loop.Fault is not null || snapshot.Tick < 3)
        {
            Console.Error.WriteLine($"Game loop smoke failed: tick={snapshot.Tick}, fault={loop.Fault}");
            return 3;
        }

        Console.WriteLine($"Game loop smoke passed: tick={snapshot.Tick}, thread={snapshot.GameThreadId}, worst={snapshot.WorstTickMilliseconds:F3} ms");
        return 0;
    }

    private static int RunProtocolSmoke()
    {
        byte[] packet = CreateCurrentHelloPacket();
        var input = new ReadOnlySequence<byte>(packet);

        if (TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame) != TerrariaFrameReadResult.Frame ||
            TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request) != ConnectRequestDecodeResult.Decoded ||
            !request.IsCurrentProtocol ||
            !input.IsEmpty)
        {
            Console.Error.WriteLine("Protocol smoke failed while decoding Terraria326 handshake.");
            return 4;
        }

        var output = new ArrayBufferWriter<byte>();
        if (TerrariaFrameEncoder.TryWrite(output, frame.MessageId, packet.AsSpan(TerrariaFrameDecoderOptions.MinimumFrameLength)) != TerrariaFrameWriteResult.Written ||
            !output.WrittenSpan.SequenceEqual(packet))
        {
            Console.Error.WriteLine("Protocol smoke failed while encoding Terraria326 handshake.");
            return 5;
        }

        Console.WriteLine($"Protocol smoke passed: release={request.ProtocolRelease}, frameLength={frame.PacketLength}.");
        return 0;
    }

    private static async ValueTask<int> RunNetworkSmokeAsync()
    {
        var pipe = new Pipe();
        var sink = new HandshakeSmokeSink();
        ValueTask<TerrariaPipePumpResult> pump = TerrariaPipeFramePump.RunAsync(pipe.Reader, sink);
        byte[] packet = CreateCurrentHelloPacket();

        await pipe.Writer.WriteAsync(packet.AsMemory(0, 1)).ConfigureAwait(false);
        await pipe.Writer.WriteAsync(packet.AsMemory(1)).ConfigureAwait(false);
        await pipe.Writer.CompleteAsync().ConfigureAwait(false);

        TerrariaPipePumpResult result = await pump.ConfigureAwait(false);
        await pipe.Reader.CompleteAsync().ConfigureAwait(false);

        if (result != TerrariaPipePumpResult.Completed || !sink.DecodedCurrentProtocol)
        {
            Console.Error.WriteLine($"Network smoke failed: pump={result}, decoded={sink.DecodedCurrentProtocol}.");
            return 6;
        }

        var outbound = new BoundedOutboundQueue(new OutboundQueueOptions(
            maxFrames: 2,
            maxQueuedBytes: 64,
            maxFrameBytes: 32));

        if (outbound.TryEnqueue(new OutboundFrame(packet)) != OutboundEnqueueResult.Enqueued)
        {
            Console.Error.WriteLine("Network smoke failed while enqueueing outbound frame.");
            return 7;
        }

        OutboundFrame dequeued = await outbound.ReadAsync().ConfigureAwait(false);
        if (!dequeued.Bytes.Span.SequenceEqual(packet) || outbound.QueuedFrames != 0 || outbound.QueuedBytes != 0)
        {
            Console.Error.WriteLine("Network smoke failed while draining outbound frame queue.");
            return 8;
        }

        outbound.Complete();

        var connectionQueue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(
            maxFrames: 1,
            maxQueuedBytes: 32,
            maxFrameBytes: 16));
        if (connectionQueue.TryEnqueue(new OutboundFrame(packet)) != OutboundEnqueueResult.Enqueued ||
            connectionQueue.TryEnqueue(new OutboundFrame(packet)) != OutboundEnqueueResult.FrameBudgetExceeded ||
            !connectionQueue.IsSlowClient)
        {
            Console.Error.WriteLine("Network smoke failed while applying slow-client queue policy.");
            return 9;
        }

        connectionQueue.Complete();

        var admission = new TerrariaConnectionAdmissionGate(maxConnections: 1);
        if (!admission.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? lease) ||
            admission.TryAcquire(out _))
        {
            Console.Error.WriteLine("Network smoke failed while exercising connection admission gate.");
            return 10;
        }

        lease!.Dispose();
        if (admission.ActiveConnections != 0 || admission.AcceptedConnections != 1 || admission.RejectedConnections != 1)
        {
            Console.Error.WriteLine("Network smoke failed while verifying connection admission counters.");
            return 11;
        }

        Console.WriteLine("Network smoke passed: fragmented ingress, bounded outbound queues, slow-client policy and admission gate executed successfully.");
        return 0;
    }

    private static byte[] CreateCurrentHelloPacket() =>
    [
        15, 0,
        (byte)TerrariaMessageId.Hello,
        11,
        (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
        (byte)'3', (byte)'2', (byte)'6'
    ];

    private sealed class HandshakeSmokeSink : ITerrariaFrameSink
    {
        public bool DecodedCurrentProtocol { get; private set; }

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            DecodedCurrentProtocol =
                TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request) == ConnectRequestDecodeResult.Decoded &&
                request.IsCurrentProtocol;
            return TerrariaFrameSinkResult.Continue;
        }
    }
}
