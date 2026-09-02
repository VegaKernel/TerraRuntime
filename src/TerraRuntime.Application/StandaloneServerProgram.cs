using System.Buffers;
using System.IO.Pipelines;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.TerminalUI;
using TerraRuntime.World;

namespace TerraRuntime;

internal static class StandaloneServerProgram
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
            return WorldNativeSmoke.Run();
        }

        if (args.Contains("--tui-smoke", StringComparer.Ordinal))
        {
            return TerminalUiSmoke.Run();
        }

        int saveWldIndex = Array.FindIndex(
            args,
            static value => string.Equals(value, "--save-wld", StringComparison.Ordinal));
        if (saveWldIndex >= 0)
        {
            if (saveWldIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[saveWldIndex + 1]))
            {
                Console.Error.WriteLine("Usage: TerraRuntime.Server --save-wld <path.wld>");
                return 29;
            }

            string worldPath = args[saveWldIndex + 1];
            string cachePath = RuntimeWorldSnapshotCache.GetCachePath(worldPath);
            RuntimeWorldCheckpointSaveDiagnostic save = WorldCheckpointExporter.TryExport(
                cachePath,
                worldPath,
                ServerWorldLoadPolicy.CreateLimits());
            if (!save.IsSaved)
            {
                Console.Error.WriteLine(
                    $"Canonical .wld checkpoint save failed: result={save.Result}, code={save.DetailCode}, cache='{cachePath}'.");
                return 30;
            }

            Console.WriteLine(
                $"Canonical .wld checkpoint saved atomically: '{worldPath}', cache='{cachePath}', result={save.Result}.");
            return 0;
        }

        if (args.Contains("--world", StringComparer.Ordinal))
        {
            if (!ServerHostOptions.TryParse(args, out ServerHostOptions? options, out string? error) || options is null)
            {
                Console.Error.WriteLine(error ?? "Invalid server host options.");
                Console.Error.WriteLine("Usage: TerraRuntime.Server --world <path.wld> [--port 7777] [--max-players 8] [--interest-management] [--tui]");
                return 23;
            }

            AtomicSaveFileRecoveryDiagnostic interruptedRecovery =
                AtomicSaveFileWriter.RecoverAbandonedWrites(options.WorldPath);

            if (interruptedRecovery.IoFailed)
            {
                Console.Error.WriteLine(
                    $"Interrupted world-save recovery could not inspect managed transactions safely: '{options.WorldPath}'.");
                return 26;
            }

            if (interruptedRecovery.LiveWrites != 0)
            {
                Console.Error.WriteLine(
                    $"Refusing world startup while another managed save writer still owns a live lease: '{options.WorldPath}', " +
                    $"live={interruptedRecovery.LiveWrites}.");
                return 26;
            }

            if (interruptedRecovery.SuppressedWrites != 0)
            {
                Console.Error.WriteLine(
                    $"Interrupted world-save recovery found a durable transaction whose publication preconditions no longer match; " +
                    $"the candidate was quarantined and startup is blocked: '{options.WorldPath}', " +
                    $"suppressed={interruptedRecovery.SuppressedWrites}.");
                return 26;
            }

            if (interruptedRecovery.RecoveredWrites != 0)
            {
                Console.WriteLine(
                    $"Interrupted world save recovered from durable marker: '{options.WorldPath}', " +
                    $"recovered={interruptedRecovery.RecoveredWrites}, removed={interruptedRecovery.RemovedWrites}.");
            }
            else if (interruptedRecovery.RemovedWrites != 0)
            {
                Console.WriteLine(
                    $"Discarded unsealed or invalid interrupted world-save transactions before startup: '{options.WorldPath}', " +
                    $"removed={interruptedRecovery.RemovedWrites}.");
            }

            return TerrariaServerHost.RunAsync(options).GetAwaiter().GetResult();
        }

        Console.WriteLine(
            "TerraRuntime .NET 11 server runtime. " +
            "Start with --world <path.wld> [--port 7777] [--max-players 8] [--interest-management] [--tui], " +
            "restore the compatible checkpoint with --save-wld <path.wld>, " +
            "or use --loop-smoke, --protocol-smoke, --network-smoke, --world-smoke or --tui-smoke.");
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

        if (!AuthoritativePlayerSpawnSmoke.Run(out string spawnFailure))
        {
            Console.Error.WriteLine($"Game loop smoke failed during authoritative player spawn commit: {spawnFailure}.");
            return 22;
        }

        if (!RuntimeActorCommerceSmoke.Run(out string actorFailure))
        {
            Console.Error.WriteLine($"Game loop smoke failed during runtime actor/commerce coverage: {actorFailure}.");
            return 31;
        }

        Console.WriteLine(
            $"Game loop smoke passed: tick={snapshot.Tick}, thread={snapshot.GameThreadId}, " +
            $"wallWorst={snapshot.WorstTickMilliseconds:F3} ms, cpuWorst={snapshot.WorstTickCpuMilliseconds:F3} ms, " +
            $"slowest={snapshot.SlowestLastPhase}:{snapshot.SlowestLastPhaseMilliseconds:F3} ms, " +
            $"missed={snapshot.MissedTickDeadlines}, workerCompleted={workers.Snapshot.CompletedWork}, " +
            $"forwarded={forwarder.ForwardedCommands}, spawnCommit=ok, actorCommerce=ok");
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
