using System.Buffers;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InvasionJoinSnapshot1458Tests
{
    [Fact]
    public void Actual_connection_world_and_section_requests_send_one_live_7_78_pair_before_sections_and_49()
    {
        using var world = CreateWorld();
        var outbound = Outbound();
        Assert.True(RuntimeConnectionWorldBinding.TryCreateInitial(world, GameCommandSourceId.FromConnection(601), outbound, out var connection));
        using var binding = connection!;
        var queue = Queue(outbound);
        Assert.Equal(TerrariaFrameSinkResult.Continue, binding.Bootstrap.OnFrame(Hello()));
        Drain(queue);
        Assert.Equal(TerrariaFrameSinkResult.Continue, binding.Bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        var initial = Drain(queue);
        Assert.Equal(new byte[] { 7, 78 }, Ids(initial));
        // Independent Main.SyncAnInvasion capture: progress27/max120/icon4/wave0.
        Assert.Equal("0D004E1B000000780000000400", Convert.ToHexString(initial[1]));
        Assert.Equal(world.CaptureWorldBootstrap().WorldInfoFrame.ToArray(), initial[0]);
        Assert.Equal(TerrariaFrameSinkResult.Continue, binding.Bootstrap.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        var sections = Drain(queue);
        Assert.Equal(new byte[] { 7, 9 }, Ids(sections)[..2]);
        Assert.DoesNotContain(sections, frame => frame[2] == 78);
        Assert.Equal(49, sections[^1][2]);
        Assert.True(Array.FindIndex(sections, frame => frame[2] == 10) > 0);
    }

    [Fact]
    public void Socket_reads_published_detached_pair_until_owner_refresh_and_capture_is_once_per_request()
    {
        using var world = CreateWorld();
        var old = world.CaptureWorldBootstrap();
        Assert.True(world.Invasion.TryCapture(out var before));
        var transition = new TerraRuntime.Gameplay.Worlds.InvasionTransition1458(before.State with { Size = 19, SizeStart = 333, Type = 3 }, default);
        Assert.True(world.Invasion.TryAdopt(in before, in transition, out _));
        world.WorldProgression.MarkCompleted(VanillaWorldProgressionId.GoblinArmy);
        Assert.Same(old, world.CaptureWorldBootstrap());
        world.RefreshWorldBootstrap();
        var fresh = world.CaptureWorldBootstrap();
        Assert.NotSame(old, fresh);
        Assert.Equal((sbyte)1, old.Runtime.InvasionType);
        Assert.Equal((sbyte)3, fresh.Runtime.InvasionType);
        Assert.Equal("0D004E3A0100004D0100000600", Convert.ToHexString(fresh.ProgressFrame.Span));

        var outbound = Outbound();
        using var sink = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), outbound, world.BootstrapPackets);
        int captures = 0;
        sink.SetWorldResponseSource(() => { captures++; world.RefreshWorldBootstrap(); return captures == 1 ? old : fresh; });
        sink.OnFrame(Hello());
        Drain(Queue(outbound));
        sink.OnFrame(Frame(TerrariaMessageId.RequestWorldData, []));
        var frames = Drain(Queue(outbound));
        Assert.Equal(1, captures);
        Assert.Equal(old.WorldInfoFrame.ToArray(), frames[0]);
        Assert.Equal(old.ProgressFrame.ToArray(), frames[1]);
    }

    [Fact]
    public void Transfer_captures_fresh_owner_pair_and_inactive_completion_has_no_phantom_78()
    {
        using var world = CreateWorld();
        Assert.True(world.Invasion.TryCapture(out var before));
        var transition = new TerraRuntime.Gameplay.Worlds.InvasionTransition1458(before.State with { Type = 0, Size = 0 }, default);
        Assert.True(world.Invasion.TryAdopt(in before, in transition, out _));
        world.WorldProgression.MarkCompleted(VanillaWorldProgressionId.GoblinArmy);
        var outbound = Outbound();
        Assert.True(RuntimeConnectionWorldBinding.TryCreateInitial(world, GameCommandSourceId.FromConnection(602), outbound, out var connection));
        using var binding = connection!;
        Assert.Equal(OutboundEnqueueResult.Enqueued, binding.TryQueueWorldBootstrap());
        var frames = Drain(Queue(outbound));
        Assert.Equal(7, frames[0][2]);
        Assert.Equal(9, frames[1][2]);
        Assert.DoesNotContain(frames, frame => frame[2] == 78);
        Assert.Equal(49, frames[^1][2]);
        var snapshot = world.CaptureWorldBootstrap();
        Assert.Equal((sbyte)0, snapshot.Runtime.InvasionType);
        Assert.True(snapshot.Runtime.ProgressionMutations!.Value.IsCompleted(VanillaWorldProgressionId.GoblinArmy));
        Assert.Equal(snapshot.WorldInfoFrame.ToArray(), frames[0]);
    }

    [Fact]
    public void Owned_snow_moon_progress_has_source_priority_over_ordinary_invasion()
    {
        using var world = CreateWorld();
        Assert.True(world.WorldClock.TryStartSnowMoon());
        world.RefreshWorldBootstrap();
        var snapshot = world.CaptureWorldBootstrap();
        Assert.True(snapshot.Transient.SnowMoon);
        Assert.Equal((sbyte)1, snapshot.Progress!.Value.Icon);
        Assert.Equal("0D004E00000000190000000101", Convert.ToHexString(snapshot.ProgressFrame.Span));
    }

    [Fact]
    public void Accepted_death_progress_keeps_source_zero_maximum_while_join_sync_uses_one()
    {
        using var world = CreateWorld();
        Assert.True(world.Invasion.TryCapture(out var before));
        var transition = new TerraRuntime.Gameplay.Worlds.InvasionTransition1458(before.State with
        {
            Size = 2, SizeStart = 0, Progress = -2, ProgressMax = 0, ProgressIcon = 4, ProgressWave = 0
        }, default);
        Assert.True(world.Invasion.TryAdopt(in before, in transition, out var accepted));
        var outbound = Outbound();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(603),
            new(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
        world.RuntimeConnections.PlayerSpawned(connection, in spawn);
        Drain(Queue(outbound));
        typeof(WorldRuntime).GetMethod("PublishInvasionProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(world, [accepted]);
        var frames = Drain(Queue(outbound));
        Assert.Single(frames);
        // Independent source Goblin27.checkDead(Size3, SizeStart0) reports -2/0 after its one credit.
        Assert.Equal("0D004EFEFFFFFF000000000400", Convert.ToHexString(frames[0]));
        Assert.Equal("0D004EFEFFFFFF010000000400", Convert.ToHexString(world.CaptureWorldBootstrap().ProgressFrame.Span));
    }

    [Fact]
    public void Actual_owner_completion_emits_source_order_and_publishes_completed_join_snapshot()
    {
        foreach (int type in new[] { 1, 3, 4 })
        {
            using var world = CreateWorld();
            Assert.True(world.Invasion.TryCapture(out var before));
            var transition = new TerraRuntime.Gameplay.Worlds.InvasionTransition1458(before.State with
            {
                Type = type, Size = 0, X = 9, Warning = 0
            }, default);
            Assert.True(world.Invasion.TryAdopt(in before, in transition, out _));
            var outbound = Outbound();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(604),
                new(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
            Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outbound));
            var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
            world.RuntimeConnections.PlayerSpawned(connection, in spawn);
            Drain(Queue(outbound));
            typeof(WorldRuntime).GetMethod("AdvanceInvasion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, null);
            var frames = Drain(Queue(outbound));
            Assert.Equal(new byte[] { 98, 82, 7, 82, 82 }, Ids(frames));
            Assert.Equal(type switch { 1 => "0500620A00", 3 => "0500620B00", _ => "0500620D00" }, Convert.ToHexString(frames[0]));
            var snapshot = world.CaptureWorldBootstrap();
            Assert.Equal((sbyte)0, snapshot.Runtime.InvasionType);
            Assert.True(snapshot.ProgressFrame.IsEmpty);
            Assert.Equal(snapshot.WorldInfoFrame.ToArray(), frames[2]);
            Assert.True(snapshot.Runtime.ProgressionMutations!.Value.IsCompleted(type switch
            {
                1 => VanillaWorldProgressionId.GoblinArmy,
                3 => VanillaWorldProgressionId.PirateInvasion,
                _ => VanillaWorldProgressionId.MartianMadness
            }));
        }
    }

    private static WorldRuntime CreateWorld()
    {
        var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Join invasion", 1458, 32, 24, WorldGenerationOptions.Default);
        var result = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits())
            .Materialize(source, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error);
        var loaded = result.World! with
        {
            RuntimeMetadata = new WorldFileRuntimeMetadata
            {
                SpawnX = 10, SpawnY = 10, WorldSurface = 12, RockLayer = 16, DayTime = false,
                InvasionType = 1, InvasionSize = 93, InvasionSizeStart = 120, InvasionX = 10
            }
        };
        return new(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()), source, loaded, result.Bootstrap!,
            new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 2 });
    }

    private static TerrariaConnectionOutboundQueue Outbound() => new(new OutboundQueueOptions(256, 1_048_576, 65_536));
    private static BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue outbound) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
    private static byte[][] Drain(BoundedOutboundQueue queue)
    {
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
    private static byte[] Ids(byte[][] frames) => frames.Select(frame => frame[2]).ToArray();
    private static TerrariaFrame Hello() => Frame(TerrariaMessageId.Hello, [11, .. System.Text.Encoding.ASCII.GetBytes("Terraria326")]);
    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(checked((ushort)(3 + payload.Length)), (byte)id, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));
}
