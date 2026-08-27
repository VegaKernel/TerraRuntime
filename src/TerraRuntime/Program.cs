using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

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

        if (args.Contains("--world-smoke", StringComparer.Ordinal))
        {
            return RunWorldSmoke();
        }

        Console.WriteLine("TerraRuntime .NET 11 NativeAOT-first runtime scaffold. Use --loop-smoke, --protocol-smoke, --network-smoke or --world-smoke for smoke tests.");
        return 0;
    }

    private static int RunLoopSmoke()
    {
        var state = new ServerRuntimeState();
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var ingress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(loop);
        using var workers = new BoundedWorkerPool<int, int>(
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            execute: static value => value * 2);
        using var forwarder = new WorkerCompletionCommandForwarder<int, int, RuntimeCommand>(
            workers,
            ingress,
            static completion => completion.IsSuccess
                ? new WorkerResultCommand(completion.Result)
                : throw new InvalidOperationException("Worker smoke completion failed.", completion.Error));

        loop.Start();
        forwarder.Start();
        workers.Start();

        if (!loop.TryPost(new ProbeCommand()))
        {
            Console.Error.WriteLine("Failed to enqueue loop smoke command.");
            return 2;
        }

        if (!workers.TrySubmit(21))
        {
            Console.Error.WriteLine("Game loop smoke failed while submitting bounded worker work.");
            return 15;
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while ((loop.Snapshot.Tick < 3 || state.LastWorkerResult != 42) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        bool workersStopped = workers.Stop(TimeSpan.FromSeconds(1));
        bool forwarderStopped = forwarder.Stop(TimeSpan.FromSeconds(1));
        bool loopStopped = loop.Stop(TimeSpan.FromSeconds(1));
        GameLoopSnapshot snapshot = loop.Snapshot;

        if (loop.Fault is not null || forwarder.Fault is not null || snapshot.Tick < 3 || state.LastWorkerResult != 42)
        {
            Console.Error.WriteLine(
                $"Game loop smoke failed: tick={snapshot.Tick}, workerResult={state.LastWorkerResult}, " +
                $"loopFault={loop.Fault}, forwarderFault={forwarder.Fault}");
            return 3;
        }

        if (!workersStopped || !forwarderStopped || !loopStopped || forwarder.ForwardedCommands != 1)
        {
            Console.Error.WriteLine("Game loop smoke failed during bounded worker/forwarder shutdown.");
            return 16;
        }

        if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && !snapshot.CpuTimeAvailable)
        {
            Console.Error.WriteLine("Game loop smoke failed: authoritative per-thread CPU clock is unavailable.");
            return 17;
        }

        Console.WriteLine(
            $"Game loop smoke passed: tick={snapshot.Tick}, thread={snapshot.GameThreadId}, " +
            $"wallWorst={snapshot.WorstTickMilliseconds:F3} ms, cpuWorst={snapshot.WorstTickCpuMilliseconds:F3} ms, " +
            $"slowest={snapshot.SlowestLastPhase}:{snapshot.SlowestLastPhaseMilliseconds:F3} ms, " +
            $"missed={snapshot.MissedTickDeadlines}, workerCompleted={workers.Snapshot.CompletedWork}, " +
            $"forwarded={forwarder.ForwardedCommands}");
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

        var rate = new TerrariaConnectionRateAccountant(new ConnectionRateBudgetOptions(
            window: TimeSpan.FromMinutes(1),
            maxFrames: 1,
            maxBytes: null));
        if (rate.Observe(packet.Length) != ConnectionRateDecision.Allowed ||
            rate.Observe(packet.Length) != ConnectionRateDecision.FrameLimitExceeded ||
            rate.Snapshot.RejectedFrames != 1)
        {
            Console.Error.WriteLine("Network smoke failed while exercising connection rate accounting.");
            return 12;
        }

        var commandInput = new ReadOnlySequence<byte>(packet);
        if (TerrariaFrameDecoder.TryRead(ref commandInput, out TerrariaFrame commandFrame) != TerrariaFrameReadResult.Frame)
        {
            Console.Error.WriteLine("Network smoke failed while preparing typed command frame.");
            return 13;
        }

        var commandIngress = new HandshakeCommandIngress();
        var commandSink = new TerrariaCommandFrameSink<HandshakeCommand>(
            GameCommandSourceId.FromConnection(1),
            new HandshakeCommandDecoder(),
            commandIngress);
        if (commandSink.OnFrame(in commandFrame) != TerrariaFrameSinkResult.Continue ||
            commandSink.StopReason != TerrariaCommandFrameSinkStopReason.None ||
            commandIngress.Command is not { ProtocolRelease: TerrariaProtocolVersion.CurrentRelease })
        {
            Console.Error.WriteLine("Network smoke failed while exercising typed game-command ingress.");
            return 14;
        }

        Console.WriteLine("Network smoke passed: fragmented ingress, bounded outbound queues, slow-client policy, admission gate, rate accounting and typed command ingress executed successfully.");
        return 0;
    }

    private static int RunWorldSmoke()
    {
        var dimensions = new WorldDimensions(widthTiles: 421, heightTiles: 301);
        var dirty = new DirtySectionTracker(dimensions);
        if (!dirty.MarkTileDirty(420, 300))
        {
            Console.Error.WriteLine("World smoke failed while marking an edge section dirty.");
            return 18;
        }

        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        if (dirty.Drain(drained) != 1 || drained[0] != new WorldSectionId(2, 2) || dirty.DirtyCount != 0)
        {
            Console.Error.WriteLine("World smoke failed while draining the dirty-section tracker.");
            return 19;
        }

        byte[] file = CreateCurrentCoreWorld();
        WorldFileCoreLoadDiagnostic load = WorldFileCoreLoader.TryLoad(file, maxTileCount: 6, out WorldFileCore? world);
        if (load.Result != WorldFileCoreLoadResult.Loaded ||
            world is null ||
            world.Envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion ||
            world.Envelope.Compatibility != WorldFormatCompatibility.Verified ||
            world.Header.Name != "native-smoke" ||
            world.Header.Dimensions.WidthTiles != 2 ||
            world.Header.Dimensions.HeightTiles != 3 ||
            world.Tiles.Count != 6 ||
            world.Tiles.Get(0, 0).IsActive ||
            world.Tiles.Get(1, 2).IsActive)
        {
            Console.Error.WriteLine(
                $"World smoke failed while loading current .wld core: result={load.Result}, " +
                $"envelope={load.EnvelopeResult}, header={load.HeaderResult}, tiles={load.TileResult}.");
            return 20;
        }

        WorldFileCoreLoadDiagnostic budget = WorldFileCoreLoader.TryLoad(file, maxTileCount: 5, out WorldFileCore? rejected);
        if (budget.Result != WorldFileCoreLoadResult.TileBudgetExceeded || rejected is not null || budget.TileResult is not null)
        {
            Console.Error.WriteLine("World smoke failed while enforcing pre-allocation tile budget.");
            return 21;
        }

        if (WorldFileChestDecoder.TryDecode(
                file,
                world.Envelope,
                world.Header,
                maxItemsPerChest: 40,
                maxTotalItems: 100,
                out WorldChest[] chests,
                out _) != WorldFileChestDecodeResult.Decoded ||
            chests.Length != 0)
        {
            Console.Error.WriteLine("World smoke failed while decoding the current chest section.");
            return 22;
        }

        if (WorldFileSignDecoder.TryDecode(
                file,
                world.Envelope,
                world.Header,
                maxTextBytesPerSign: 256,
                maxTotalTextBytes: 1024,
                out WorldSign[] signs,
                out _) != WorldFileSignDecodeResult.Decoded ||
            signs.Length != 0)
        {
            Console.Error.WriteLine("World smoke failed while decoding the current sign section.");
            return 23;
        }

        Console.WriteLine(
            $"World smoke passed: sections={dimensions.SectionCount}, dirtySection={drained[0]}, " +
            $"wldVersion={world.Envelope.FormatVersion}, world={world.Header.Name}, tiles={world.Tiles.Count}, " +
            $"chests={chests.Length}, signs={signs.Length}.");
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

    private static byte[] CreateCurrentCoreWorld()
    {
        const int envelopeEnd = 167;
        const int headerEnd = 240;
        byte[] tileBytes = [0x40, 0x02, 0x40, 0x02];
        int tileEnd = headerEnd + tileBytes.Length;
        int chestEnd = tileEnd + sizeof(short);
        int signEnd = chestEnd + sizeof(short);
        int[] pointers =
        [
            envelopeEnd,
            headerEnd,
            tileEnd,
            chestEnd,
            signEnd,
            signEnd + 8,
            signEnd + 16,
            signEnd + 24,
            signEnd + 32,
            signEnd + 40,
            signEnd + 48
        ];
        var file = new byte[pointers[^1] + 1];

        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), WorldFileFormatPolicy.CurrentVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 1);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.SectionCount);
        offset += sizeof(short);
        foreach (int pointer in pointers)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.TileTypeCount);
        offset += sizeof(ushort);
        offset += (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        if (offset != envelopeEnd)
        {
            throw new InvalidOperationException("Current .wld smoke envelope size drifted from the verified 1.4.5.8 layout.");
        }

        using (var stream = new MemoryStream(file, writable: true))
        {
            stream.Position = envelopeEnd;
            using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write("native-smoke");
            writer.Write("326");
            writer.Write(1UL);
            writer.Write(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToByteArray());
            writer.Write(7);
            writer.Write(0);
            writer.Write(32);
            writer.Write(0);
            writer.Write(48);
            writer.Write(3);
            writer.Write(2);
            writer.Flush();
            if (stream.Position > headerEnd)
            {
                throw new InvalidOperationException("Current .wld smoke header exceeded its declared section boundary.");
            }
        }

        tileBytes.CopyTo(file, headerEnd);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(tileEnd), 0);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(chestEnd), 0);
        return file;
    }

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

    private sealed record HandshakeCommand(int ProtocolRelease);

    private sealed class HandshakeCommandDecoder : ITerrariaCommandDecoder<HandshakeCommand>
    {
        public TerrariaCommandDecodeResult TryDecode(in TerrariaFrame frame, out HandshakeCommand command)
        {
            ConnectRequestDecodeResult result = TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request);
            if (result == ConnectRequestDecodeResult.WrongMessageId)
            {
                command = default!;
                return TerrariaCommandDecodeResult.Ignored;
            }

            if (result != ConnectRequestDecodeResult.Decoded)
            {
                command = default!;
                return TerrariaCommandDecodeResult.Malformed;
            }

            command = new HandshakeCommand(request.ProtocolRelease);
            return TerrariaCommandDecodeResult.Decoded;
        }
    }

    private sealed class HandshakeCommandIngress : IGameCommandIngress<HandshakeCommand>
    {
        public HandshakeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, HandshakeCommand command)
        {
            if (source.IsSystem)
            {
                return false;
            }

            Command = command;
            return true;
        }
    }
}
