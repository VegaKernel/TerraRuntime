using System.Buffers;
using TerraRuntime.Core;
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

        Console.WriteLine("TerraRuntime .NET 11 NativeAOT-first runtime scaffold. Use --loop-smoke or --protocol-smoke for smoke tests.");
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
        byte[] packet =
        [
            15, 0,
            (byte)TerrariaMessageId.Hello,
            11,
            (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
            (byte)'3', (byte)'2', (byte)'6'
        ];
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
}
