using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCombatCurrentness1458Tests
{
    [Theory]
    [InlineData("none")]
    [InlineData("terrain")]
    [InlineData("player")]
    [InlineData("status")]
    public void Adoption_rechecks_external_owners_after_the_final_spawn_context_callback(string mutation)
    {
        using var f = new Fixture(381);
        f.Attack();
        var before = f.Current();
        var checkpoint = f.Random.Clone();
        Assert.True(f.Targeting.TryStepState(in before, out var proposal));
        Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var placeholder));
        var field = typeof(RuntimeNpcStore).GetField("_spawnContext", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var provider = Assert.IsType<Func<VanillaNpcSpawnContext>>(field.GetValue(f.Npcs));
        int callbacks = 0;
        field.SetValue(f.Npcs, (Func<VanillaNpcSpawnContext>)(() => {
            var facts = provider();
            if (++callbacks == 2)
            {
                if (mutation == "terrain") f.Tiles.Set(68, 95, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                if (mutation == "player") f.MovePlayer(1250f, 1558f);
                if (mutation == "status") Assert.True(f.Status.TryApply(before.Handle, 6));
            }
            return facts;
        }));
        NpcSnapshot completed = default;
        var error = Record.Exception(() => completed = f.Motion.CompleteCommittedState(in before, in placeholder,
            new RuntimeNpcAiStateExecutor(f.Npcs, f.Projectiles)));
        Assert.Equal(2, callbacks);
        if (mutation == "none")
        {
            Assert.Null(error);
            Assert.True(completed.Handle.IsAssigned);
            Assert.False(checkpoint.HasSameState(f.Random));
            Assert.Equal(1, f.Projectiles.ActiveCount);
        }
        else
        {
            Assert.True(placeholder == f.Current(), $"{mutation}: stale phase changed NPC before refusing; {error}");
            Assert.True(checkpoint.HasSameState(f.Random), $"{mutation}: stale phase adopted shared entropy");
            Assert.Null(error);
            Assert.False(completed.Handle.IsAssigned);
            Assert.Equal(0, f.Projectiles.ActiveCount);
            if (mutation == "terrain") Assert.True(f.Tiles.Get(68, 95).IsActive);
            if (mutation == "player")
            {
                Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var moved));
                Assert.Equal(1250f, moved.PositionX);
            }
            if (mutation == "status")
            {
                Assert.True(f.Status.TryGetWireDuration(before.Handle, out int duration));
                Assert.Equal(6, duration);
            }
        }
    }

    [Fact]
    public void Birth_publication_refuses_nested_combat_planning_without_losing_sync_or_a_future_plan()
    {
        var sink = new Sink();
        using var f = new Fixture(381, sink);
        f.Attack();
        var before = f.Current();
        var otherState = new NpcStateUpdate(381, 381, 1100f, 1560f, .9f, 0f, 0,
            new(0f, 41f, 1f, 0f), before.Simulation with { OldPositionX = 1099f, OldPositionY = 1560f });
        Assert.True(f.Npcs.TrySpawnVanilla(in otherState, out var other));
        bool nestedAccepted = false;
        sink.Callback = shot => nestedAccepted = f.Targeting.TryStepState(in other, out _);
        Assert.True(f.Targeting.TryStepState(in before, out var proposal));
        Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var placeholder));
        var completed = f.Motion.CompleteCommittedState(in before, in placeholder,
            new RuntimeNpcAiStateExecutor(f.Npcs, f.Projectiles));
        Assert.True(completed.Handle.IsAssigned);
        Assert.Equal(1, sink.Births);
        Assert.False(nestedAccepted);
        Assert.False(f.Targeting.GroundCombat.HasPlan(in other));
        Assert.True(f.Targeting.RequiresForcedUpdateAfterCompletion(in before, in completed));
        // Refusal is limited to the notification boundary, with no stranded lease.
        Assert.True(f.Targeting.TryStepState(in other, out _));
        Assert.True(f.Targeting.GroundCombat.HasPlan(in other));
        f.Targeting.GroundCombat.Cancel();
        Assert.False(f.Targeting.GroundCombat.HasPlan(in other));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wet_buff_frame_is_not_sent_to_a_replacement_created_by_the_birth_observer(bool replace)
    {
        var registry = new RuntimeNpcReplicationRegistry();
        var sink = new Sink();
        using var f = new Fixture(381, sink, replication: registry);
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 1024));
        Assert.True(registry.TryRegister(f.Connection.Source, outbound));
        registry.PlayerSpawned(f.Connection, new(f.Connection.Player.Slot, 64, 100, 0, 0, 0, 0, 0));
        _ = Drain(outbound);
        f.Attack();
        var before = f.Current();
        Assert.True(f.Status.TryApply(before.Handle, 6));
        _ = Drain(outbound);
        for (int x = 62; x <= 68; x++)
            for (int y = 97; y < 100; y++) f.Tiles.Set(x, y, new WorldTile { LiquidAmount = 255 });
        NpcSnapshot replacement = default;
        sink.Callback = _ => {
            Assert.True(f.Status.TryGetWireDuration(before.Handle, out int duration));
            Assert.Equal(-1, duration);
            if (!replace) return;
            Assert.True(f.Npcs.TryDespawn(before.Handle));
            var state = new NpcStateUpdate(1, 1, 400f, 400f, 0f, 0f, 255, default, NpcSimulationState.Initial);
            Assert.True(f.Npcs.TrySpawn(before.Handle.Slot, in state, out replacement));
            f.World.Apply(new ClientNpcBuffRuntimeCommand(f.Connection,
                new(replacement.Handle.Slot, 120, 90)));
        };
        NonresidentBunnyNpcPhase1458.Tick(f.World);
        Assert.Equal(1, sink.Births);
        var frames = Drain(outbound);
        if (replace)
        {
            Assert.True(f.Npcs.TryGet(replacement.Handle, out var retained));
            Assert.Equal(replacement, retained);
            Assert.True(f.Status.TryGetWireDuration(replacement.Handle, out int duration));
            Assert.Equal(90, duration);
            Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(replacement.Handle.Slot, 90, out var newest));
            Assert.Equal(newest, Assert.Single(frames, frame => frame[2] == 54));
        }
        else
        {
            Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(before.Handle.Slot, -1, out var clear));
            Assert.Equal(clear, Assert.Single(frames, frame => frame[2] == 54));
        }
    }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(outbound)!;
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }

    [Fact]
    public void Prepared_ranged_birth_and_shared_entropy_are_adopted_only_with_all_captured_owners()
    {
        Span<ProjectileSnapshot> shots = stackalloc ProjectileSnapshot[1];
        foreach (string mutation in new[] { "none", "terrain", "player", "projectiles", "random", "width" })
        {
            using var f = new Fixture(381);
            f.Attack();
            var before = f.Current();
            var checkpoint = f.Random.Clone();
            Assert.True(f.Targeting.TryStepState(in before, out var proposal), mutation);
            Assert.True(checkpoint.HasSameState(f.Random));
            Assert.Equal(0, f.Projectiles.ActiveCount);
            Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var placeholder));
            switch (mutation)
            {
                case "terrain": f.Tiles.Set(68, 95, new WorldTile { Type = 1, Flags = WorldTileFlags.Active }); break;
                case "player": f.MovePlayer(1250f, 1558f); break;
                case "projectiles":
                    var foreign = new ProjectileStateUpdate(new(1), 0, 400f, 400f, 1f, 0f, default, 0, 1, 0f, 1);
                    Assert.True(f.Projectiles.TrySpawnVanilla(in foreign, out _));
                    break;
                case "random": f.Random.Next(); break;
                case "width": f.Targeting.SetWorldBounds(201, 80d, worldHeightTiles: 200); break;
            }
            var live = f.Random.Clone();
            int count = f.Projectiles.ActiveCount;
            var completed = f.Motion.CompleteCommittedState(in before, in placeholder,
                new RuntimeNpcAiStateExecutor(f.Npcs, f.Projectiles));
            Assert.Equal(mutation == "none", completed.Handle.IsAssigned);
            if (mutation != "none")
            {
                Assert.Equal(placeholder, f.Current());
                Assert.Equal(count, f.Projectiles.ActiveCount);
                Assert.True(live.HasSameState(f.Random));
            }
            else
            {
                Assert.Equal(completed, f.Current());
                Assert.False(checkpoint.HasSameState(f.Random));
                Assert.Equal(1, f.Projectiles.CopyActive(shots));
                Assert.Equal(436, shots[0].Type.Value);
                Assert.Equal(byte.MaxValue, shots[0].Spawner);
                Assert.Equal((short)0, shots[0].OriginalDamage);
                Assert.True(f.Projectiles.TryGetLifecycle(shots[0].Handle, out var lifecycle));
                Assert.Equal(3600, lifecycle.TimeLeft);
                Assert.False(lifecycle.NpcProjectile);
                Assert.True(f.Projectiles.TryGetServerNpcSource(shots[0].Handle, out var source));
                Assert.Equal(completed.Handle, source);
            }
        }
    }

    [Fact]
    public void Ranged_line_of_sight_retains_the_remote_corridor_section_before_adoption()
    {
        foreach (bool change in new[] { false, true })
        {
            using var f = new Fixture(381, width: 600, playerX: 3200f, npcX: 3000f);
            f.Prepare(3000f, 0f, 0f);
            var before = f.Current();
            var random = f.Random.Clone();
            var actorSection = TerrariaSectionGeometry.FromTile(f.Tiles.Dimensions, 187, 98);
            var remoteSection = TerrariaSectionGeometry.FromTile(f.Tiles.Dimensions, 200, 98);
            Assert.NotEqual(actorSection, remoteSection);
            long actorVersion = f.Tiles.GetSectionVersion(actorSection);
            Assert.True(f.Targeting.TryStepState(in before, out var proposal));
            Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var placeholder));
            if (change) f.Tiles.Set(200, 94, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Assert.Equal(actorVersion, f.Tiles.GetSectionVersion(actorSection));
            var completed = f.Motion.CompleteCommittedState(in before, in placeholder,
                new RuntimeNpcAiStateExecutor(f.Npcs, f.Projectiles));
            Assert.Equal(!change, completed.Handle.IsAssigned);
            if (change)
            {
                Assert.Equal(placeholder, f.Current());
                Assert.True(random.HasSameState(f.Random));
            }
            else Assert.True(completed.Ai.Ai2 > 0f, "Source LOS must start the ranged windup.");
            Assert.Equal(0, f.Projectiles.ActiveCount);
        }
    }

    [Fact]
    public void Projectile_birth_observer_sees_adopted_npc_and_cannot_overwrite_a_reused_npc_generation()
    {
        var sink = new Sink();
        using var f = new Fixture(381, sink);
        f.Attack();
        var before = f.Current();
        var checkpoint = f.Random.Clone();
        NpcSnapshot replacement = default;
        sink.Callback = shot =>
        {
            Assert.True(f.Npcs.TryGet(before.Handle, out var adopted));
            Assert.True(adopted.Revision.Value >= before.Revision.Value + 2);
            Assert.False(checkpoint.HasSameState(f.Random));
            Assert.True(f.Projectiles.TryGetServerNpcSource(shot.Handle, out var source));
            Assert.Equal(before.Handle, source);
            Assert.True(f.Npcs.TryDespawn(before.Handle));
            var state = new NpcStateUpdate(1, 1, 400f, 400f, 0f, 0f, 255, default, NpcSimulationState.Initial);
            Assert.True(f.Npcs.TrySpawn(before.Handle.Slot, in state, out replacement));
        };
        NonresidentBunnyNpcPhase1458.Tick(f.World);
        Assert.Equal(1, sink.Births);
        Assert.True(replacement.Handle.IsAssigned);
        Assert.True(f.Npcs.TryGet(replacement.Handle, out var retained));
        Assert.Equal(replacement, retained);
        Assert.NotEqual(before.Handle.Generation, retained.Handle.Generation);
    }

    [Fact]
    public void Every_new_identity_reaches_the_existing_world_phase_without_a_parallel_owner()
    {
        foreach (int identity in new[] { 185,385,482,508,580,582,590,-15,381,382,449,450,451,452,481,520 })
        {
            using var f = new Fixture(identity);
            var before = f.Current();
            NonresidentBunnyNpcPhase1458.Tick(f.World);
            var after = f.Current();
            Assert.Equal(before.Handle, after.Handle);
            Assert.True(after.Revision.Value >= before.Revision.Value + 2, $"identity {identity}: retained phase did not finish");
            Assert.Equal(before.Simulation.Life, after.Simulation.Life);
            Assert.NotNull(after.Simulation.FrameIndex);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeProjectileStore Projectiles;
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly ServerRuntimeState World;
        internal readonly ConnectionHandle Connection;
        internal readonly VanillaNpcTargetingAiStepper Targeting;
        internal readonly VanillaNpcWorldMotionAiStepper Motion;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        private readonly PlayerJoinSession session;
        private readonly NpcHandle handle;

        internal Fixture(int identity, IProjectileStateCommitSink? sink = null,
            int width = 200, float playerX = 1210f, float npcX = 1023f, RuntimeNpcReplicationRegistry? replication = null)
        {
            Npcs = new(commitSink: replication);
            Tiles = new(new WorldDimensions(width, 200));
            Projectiles = new(commitSink: sink);
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < width; x++) Tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(npcs: Npcs, projectiles: Projectiles, worldTiles: Tiles, npcReplication: replication,
                worldClock: new RuntimeWorldClock(0, false, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9087), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(Connection, session,
                new(session.Slot, checked((short)(playerX / 16f)), 100, 0, 0, 0, 0, 0)));
            MovePlayer(playerX, 1558f);
            int type = identity == -15 ? 77 : identity;
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)identity), out var definition));
            Assert.True(definition.TryResolveHitbox(NpcSimulationState.Initial with { Scale = definition.Scale }, out var body));
            float y = 1600f - body.Height;
            var creation = new NpcStateUpdate(type, (short)identity, npcX, y, .9f, 0f, 255, default,
                NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                    OldPositionX = npcX - 1f, OldPositionY = y, OldVelocityX = .9f });
            Assert.True(Npcs.TrySpawnVanilla(in creation, out var spawned));
            handle = spawned.Handle;
            NonresidentBunnyNpcPhase1458.Tick(World);
            Assert.True(Current().Revision.Value >= spawned.Revision.Value + 2,
                $"identity {identity}: initial retained world phase did not finish");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(World));
            Targeting = Assert.IsType<VanillaNpcTargetingAiStepper>(typeof(NpcAuthority).GetField("vanillaTargeting", flags)!.GetValue(graph.Npcs));
            Motion = Assert.IsType<VanillaNpcWorldMotionAiStepper>(typeof(NpcAuthority).GetField("pirateGhostMotion", flags)!.GetValue(graph.Npcs));
            Status = graph.Npcs.NpcBuffStatus;
        }

        internal NpcSnapshot Current() { Assert.True(Npcs.TryGet(handle, out var current)); return current; }
        internal void MovePlayer(float x, float y) => World.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(session.Slot, 0, 0, 0, 0, 0, x, y, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        internal void Attack() => Prepare(1023f, 41f, 1f);
        internal void Prepare(float x, float timer, float mode)
        {
            var current = Current();
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(current.TypeIdentity, current.NetIdentity, out var definition));
            Assert.True(definition.TryResolveHitbox(current.Simulation, out var body));
            var state = new NpcStateUpdate(current.Type, current.NetId, x, 1600f - body.Height, .9f, 0f, 0,
                current.Ai with { Ai1 = timer, Ai2 = mode }, current.Simulation with { DirectionX = 1, DirectionY = 1,
                    SpriteDirection = 1, OldPositionX = x - 1f, OldPositionY = 1600f - body.Height });
            Assert.True(Npcs.TryUpdate(handle, in state, out _));
        }
        public void Dispose() => session.Dispose();
    }

    private sealed class Sink : IProjectileStateCommitSink
    {
        internal Action<ProjectileSnapshot>? Callback;
        internal int Births;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            if (kind != ProjectileStateCommitKind.Spawn) return;
            Births++;
            var callback = Callback;
            Callback = null;
            callback?.Invoke(snapshot);
        }
    }
}
