using System.Buffers;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonDeathLiveBootstrap1458Tests
{
    [Fact]
    public void Actual_world_bootstrap_and_new_connection_see_accepted_moon_death_before_first_loot_observer()
    {
        (int Type, VanillaWorldProgressionId Milestone, bool Lantern)[] cases =
        [(325, VanillaWorldProgressionId.MourningWood, false), (327, VanillaWorldProgressionId.Pumpking, true),
            (344, VanillaWorldProgressionId.Everscream, false), (345, VanillaWorldProgressionId.IceQueen, true),
            (346, VanillaWorldProgressionId.SantaNk1, false)];
        foreach (var test in cases)
        {
            using var world = CreateWorld();
            Assert.True(test.Type < 338 ? world.WorldClock.TryStartPumpkinMoon() : world.WorldClock.TryStartSnowMoon());
            world.RefreshWorldBootstrap();
            var before = world.CaptureWorldBootstrap();
            Assert.Equal(1, before.Progress!.Value.Wave);
            Assert.False(before.Runtime.ProgressionMutations!.Value.IsCompleted(test.Milestone));
            var state = new NpcStateUpdate(test.Type, (short)test.Type, 200, 200, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(world.Npcs.TrySpawnVanilla(in state, out var target));
            // Test-only observation of the existing commit boundary; the real registry remains
            // the sink. No production callback seam or alternative composition is introduced.
            var sinkField = typeof(RuntimeWorldItemStore).GetField("_commitSink", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var inner = Assert.IsAssignableFrom<IWorldItemStateCommitSink>(sinkField.GetValue(world.WorldItems));
            int observerCount = 0;
            var observer = new Observer(inner, () =>
            {
                var accepted = world.CaptureWorldBootstrap();
                Assert.NotSame(before, accepted);
                Assert.True(accepted.Runtime.ProgressionMutations!.Value.IsCompleted(test.Milestone));
                Assert.Equal(2, accepted.Progress!.Value.Wave);
                Assert.Equal(0, accepted.Progress!.Value.Progress);
                Assert.False(world.Npcs.TryGet(target.Handle, out _));
                Assert.True(world.Invasion.TryCapture(out var invasion));
                Assert.Equal(test.Lantern, invasion.State.LanternNextNight);
                if (observerCount++ != 0) return;
                // This is the actual socket bootstrap adapter, invoked while the first accepted
                // drop is still waiting to reach its original network registry sink.
                var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(256, 1_048_576, 65_536));
                Assert.True(RuntimeConnectionWorldBinding.TryCreateInitial(world,
                    GameCommandSourceId.FromConnection(73000 + test.Type), outbound, out var connection));
                using var binding = connection!;
                Assert.Equal(TerrariaFrameSinkResult.Continue, binding.Bootstrap.OnFrame(Frame(TerrariaMessageId.Hello,
                    [11, .. System.Text.Encoding.ASCII.GetBytes("Terraria326")])));
                var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                    .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
                Drain(queue);
                Assert.Equal(TerrariaFrameSinkResult.Continue,
                    binding.Bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
                var frames = Drain(queue);
                Assert.Equal(new byte[] { 7, 78 }, frames.Select(frame => frame[2]).ToArray());
                Assert.Equal(accepted.WorldInfoFrame.ToArray(), frames[0]);
                Assert.Equal(0, BitConverter.ToInt32(frames[1], 3));
                Assert.Equal(test.Type < 338 ? 2 : 1, frames[1][11]);
                Assert.Equal(2, frames[1][12]);
            });
            sinkField.SetValue(world.WorldItems, observer);
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.State)!;
            var combat = (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority).GetField("combat",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, combat.TryStrikeEnvironment(target.Handle, 100000));
            Assert.True(observer.Count > 0);
            Assert.False(before.Runtime.ProgressionMutations!.Value.IsCompleted(test.Milestone));
            Assert.Equal(1, before.Progress!.Value.Wave);
        }
    }

    [Fact]
    public void Ordinary_goblin_and_captain_publish_accepted_invasion_snapshot_before_first_loot()
    {
        foreach (var test in new[] { (Type: 26, Invasion: 1, Credit: 1), (Type: 216, Invasion: 3, Credit: 5) })
        {
            using var world = CreateWorld(test.Invasion);
            var before = world.CaptureWorldBootstrap();
            var state = new NpcStateUpdate(test.Type, (short)test.Type, 200, 200, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(world.Npcs.TrySpawnVanilla(in state, out var target));
            var observer = ObserveItems(world, () =>
            {
                var accepted = world.CaptureWorldBootstrap();
                Assert.NotSame(before, accepted);
                Assert.Equal((sbyte?)test.Invasion, accepted.Runtime.InvasionType);
                Assert.Equal(test.Credit, accepted.Progress!.Value.Progress);
                Assert.Equal(80, accepted.Progress!.Value.Maximum);
                Assert.False(world.Npcs.TryGet(target.Handle, out _));
                Assert.True(world.Invasion.TryCapture(out var invasion));
                Assert.Equal(80 - test.Credit, invasion.State.Size);
            });
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, Combat(world).TryStrikeEnvironment(target.Handle, 100000));
            Assert.True(observer.Count > 0);
            Assert.Equal(0, before.Progress!.Value.Progress);
        }
    }

    [Fact]
    public void Trusted_frost_backend_publishes_real_bootstrap_completion_and_checkpoint_without_admitting_AI38()
    {
        foreach (int type in new[] { 143, 144, 145 })
        {
            using var world = CreateWorld(2, invasionSize: 1);
            var state = new NpcStateUpdate(type, (short)type, 200, 200, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = type == 143 ? 200 : type == 144 ? 240 : 220,
                    LifeMax = type == 143 ? 200 : type == 144 ? 240 : 220,
                    MoneyValue = 400f, ExtraMoneyValue = 0, Midas = false,
                    Friendly = false, Chaseable = true, Immortal = false, TownNpc = false,
                    BaseDamage = type == 143 ? 50 : type == 144 ? 65 : 55,
                    BaseDefense = type == 143 ? 20 : type == 144 ? 26 : 22,
                    BaseLifeMax = type == 143 ? 200 : type == 144 ? 240 : 220,
                    SpawnDifficulty = 1f, KnockBackResist = .6f, Rotation = 0,
                    // NPC.SetDefaults(143..145): the trusted backend body must
                    // carry its real dimensions for retained network birth/terminal frames.
                    HitboxOverride = new NpcHitboxDimensions(26, 40) });
            // An explicit trusted body exercises the future admitted owner's backend.
            // Public combat retains the lifecycle barrier despite admitted creation defaults.
            Assert.True(world.Npcs.TrySpawn(0, in state, out var target));
            var combat = Combat(world);
            var rng = combat.SourceRandom.Clone();
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, combat.TryStrikeEnvironment(target.Handle, 100000));
            Assert.True(world.Npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.True(combat.SourceRandom.HasSameState(rng));
            var observer = ObserveItems(world, () =>
            {
                var accepted = world.CaptureWorldBootstrap();
                Assert.Equal((sbyte?)2, accepted.Runtime.InvasionType);
                Assert.Equal(80, accepted.Progress!.Value.Progress);
                Assert.False(world.Npcs.TryGet(target.Handle, out _));
                Assert.False(world.WorldProgression.IsCompleted(VanillaWorldProgressionId.FrostLegion));
            });
            var outgoing = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(512, 1_048_576, 65_536));
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(74000 + type), new(new(0), new(1)));
            Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outgoing));
            var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
            world.RuntimeConnections.PlayerSpawned(connection, in spawn);
            Assert.True(world.NpcReplication.TryRegister(connection.Source, outgoing));
            world.NpcReplication.PlayerSpawned(connection, in spawn);
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outgoing)!;
            Drain(queue);
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
            var projected = target with { Simulation = target.Simulation with { Life = 0 } };
            Assert.True((bool)typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("TryAdmitLethalDeath", methods)!
                .Invoke(combat, [projected])!);
            var hp = state with { Simulation = projected.Simulation };
            Assert.True(world.Npcs.TryUpdateUnpublished(target.Handle, in hp, out var acceptedNpc));
            typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("AdoptOrdinaryInvasionOwners", methods)!.Invoke(combat, [acceptedNpc]);
            var wire = new TerrariaNpcDamageState(target.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(target.Handle.Generation), short.MaxValue, 0, 1, 0);
            typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("PublishOrdinaryInvasionDeath", methods)!
                .Invoke(combat, [acceptedNpc, wire, null]);
            Assert.True(observer.Count > 0);
            var death = Drain(queue);
            int progress = Array.FindIndex(death, frame => frame[2] == 78);
            int terminal = Array.FindLastIndex(death, frame => frame[2] == 23);
            Assert.True(progress >= 0 && terminal > progress);
            Assert.Equal(80, BitConverter.ToInt32(death[progress], 3));
            typeof(WorldRuntime).GetMethod("AdvanceInvasion", methods)!.Invoke(world, null);
            var completed = Drain(queue);
            Assert.Equal("0500620C00", Convert.ToHexString(completed[0]));
            Assert.Equal(82, completed[1][2]);
            Assert.Equal(7, completed[2][2]);
            Assert.True(world.WorldProgression.IsCompleted(VanillaWorldProgressionId.FrostLegion));
            Assert.Null(world.CaptureWorldBootstrap().Progress);
            Assert.Equal((sbyte)0, world.CaptureWorldBootstrap().Runtime.InvasionType);
            var source = new RuntimeWorldCheckpointSnapshotSource(world.World.Tiles, world.Chests, 8,
                world.WorldClock, progressionMutations: world.WorldProgression, invasionSaveStateSource: world.Invasion.CaptureSaveState);
            source.CaptureTileBootstrap(8);
            Assert.True(source.TryCapture(out var checkpoint));
            Assert.True(checkpoint!.ProgressionMutations!.Value.IsCompleted(VanillaWorldProgressionId.FrostLegion));
            Assert.True(checkpoint.Invasion!.Value.LanternNightNextNight);
        }
    }

    private static RuntimeNpcNetworkCombatPipeline Combat(WorldRuntime world)
    {
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.State)!;
        return (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority).GetField("combat",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
    }

    private static Observer ObserveItems(WorldRuntime world, Action onDrop)
    {
        var field = typeof(RuntimeWorldItemStore).GetField("_commitSink", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var observer = new Observer(Assert.IsAssignableFrom<IWorldItemStateCommitSink>(field.GetValue(world.WorldItems)), onDrop);
        field.SetValue(world.WorldItems, observer);
        return observer;
    }

    private static WorldRuntime CreateWorld(int invasionType = 0, int invasionSize = 80)
    {
        var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Moon retained bootstrap", 1458,
            32, 24, WorldGenerationOptions.Default);
        var result = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits())
            .Materialize(source, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error);
        var world = result.World! with { RuntimeMetadata = new WorldFileRuntimeMetadata
            { SpawnX = 10, SpawnY = 10, WorldSurface = 12, RockLayer = 16, DayTime = false,
                InvasionType = checked((sbyte)invasionType), InvasionSize = invasionType > 0 ? invasionSize : 0,
                InvasionSizeStart = invasionType > 0 ? 80 : 0, InvasionX = 10 } };
        var runtime = new WorldRuntime(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()), source, world, result.Bootstrap!,
            new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 2 });
        Assert.Equal(VanillaNpcSpawnRules.PhysicalSlotCount, runtime.Npcs.Capacity);
        return runtime;
    }

    private sealed class Observer(IWorldItemStateCommitSink inner, Action beforeDrop) : IWorldItemStateCommitSink
    {
        public int Count { get; private set; }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind == WorldItemStateCommitKind.Drop) { Count++; beforeDrop(); }
            inner.WorldItemStateCommitted(kind, in snapshot, reservationOffer, excludedSource);
        }
    }

    private static byte[][] Drain(BoundedOutboundQueue queue)
    {
        var result = new List<byte[]>();
        while (queue.TryRead(out var frame)) result.Add(frame.Bytes.ToArray());
        return result.ToArray();
    }
    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) => new(checked((ushort)(3 + payload.Length)),
        (byte)id, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));
}
