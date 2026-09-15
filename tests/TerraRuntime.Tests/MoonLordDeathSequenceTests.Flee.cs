using System.Security.Cryptography;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Network;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed partial class MoonLordDeathSequenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Mounted_player_remains_a_target_and_does_not_trigger_departure(ushort mountType)
    {
        var npcs = new RuntimeNpcStore();
        var progression = new RuntimeWorldProgressionMutations(true);
        var state = new ServerRuntimeState(npcs: npcs, worldProgression: progression);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(83), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 30, 20, 0, 0, 0, 0, 0)));
        var movement = new PlayerMovementCommitRequest(session.Slot,
            0, VanillaPlayerMovementNormalizer.MovementMountPresentFlag, 0, 0, 0, 500f, 300f, false, 0f, 0f, true, mountType,
            false, 0f, 0f, 0f, 0f, false, 0f, 0f);
        state.Apply(new PlayerMovementRuntimeCommand(connection, movement));
        Assert.True(state.TryCapturePlayerSnapshot(connection.Player, out PlayerStateSnapshot player));
        Assert.Equal(mountType, player.MountType);
        Assert.False(player.IsDead);
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(1f, 0f, 0f, 0f));
        NpcSnapshot eye = SpawnNpc(npcs, new NpcTypeId(2), default);
        // Official NPC.TargetClosest tests active/dead/ghost, never mount.Type. The
        // production candidate projection is shared by boss departure and ordinary NPC AI.
        state.Tick();
        Assert.True(npcs.TryGet(core.Handle, out NpcSnapshot current));
        Assert.Equal(1f, current.Ai.Ai0);
        Assert.True(current.VelocityX > 0f);
        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot eyeState));
        Assert.Equal((ushort)session.Slot.Value, eyeState.Target);
        Assert.True(progression.LunarApocalypseIsUp);
    }

    [Fact]
    public async Task Live_world_sends_event_end_world_info_without_waiting_for_periodic_sync()
    {
        var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Lunar", 1458,
            32, 24, WorldGenerationOptions.Default);
        var materializer = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
        SandboxWorldMaterializationResult materialized = materializer.Materialize(source, CancellationToken.None);
        Assert.True(materialized.Succeeded, materialized.Error);
        WorldFileData loaded = materialized.World! with
        {
            RuntimeMetadata = new WorldFileRuntimeMetadata
            {
                LunarApocalypseIsUp = true, SpawnX = 10, SpawnY = 10,
                Time = 1000, DayTime = true, WorldSurface = 12, RockLayer = 16
            }
        };
        using var world = new WorldRuntime(new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
            source, loaded, materialized.Bootstrap!, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 2 });
        Assert.True(world.WorldProgression.LunarApocalypseIsUp);
        Assert.False(world.WorldProgression.CaptureSnapshot().HasAny);
        world.WorldClock.SetDayRate(0); // No time boundary can trigger the normal broadcast.
        NpcSnapshot core = SpawnNpc(world.Npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(3f, 59f, 0f, 0f));
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(80),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
        world.RuntimeConnections.PlayerSpawned(connection, in spawn);
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        int worldInfoFrames = 0;
        world.Start();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (worldInfoFrames == 0)
            {
                while (queue.TryRead(out OutboundFrame received))
                    if (received.Bytes.Span[2] == 7) worldInfoFrames++;
                if (worldInfoFrames == 0) await Task.Delay(10, deadline.Token);
            }
        }
        finally
        {
            Assert.True(await world.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false));
        }
        Assert.Null(world.GameLoop.Fault);
        Assert.False(world.Npcs.TryGet(core.Handle, out _));
        Assert.False(world.WorldProgression.LunarApocalypseIsUp);
        while (queue.TryRead(out OutboundFrame trailing))
            if (trailing.Bytes.Span[2] == 7) worldInfoFrames++;
        Assert.Equal(1, worldInfoFrames);
    }

    [Fact]
    public void Real_executor_enters_departure_and_finishes_event_without_victory_or_loot()
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var progression = new RuntimeWorldProgressionMutations(true);
        var items = new RuntimeWorldItemStore();
        var clock = new RuntimeWorldClock(1000, false, default, 0, 0);
        var pipeline = CreatePipeline(npcs, projectiles, progression, items, clock);
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(1f, 15f, 0f, 0f));
        ProjectileSnapshot attack = SpawnProjectile(projectiles, VanillaProjectileIds.PhantasmalBolt);
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 510f, 321f, 0, true, true, false, false)]);
        executor.Tick(stepper, pipeline);
        Assert.True(npcs.TryGet(core.Handle, out NpcSnapshot entering));
        Assert.Equal(3f, entering.Ai.Ai0);
        Assert.Equal(0f, entering.Ai.Ai1);
        for (int tick = 1; tick <= 60; tick++)
        {
            executor.Tick(stepper, pipeline);
            Assert.Equal(tick < 60, npcs.TryGet(core.Handle, out _));
            Assert.Equal(tick < 40, projectiles.TryGet(attack.Handle, out _));
            Assert.Equal(tick < 60, progression.LunarApocalypseIsUp);
        }
        Assert.True(clock.ConsumeWorldInfoSyncRequest());
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
        Assert.Equal(0, items.ActiveCount);
    }

    [Fact]
    public void Stale_departure_cannot_remove_replacement_actors_or_end_their_event()
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var progression = new RuntimeWorldProgressionMutations(true);
        var clock = new RuntimeWorldClock(1000, false, default, 0, 0);
        var pipeline = CreatePipeline(npcs, projectiles, progression, clock: clock);
        NpcSnapshot oldCore = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(3f, 60f, 0f, 0f));
        Assert.True(npcs.TryDespawn(oldCore.Handle));
        NpcSnapshot replacement = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, default, oldCore.Handle.Slot);
        NpcSnapshot hand = SpawnNpc(npcs, VanillaNpcIds.MoonLordHand, default);
        Assert.Equal(oldCore.Handle.Slot, replacement.Handle.Slot);
        pipeline.NpcAiStateCommitted(in oldCore);
        Assert.True(npcs.TryGet(replacement.Handle, out _));
        Assert.True(npcs.TryGet(hand.Handle, out _));
        Assert.True(progression.LunarApocalypseIsUp);
        Assert.False(clock.ConsumeWorldInfoSyncRequest());
    }

    [Fact]
    public void Departure_repeats_packet27_before_silent_removal_and_clears_join_baseline()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var owner = new ConnectionHandle(GameCommandSourceId.FromConnection(71),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(replication.TryRegister(owner.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(owner.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(owner, in spawn);
        var projectiles = new RuntimeProjectileStore(commitSink: replication);
        ProjectileTypeId[] types = [VanillaProjectileIds.MoonLeech, VanillaProjectileIds.PhantasmalBolt,
            VanillaProjectileIds.PhantasmalDeathray, VanillaProjectileIds.PhantasmalEye, VanillaProjectileIds.PhantasmalSphere];
        foreach (ProjectileTypeId type in types) SpawnProjectile(projectiles, type);
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        var original = new List<byte[]>();
        while (queue.TryRead(out OutboundFrame frame)) original.Add(frame.Bytes.ToArray());
        Assert.Equal(5, original.Count);
        var npcs = new RuntimeNpcStore();
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(3f, 40f, 0f, 0f));
        var pipeline = CreatePipeline(npcs, projectiles, new RuntimeWorldProgressionMutations(),
            projectileReplication: replication);
        pipeline.NpcAiStateCommitted(in core);
        Assert.Equal(0, projectiles.ActiveCount);
        foreach (byte[] expected in original)
        {
            Assert.True(queue.TryRead(out OutboundFrame frame));
            Assert.Equal(27, frame.Bytes.Span[2]);
            Assert.Equal(expected, frame.Bytes.ToArray());
        }
        Assert.False(queue.TryRead(out _)); // No packet29 combat-kill notification.
        var joining = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var newcomer = new ConnectionHandle(GameCommandSourceId.FromConnection(72),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        Assert.True(replication.TryRegister(newcomer.Source, joining));
        var joinSpawn = new PlayerSpawnCommitRequest(newcomer.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(newcomer, in joinSpawn);
        Assert.Equal(0, joining.QueuedFrames);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(1f, false)]
    [InlineData(0f, true)]
    [InlineData(1f, true)]
    public void Loss_of_last_player_keeps_original_final_pursuit_step(float state, bool dead)
    {
        foreach (float playerX in new[] { 0f, 500f, -100f })
        {
            var npcs = new RuntimeNpcStore();
            NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(state, 15f, 0f, 0f));
            NpcSnapshot left = SpawnNpc(npcs, VanillaNpcIds.MoonLordHand, default);
            NpcSnapshot right = SpawnNpc(npcs, VanillaNpcIds.MoonLordHand, default);
            NpcSnapshot head = SpawnNpc(npcs, VanillaNpcIds.MoonLordHead, default);
            var initial = new NpcStateUpdate(core.Type, core.NetId, 100f, 100f, 2f, -3f, 0, core.Ai,
                core.Simulation with { LocalAi = new NpcAiState(left.Handle.Slot, right.Handle.Slot, head.Handle.Slot, 1f) });
            Assert.True(npcs.TryUpdate(core.Handle, in initial, out core));
            var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
            stepper.SetNpcPeers([core, left, right, head]);
            stepper.SetCandidates([new VanillaNpcTargetCandidate(0, playerX + 10f, 321f, 0, dead, dead, false, false)]);
            Assert.True(stepper.TryStepState(in core, out NpcStateUpdate next));
            // Twelve original AI_077 calls, inactive/dead target and both pursuit states.
            Assert.Equal(playerX == 500f ? 2.25f : 1.5f, next.VelocityX);
            Assert.Equal(-2.5f, next.VelocityY);
            Assert.Equal(3f, next.Ai.Ai0);
            Assert.Equal(0f, next.Ai.Ai1);
            Assert.Equal(state == 0f, next.Simulation.DontTakeDamage);
        }
    }

    [Theory]
    [InlineData(-1, "147d1d16f78622d0d82c090ceb84f61275ecf9bdb41d77940b359b395ceb1671")]
    [InlineData(1, "91660a4eb725480d068156d9221ce2c49d187a288032069fbee95f5e732002a3")]
    public void Departure_matches_original_ai_velocity_global_cleanup_and_event_state(int direction, string expected)
    {
        // Independent original TerrariaServer 1.4.5.8 AI_077 invocation: 60 incoming timers
        // per direction; all actor tables reset each call. Hash includes single-precision
        // velocity/timer followed by all seven NPC, six projectile and lunar-event booleans.
        var random = new Random(1458);
        if (direction == 1)
            for (int i = 0; i < 120; i++) random.Next(-100, 101);
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        for (int tick = 0; tick < 60; tick++)
        {
            var npcs = new RuntimeNpcStore();
            var projectiles = new RuntimeProjectileStore();
            var items = new RuntimeWorldItemStore();
            var progression = new RuntimeWorldProgressionMutations(lunarApocalypseIsUp: true);
            var clock = new RuntimeWorldClock(1000, false, default, 0, 0);
            var pipeline = CreatePipeline(npcs, projectiles, progression, items, clock);
            NpcTypeId[] types = [VanillaNpcIds.MoonLordCore, VanillaNpcIds.MoonLordHand,
                VanillaNpcIds.MoonLordHand, VanillaNpcIds.MoonLordHead, VanillaNpcIds.MoonLordFreeEye,
                VanillaNpcIds.BlueSlime, VanillaNpcIds.MoonLordCore];
            NpcSnapshot[] actors = types.Select(type => SpawnNpc(npcs, type, default)).ToArray();
            NpcSnapshot core = actors[0];
            var initial = new NpcStateUpdate(core.Type, core.NetId, 100f, 100f,
                random.Next(-100, 101) / 8f, random.Next(-100, 101) / 8f, 0,
                new NpcAiState(3f, tick, 0f, 0f), core.Simulation with { DirectionX = direction });
            Assert.True(npcs.TryUpdate(core.Handle, in initial, out core));
            ProjectileTypeId[] attackTypes = [VanillaProjectileIds.MoonLeech, VanillaProjectileIds.PhantasmalBolt,
                VanillaProjectileIds.PhantasmalDeathray, VanillaProjectileIds.PhantasmalEye,
                VanillaProjectileIds.PhantasmalSphere, VanillaProjectileIds.WoodenArrowFriendly];
            ProjectileSnapshot[] attacks = attackTypes.Select(type => SpawnProjectile(projectiles, type)).ToArray();
            var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
            stepper.SetCandidates([]);
            Assert.True(stepper.TryStepState(in core, out NpcStateUpdate next));
            Assert.Equal(core.Simulation.Life, next.Simulation.Life);
            Assert.True(next.Simulation.DontTakeDamage);
            Assert.True(npcs.TryUpdate(core.Handle, in next, out NpcSnapshot committed));
            pipeline.NpcAiStateCommitted(in committed);
            writer.Write(next.VelocityX);
            writer.Write(next.VelocityY);
            writer.Write(next.Ai.Ai1);
            foreach (NpcSnapshot actor in actors) writer.Write(npcs.TryGet(actor.Handle, out _));
            foreach (ProjectileSnapshot attack in attacks) writer.Write(projectiles.TryGet(attack.Handle, out _));
            writer.Write(progression.LunarApocalypseIsUp);
            Assert.Equal(tick == 59, clock.ConsumeWorldInfoSyncRequest());
            Assert.False(clock.ConsumeWorldInfoSyncRequest());
            Assert.False(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
            Assert.Equal(0, items.ActiveCount);
        }
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
    }
}
