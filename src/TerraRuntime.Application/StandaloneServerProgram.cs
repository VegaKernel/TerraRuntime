using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol.Multiplicity;
using System.IO.Pipelines;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Application.TerminalUI;
using TerraRuntime.World;

namespace TerraRuntime.Application;

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
                Console.Error.WriteLine("Usage: TerraRuntime.Server --world <path.wld> [--bind 0.0.0.0] [--port 7777] [--max-players 8] [--interest-management] [--tui]");
                return 23;
            }

            return TerrariaServerHost.RunAsync(options).GetAwaiter().GetResult();
        }

        Console.WriteLine(
            "TerraRuntime .NET 11 server runtime. " +
            "Start with --world <path.wld> [--bind 0.0.0.0] [--port 7777] [--max-players 8] [--interest-management] [--tui], " +
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

        var npcs = new RuntimeNpcStore();
        // Original ProjectileKey bit carriers: finite, infinities, NaNs, and omitted negative zero.
        ReadOnlySpan<uint> anchorKeys = [0x000400ff, 0x7f800000, 0x7fc000ff, 0xff800000, 0xffc3e8ff, 0x80000000];
        foreach (uint bits in anchorKeys)
        {
            float anchor = BitConverter.UInt32BitsToSingle(bits);
            int type = VanillaNpcIds.MoonLordLeechBlob.Value;
            var npc = new NpcStateUpdate(type, (short)type, 100, 200, 0, 0, 0, new NpcAiState(0, anchor, 0, 0),
                NpcSimulationState.Initial with { Life = 400, LifeMax = 400 });
            var wire = new TerrariaNpcUpdateState(0, 1, type, 100, 200, 0, 0, 0, 1, 1, 1,
                0, anchor, 0, 0, (short)type, 400, 400, true);
            if (!npcs.TrySpawn(0, in npc, out var stored) ||
                BitConverter.SingleToUInt32Bits(stored.Ai.Ai1) != bits ||
                !TerrariaNpcUpdateEncoder.TryEncode(in wire, out byte[] encoded) ||
                (bits == 0x80000000 ? encoded.Length != 27 :
                    encoded.Length < 29 || BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(25, 4)) != bits))
            {
                Console.Error.WriteLine("Protocol smoke failed while preserving the NPC projectile anchor.");
                return 5;
            }
            npcs.TryDespawn(stored.Handle);
        }

        ushort wrappedGeneration = RuntimeProjectilePacketProjection.ToProtocolGeneration(new ProjectileGeneration(16384));
        var wrappedKey = new TerrariaProjectileKeyState(255, 0, wrappedGeneration);
        var projectile = new TerrariaProjectileUpdateState(wrappedKey, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (wrappedGeneration != 0 || !TerrariaProjectileEncoder.TryEncodeUpdate(in projectile, out var wrappedPacket) ||
            BinaryPrimitives.ReadUInt32LittleEndian(wrappedPacket.AsSpan(3)) != 255)
        {
            Console.Error.WriteLine("Protocol smoke failed while wrapping the projectile generation through zero.");
            return 5;
        }
        var wrappedInput = new ReadOnlySequence<byte>(wrappedPacket);
        if (TerrariaFrameDecoder.TryRead(ref wrappedInput, out var wrappedFrame) != TerrariaFrameReadResult.Frame ||
            TerrariaProjectileDecoder.TryDecodeUpdate(in wrappedFrame, out var decodedProjectile) != TerrariaProjectileDecodeResult.Decoded ||
            decodedProjectile.Key != wrappedKey)
        {
            Console.Error.WriteLine("Protocol smoke failed while decoding the zero-generation projectile key.");
            return 5;
        }

        byte[] healPacket = TerrariaCombatTextCodec.EncodeNumber(1019f, 1028f, 1000, new TerrariaRgbColor(100, 255, 100));
        ReadOnlySpan<byte> originalHealPacket = [0x12, 0, 0x51, 0, 0xc0, 0x7e, 0x44, 0, 0x80, 0x80, 0x44, 0x64, 0xff, 0x64, 0xe8, 3, 0, 0];
        var leechNpcs = new RuntimeNpcStore(5);
        var headState = new NpcStateUpdate(396, 396, 1000, 1000, 0, 0, 0, new NpcAiState(0, 0, 0, 1),
            NpcSimulationState.Initial with { Life = 44000, LifeMax = 45000 });
        var clotState = new NpcStateUpdate(401, 401, 300, 400, 0, 0, 0, new NpcAiState(1, 0, 89, 0),
            NpcSimulationState.Initial with { Life = 400, LifeMax = 400 });
        leechNpcs.TrySpawn(0, in headState, out var leechHead);
        var coreState = headState with { Type = 398, NetId = 398,
            Simulation = NpcSimulationState.Initial with { Life = 50000, LifeMax = 50000 } };
        var handState = headState with { Type = 397, NetId = 397,
            Simulation = NpcSimulationState.Initial with { Life = 25000, LifeMax = 25000 } };
        leechNpcs.TrySpawn(1, in coreState, out _);
        leechNpcs.TrySpawn(2, in handState, out _);
        handState = handState with { Ai = handState.Ai with { Ai2 = 1 } };
        leechNpcs.TrySpawn(3, in handState, out _);
        leechNpcs.TrySpawn(4, in clotState, out var leechClot);
        var leechAi = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        var leechMotion = new VanillaNpcWorldMotionAiStepper(leechAi, new WorldTileStore(new WorldDimensions(100, 100)));
        new RuntimeNpcAiStateExecutor(leechNpcs).Tick(new MoonLeechSmokeStepper(leechMotion));
        if (!healPacket.AsSpan().SequenceEqual(originalHealPacket) ||
            !leechNpcs.TryGet(leechHead.Handle, out var healedHead) || healedHead.Simulation.Life != 45000 ||
            leechNpcs.TryGet(leechClot.Handle, out _))
        {
            Console.Error.WriteLine("Protocol smoke failed while committing Moon Leech healing and deactivation.");
            return 5;
        }

        var leechPlayers = new PlayerAuthority(null, null);
        var leechConnection = new ConnectionHandle(GameCommandSourceId.FromConnection(1),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var leechBuff = new PlayerBuffTypesCommitRequest(leechConnection.Player.Slot, new BuffTypeId[] { VanillaBuffIds.MoonLeech });
        var leechProjectiles = new RuntimeProjectileStore(1);
        var leechIdentities = new RuntimeProjectileWireIdentityRegistry(1);
        var tongueState = new ProjectileStateUpdate(VanillaProjectileIds.MoonLeech, 255, 1200, 1000,
            0, 0, new ProjectileAiState(-6, 0, 0), 0, 0, 0, 0);
        var tongueKey = new TerrariaProjectileKeyState(255, 0, 1);
        headState = headState with { Ai = new NpcAiState(0, 329, 0, 1) };
        if (!leechPlayers.TryApply(new PlayerBuffTypesRuntimeCommand(leechConnection, leechBuff)) ||
            !leechProjectiles.TrySpawn(0, in tongueState, out var tongue) ||
            !leechIdentities.TryBind(in tongueKey, tongue.Handle) ||
            !leechNpcs.TryUpdate(leechHead.Handle, in headState, out _)) return 5;
        leechAi.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        leechAi.SetProjectileAnchors(new RuntimeNpcProjectileAnchors(leechProjectiles, leechIdentities, leechPlayers));
        new RuntimeNpcAiStateExecutor(leechNpcs).Tick(new MoonLeechSmokeStepper(leechMotion, includeHead: true));
        if (!leechNpcs.TryGetActive(4, out var createdClot) ||
            createdClot.TypeIdentity != VanillaNpcIds.MoonLordLeechBlob || createdClot.Ai.Ai2 != 1f ||
            BitConverter.SingleToUInt32Bits(createdClot.Ai.Ai1) != 0x000400ff)
        {
            Console.Error.WriteLine("Protocol smoke failed while creating a healing clot from the head attack.");
            return 5;
        }

        var allocation = new RuntimeNpcStore();
        var queen = headState with { Type = VanillaNpcIds.QueenBee.Value, NetId = (short)VanillaNpcIds.QueenBee.Value };
        var worm = headState with { Type = 7, NetId = 7 };
        if (!allocation.TrySpawnVanilla(in queen, out var allocatedQueen) || allocatedQueen.Handle.Slot != 199 ||
            !allocation.TrySpawnVanilla(in worm, out var allocatedWorm) || allocatedWorm.Handle.Slot != 1 ||
            !allocation.TryDespawn(allocatedWorm.Handle) ||
            !allocation.TrySpawnVanilla(in worm, out var nextWorm) || nextWorm.Handle.Slot != 2)
        {
            Console.Error.WriteLine("Protocol smoke failed while selecting vanilla NPC slots.");
            return 5;
        }
        allocation.UpdateProtectedSpawnSlots();
        allocation.UpdateProtectedSpawnSlots();
        if (!allocation.TrySpawnVanilla(in worm, out var reusedWorm) || reusedWorm.Handle.Slot != 1 ||
            reusedWorm.Handle.Generation == allocatedWorm.Handle.Generation) return 5;
        if (!allocation.TrySpawnVanilla(in worm, out var offsetWorm, startSlot: 197) || offsetWorm.Handle.Slot != 197 ||
            allocation.TrySpawnVanilla(in queen, out _, startSlot: int.MaxValue)) return 5;

        var destroyerNpcs = new RuntimeNpcStore();
        if (!destroyerNpcs.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 0)
            { StartSlot = 10 }, out _)) return 5;
        var destroyerAi = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        destroyerAi.SetWorldConditions(false, false);
        destroyerAi.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        var destroyerMotion = new VanillaNpcWorldMotionAiStepper(destroyerAi, new WorldTileStore(new WorldDimensions(400, 400)));
        var destroyerTick = new RuntimeNpcAiStateExecutor(destroyerNpcs).Tick(destroyerMotion);
        if (destroyerTick.Applied != 82 || !destroyerNpcs.TryGetActive(91, out var tail) ||
            tail.TypeIdentity != VanillaNpcIds.DestroyerTail || tail.Ai != new NpcAiState(0, 90, 0, 10))
        {
            Console.Error.WriteLine("Protocol smoke failed while creating the Destroyer chain in source slot order.");
            return 5;
        }

        var scaledNpcs = new RuntimeNpcStore();
        var scaledClock = new RuntimeWorldClock(0, false, default, 0, 1, getGoodWorld: true);
        var scaledRuntime = new ServerRuntimeState(npcs: scaledNpcs, expertMode: true, worldClock: scaledClock,
            worldTiles: new WorldTileStore(new WorldDimensions(400, 400)));
        if (!scaledNpcs.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 255)
            { StartSlot = 10 }, out var scaledHead) || scaledHead.Simulation.LifeMax != 153000 ||
            scaledHead.Simulation.Scale != 1.70625f || scaledHead.Simulation.HitboxOverride != new NpcHitboxDimensions(76, 76) ||
            scaledHead.Simulation.DamageOverride != 420) return 5;
        scaledRuntime.Tick();
        if (!scaledNpcs.TryGetActive(11, out var scaledBody) || scaledBody.Simulation.LifeMax != 153000 ||
            scaledBody.Simulation.HitboxOverride != new NpcHitboxDimensions(76, 76)) return 5;

        var primeNpcs = new RuntimeNpcStore();
        primeNpcs.SetVanillaSpawnContextSource(() => new(2, 1, false));
        if (!primeNpcs.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
            { InitialAi = new NpcAiState(1, 1, 399, 0) }, out var prime)) return 5;
        var primeAi = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        primeAi.SetWorldConditions(false, false, expertMode: true);
        primeAi.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        var primeExecutor = new RuntimeNpcAiStateExecutor(primeNpcs);
        primeExecutor.Tick(primeAi);
        if (!primeNpcs.TryGet(prime.Handle, out var spinningPrime) || spinningPrime.Simulation.DamageOverride != 160 ||
            spinningPrime.Simulation.BaseDamage != 80) return 5;
        primeExecutor.Tick(primeAi);
        if (!primeNpcs.TryGet(prime.Handle, out var hoveringPrime) || hoveringPrime.Simulation.DamageOverride != 80) return 5;

        var armNpcs = new RuntimeNpcStore();
        armNpcs.SetVanillaSpawnContextSource(() => new(3, 1, true));
        var armHeadState = new NpcStateUpdate(127, 127, -0.75f, -0.75f, 0, 0, 0, default, NpcSimulationState.Initial);
        if (!armNpcs.TrySpawnVanilla(in armHeadState, out var armHead, startSlot: 10)) return 5;
        primeAi.SetWorldConditions(false, false, goodWorld: true, expertMode: true, masterMode: true);
        var primeMotion = new VanillaNpcWorldMotionAiStepper(primeAi, new WorldTileStore(new WorldDimensions(400, 400)));
        if (!primeMotion.TryStepState(in armHead, out var armHeadNext)) return 5;
        Span<NpcAiSpawnIntent> armIntents = stackalloc NpcAiSpawnIntent[4];
        if (primeAi.PlanNpcSpawns(in armHead, in armHeadNext, armIntents) != 4) return 5;
        for (int i = 0; i < armIntents.Length; i++)
        {
            if (!armNpcs.TrySpawnIntent(in armIntents[i], out var arm) || arm.Handle.Slot != 11 + i ||
                arm.PositionX != 14.5f || arm.PositionY != -1f || arm.Ai.Ai1 != 10f) return 5;
        }

        var handNpcs = new RuntimeNpcStore();
        handNpcs.SetVanillaSpawnContextSource(() => new(3, 1, true));
        var handHeadState = armHeadState with { Type = 35, NetId = 35 };
        if (!handNpcs.TrySpawnVanilla(in handHeadState, out var handHead, startSlot: 10) ||
            handHead.Simulation.LifeMax != 11220 || handHead.Simulation.BaseDamage != 106 ||
            !primeMotion.TryStepState(in handHead, out var handHeadNext) ||
            primeAi.PlanNpcSpawns(in handHead, in handHeadNext, armIntents) != 2) return 5;
        for (int i = 0; i < 2; i++)
        {
            if (!handNpcs.TrySpawnIntent(in armIntents[i], out var hand) || hand.Handle.Slot != 11 + i ||
                hand.PositionX != 19.5f || hand.PositionY != 4f || hand.Simulation.LifeMax != 1989 ||
                hand.Simulation.BaseDamage != 66) return 5;
        }

        var spinState = handHeadState with { Ai = new NpcAiState(1, 1, 399, 0), Simulation = handHead.Simulation };
        if (!handNpcs.TryUpdate(handHead.Handle, in spinState, out _)) return 5;
        var handExecutor = new RuntimeNpcAiStateExecutor(handNpcs);
        handExecutor.Tick(primeMotion);
        if (!handNpcs.TryGet(handHead.Handle, out var spinningHead) || spinningHead.Simulation.DamageOverride != 137 ||
            spinningHead.Simulation.SpawnDifficulty != 3f || !spinningHead.Simulation.ReflectsProjectiles) return 5;
        handExecutor.Tick(primeMotion);
        if (!handNpcs.TryGet(handHead.Handle, out var restingHead) || restingHead.Simulation.DamageOverride != 106 ||
            restingHead.Simulation.ReflectsProjectiles) return 5;

        var initialNpcs = new RuntimeNpcStore();
        initialNpcs.SetVanillaSpawnContextSource(() => new(2, 1, false));
        if (!initialNpcs.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = 10 }, out var initialHead)) return 5;
        var initialProjectiles = new RuntimeProjectileStore();
        primeAi.SetWorldConditions(false, false, expertMode: true);
        primeAi.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(new WorldTileStore(new WorldDimensions(400, 400))));
        if (new RuntimeNpcAiStateExecutor(initialNpcs, initialProjectiles).Tick(primeMotion).Applied != 3 ||
            !initialNpcs.TryGet(initialHead.Handle, out var initializedHead) || initializedHead.Simulation.DefenseOverride != 60 ||
            initializedHead.Ai.Ai2 != 1f || initialProjectiles.ActiveCount != 0) return 5;

        if (!handNpcs.TryGetActive(11, out var handProbe)) return 5;
        var frozenHand = handProbe with { PositionX = 900, PositionY = 600, VelocityX = 0, VelocityY = 0, Ai = new(-1, 10, 1, 0) };
        var frozenParent = restingHead with { PositionX = 950, PositionY = 873, Ai = new(1, 0, 0, 0) };
        primeAi.SetWorldConditions(false, false, goodWorld: true, expertMode: true, masterMode: true);
        primeAi.SetNpcPeers([frozenParent, frozenHand]);
        if (!primeAi.TryStepState(in frozenHand, out var handDash) || handDash.Ai.Ai2 != 2f ||
            handDash.VelocityX != 17.410515f || handDash.VelocityY != 11.741975f ||
            handDash.Simulation.SpriteDirection != 1 || handDash.Simulation.DirectionY != 1) return 5;

        frozenParent = frozenParent with { Ai = frozenParent.Ai with { Ai3 = 1 } };
        primeAi.SetNpcPeers([frozenParent, frozenHand]);
        if (!primeAi.TryStepState(in frozenHand, out var redHatDash) || redHatDash.Ai.Ai2 != 2f ||
            redHatDash.VelocityX != 19.897732f || redHatDash.VelocityY != 13.4194f ||
            redHatDash.Simulation.DamageOverride != 85) return 5;
        var orphanNpcs = new RuntimeNpcStore();
        if (!orphanNpcs.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { StartSlot = 11, InitialAi = new(-1, 10, 50, 0) }, out var orphanHand) ||
            new RuntimeNpcAiStateExecutor(orphanNpcs).Tick(primeMotion).Applied != 1 ||
            orphanNpcs.TryGet(orphanHand.Handle, out _)) return 5;

        var redHatNpcs = new RuntimeNpcStore();
        if (!redHatNpcs.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { InitialLocalAi = new(0, 0, 0, 1) }, out var redHatHand)) return 5;
        var redHatHit = new NpcDamageRequest(redHatHand.Handle, DamageSource.Server, 100, Critical: true);
        if (!new RuntimeNpcDamageExecutor(redHatNpcs).TryApply(in redHatHit, out var redHatDamage) ||
            redHatDamage.ResolvedDamage != 130 || redHatDamage.LifeAfter != 470) return 5;
        redHatHit = redHatHit with { BaseDamage = 16_777_217, Critical = false };
        if (!new RuntimeNpcDamageExecutor(redHatNpcs).TryApply(in redHatHit, out redHatDamage) ||
            redHatDamage.ResolvedDamage != 11_744_047) return 5;

        var casterNpcs = new RuntimeNpcStore();
        casterNpcs.SetVanillaSpawnContextSource(() => new(3, 1, true)
            { HardMode = true, DownedPlantera = true, SkeletronActive = true });
        if (!casterNpcs.TrySpawnIntent(new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 255), out var caster) ||
            caster.Simulation.LifeMax != 191 || caster.Simulation.BaseDamage != 60 ||
            caster.Simulation.BaseDefense != 8 || caster.Simulation.KnockBackResist != .48000002f ||
            !casterNpcs.TrySpawnIntent(new(VanillaNpcIds.WaterSphere, 1000, 1000, 0, 0, 255), out var sphere) ||
            sphere.Simulation.LifeMax != 1 || sphere.Simulation.Alpha != 255 ||
            sphere.Simulation.BaseDamage != 60 || !sphere.Simulation.NoTileCollide) return 5;

        var sphereNpcs = new RuntimeNpcStore();
        var sphereState = new NpcStateUpdate(33, 33, 992, 984, 1.25f, -2.5f, 255, default,
            NpcSimulationState.Initial with { Rotation = .25f, JustHit = true });
        if (!sphereNpcs.TrySpawnVanilla(in sphereState, out var movingSphere)) return 5;
        var sphereAi = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        sphereAi.SetCandidates([new(0, 1500, 1042, 0, true, false, false, false)]);
        if (new RuntimeNpcAiStateExecutor(sphereNpcs).Tick(sphereAi).Applied != 1 ||
            !sphereNpcs.TryGet(movingSphere.Handle, out var sphereAfter) || sphereAfter.Target != 0 ||
            sphereAfter.VelocityX != 5.9702234f || sphereAfter.VelocityY != .59702235f ||
            sphereAfter.Simulation.Rotation != .65f || sphereAfter.Simulation.TimeLeft != 100 ||
            !sphereAfter.Simulation.JustHit) return 5;

        var substitutedNpcs = new RuntimeNpcStore();
        substitutedNpcs.SetVanillaSpawnContextSource(() => new(2, 1, true));
        substitutedNpcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(0));
        if (!substitutedNpcs.TrySpawnIntent(new(VanillaNpcIds.Demon, 1000, 1000, 0, 0, 255), out var demon) ||
            demon.TypeIdentity != VanillaNpcIds.VoodooDemon || demon.Simulation.LifeMax != 280 ||
            demon.Simulation.BaseDamage != 64 || demon.Simulation.NoGravity) return 5;
        substitutedNpcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(3));
        if (!substitutedNpcs.TrySpawnIntent(new(VanillaNpcIds.Bunny, 1000, 1000, 0, 0, 255), out var bunny) ||
            bunny.TypeIdentity != VanillaNpcIds.Bunny || bunny.Simulation.LifeMax != 5 ||
            bunny.Simulation.SpawnDifficulty != 1f || bunny.Simulation.Friendly != true) return 5;

        var casterTiles = new WorldTileStore(new WorldDimensions(400, 400));
        for (int x = 0; x < 400; x++)
        {
            casterTiles.Set(x, 80, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            casterTiles.Set(x, 79, new WorldTile { Wall = 7 });
        }
        var casterRandom = new SystemVanillaNpcRandom(123);
        var teleportNpcs = new RuntimeNpcStore();
        teleportNpcs.SetVanillaSpawnContextSource(() => new(1, 1, false));
        teleportNpcs.SetVanillaSpawnRandomSource(casterRandom);
        if (!teleportNpcs.TrySpawnIntent(new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 0)
            { InitialAi = new(649, 26, 0, 0) }, out var teleportCaster)) return 5;
        var casterAi = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: casterRandom);
        casterAi.SetCandidates([new(0, 1510, 1021, 0, true, false, false, false)]);
        casterAi.SetCasterEnvironment(new VanillaCasterWorldEnvironment(casterTiles));
        var casterExecutor = new RuntimeNpcAiStateExecutor(teleportNpcs);
        casterExecutor.Tick(casterAi);
        if (!teleportNpcs.TryGet(teleportCaster.Handle, out var queuedCaster) ||
            queuedCaster.Ai != new NpcAiState(1, 19, 114, 80) || teleportNpcs.ActiveCount != 1) return 5;
        casterExecutor.Tick(casterAi);
        if (!teleportNpcs.TryGet(teleportCaster.Handle, out var movedCaster) ||
            movedCaster.PositionX != 1823 || movedCaster.PositionY != 1240 || movedCaster.Ai != new NpcAiState(2, 18, 0, 0)) return 5;

        var skullNpcs = new RuntimeNpcStore();
        skullNpcs.SetVanillaSpawnContextSource(() => new(2, 1, false));
        if (!skullNpcs.TrySpawnIntent(new(VanillaNpcIds.SkeletronHead, 1000, 1000, 1.125f, -2.625f, 0)
            { InitialAi = new(1, 0, 0, 0) }, out var skullHead)) return 5;
        var skullProjectiles = new RuntimeProjectileStore();
        var skullAi = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: new SystemVanillaNpcRandom(0));
        skullAi.SetWorldConditions(false, false, expertMode: true);
        skullAi.SetCandidates([new(0, 1011.00024f, 951.99976f, 0, true, false, false, false)]);
        skullAi.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(new WorldTileStore(new WorldDimensions(400, 400))));
        if (new RuntimeNpcAiStateExecutor(skullNpcs, skullProjectiles).Tick(skullAi).Applied != 1 ||
            !skullProjectiles.TryGetActive(0, out var skull) || skull.Type != VanillaProjectileIds.SkeletronSkull ||
            skull.PositionX != 1012.504f || skull.PositionY != 938.0349f || skull.VelocityX != 5.100809f ||
            skull.VelocityY != (OperatingSystem.IsWindows() ? .406986237f : .406986f) ||
            skull.Ai != new ProjectileAiState(-1, 0, 0) || skull.Damage != 17 ||
            !skullProjectiles.TryGetLifecycle(skull.Handle, out var skullLife) || skullLife.TimeLeft != 300 ||
            !skullProjectiles.TryGetServerNpcSource(skull.Handle, out var skullSource) || skullSource != skullHead.Handle) return 5;

        Console.WriteLine($"Protocol smoke passed: release={request.ProtocolRelease}, frameLength={frame.PacketLength}, npcAnchors=ok, projectileWrap=ok, npcHealing=ok, npcClotSpawn=ok, npcAllocation=ok, destroyerChain=ok, npcSpawnContext=ok, primeBaseline=ok, primeArms=ok, skeletronHands=ok, skeletronPhase=ok, skeletronInitialization=ok, skeletronHandDash=ok, skeletronHandVariants=ok, skeletronRedHatCombat=ok, casterSpawn=ok, sphereAI=ok, npcSpawnRandom=ok, darkCasterAI=ok, skeletronSkull=ok.");
        return 0;
    }

    private sealed class MoonLeechSmokeStepper(INpcAiStateStepper inner, bool includeHead = false) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return (npc.TypeIdentity == VanillaNpcIds.MoonLordLeechBlob ||
                includeHead && npc.TypeIdentity == VanillaNpcIds.MoonLordHead) && inner.TryStepState(in npc, out next);
        }
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

        IPacketRateLimitControl packetLimits = new PacketRateLimitControl();
        var firstSession = new SessionPacketRateBudget(packetLimits);
        var secondSession = new SessionPacketRateBudget(packetLimits);
        packetLimits.SetLimit(12, 1);
        if (!firstSession.TryAcquire(12, out _) || !secondSession.TryAcquire(12, out _) ||
            firstSession.TryAcquire(12, out _) || secondSession.TryAcquire(12, out _))
        {
            Console.Error.WriteLine("Network smoke failed: shared packet policy did not retain independent session budgets.");
            return 15;
        }
        packetLimits.SetLimit(12, null);
        if (!firstSession.TryAcquire(12, out _) || !secondSession.TryAcquire(12, out _))
        {
            Console.Error.WriteLine("Network smoke failed: live packet policy removal did not reach existing sessions.");
            return 15;
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

        Console.WriteLine("Network smoke passed: fragmented ingress, bounded outbound queues, slow-client policy, admission gate, rate accounting, shared live packet limits and typed command ingress executed successfully.");
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
