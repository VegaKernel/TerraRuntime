using System.Reflection;
using System.Diagnostics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Tests;

public sealed class MoonSeasonCommandBoundary1458Tests
{
    [Fact]
    public async Task Paid_typed_command_on_last_night_tick_publishes_start_before_zero_score_and_dawn()
    {
        foreach (bool paid in new[] { false, true })
        {
            using var fixture = new Fixture(player: true, paid: paid);
            var world = fixture.World;
            // Both requests are typed commands in the actual owner queue before its first Tick.
            Assert.True(world.BossSummonIngress.TryPost(fixture.Connection, -5));
            Assert.True(world.BossSummonIngress.TryPost(fixture.Connection, -5));
            world.Start();
            try
            {
                var frames = new List<byte[]>();
                var deadline = Stopwatch.StartNew();
                while (frames.Count(frame => frame[2] == 7) < (paid ? 2 : 1) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    frames.AddRange(fixture.Drain());
                    if (frames.Count(frame => frame[2] == 7) < (paid ? 2 : 1))
                        await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                Assert.Equal(paid ? new byte[] { 82, 82, 7, 78, 82, 7 } : new byte[] { 7 }, Ids(frames));
                if (paid)
                {
                    Assert.Equal("LegacyMisc.34", Key(frames[0]));
                    Assert.Equal("Game.InvasionWave_Type2", Key(frames[1]));
                    Assert.Equal(0, frames[2][7] & 1); // Source packet7 Time(Int32), then DayandMoonInfo.
                    Assert.Equal("0D004E00000000010000000101", Convert.ToHexString(frames[3]));
                    Assert.Equal("Misc.FrostMoonScore", Key(frames[4]));
                    Assert.Equal("0", Literal(frames[4]));
                    Assert.Equal(1, frames[5][7] & 1);
                }
                Assert.True(world.CaptureWorldBootstrap().Runtime.DayTime);
                Assert.False(world.CaptureWorldBootstrap().Transient.SnowMoon);
            }
            finally { Assert.True(await world.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false)); }
            Assert.Equal(paid ? (short)0 : (short)1, fixture.ItemStack);
            Assert.Equal((short)0, fixture.FundedQuantity);
        }
    }

    [Fact]
    public void Accepted_start_and_last_night_progress_use_the_actual_command_and_update_closures()
    {
        using var fixture = new Fixture(player: true, paid: true);
        fixture.Apply(new ClientBossSummonRuntimeCommand(fixture.Connection, -5));
        var start = fixture.Drain();
        Assert.Equal(new byte[] { 82, 82, 7, 78 }, Ids(start));
        // Scalar death acceptance here isolates the poll boundary. The earlier death-authority
        // tests own actual NPC collisions; this test runs the real WorldRuntime update closure.
        Assert.True(fixture.World.WorldClock.TryAdvanceMoonEventDeath(new(338), false, false));
        fixture.Update();
        var end = fixture.Drain();
        Assert.Equal(new byte[] { 78, 82, 7 }, Ids(end));
        Assert.Equal("0D004E01000000190000000101", Convert.ToHexString(end[0]));
        Assert.Equal("Misc.FrostMoonScore", Key(end[1]));
        Assert.Equal("1", Literal(end[1]));
        Assert.Null(fixture.World.CaptureWorldBootstrap().Progress);

        using var synthetic = new Fixture(player: false, paid: false);
        Assert.True(synthetic.World.WorldClock.TryStartSnowMoon());
        Assert.True(synthetic.World.WorldClock.TryAdvanceMoonEventDeath(new(338), false, false));
        synthetic.Update();
        Assert.Equal(new byte[] { 82, 7 }, Ids(synthetic.Drain()));
    }

    [Fact]
    public void Dawn_score_and_clock_world_info_precede_overlapping_invasion_completion()
    {
        using var fixture = new Fixture(player: false, paid: false);
        var world = fixture.World;
        Assert.True(world.WorldClock.TryStartSnowMoon());
        Assert.True(world.Invasion.TryCapture(out var before));
        var transition = new InvasionTransition1458(before.State with { Type = 1, Size = 0, X = 9, Warning = 0 }, default);
        Assert.True(world.Invasion.TryAdopt(in before, in transition, out _));
        fixture.Update();
        var frames = fixture.Drain();
        Assert.Equal(new byte[] { 82, 7, 98, 82, 7, 82, 82 }, Ids(frames));
        Assert.Equal("Misc.FrostMoonScore", Key(frames[0]));
        Assert.Equal("0", Literal(frames[0]));
        Assert.Equal("0500620A00", Convert.ToHexString(frames[2]));
        Assert.True(world.WorldProgression.IsCompleted(VanillaWorldProgressionId.GoblinArmy));
    }

    private static byte[] Ids(IEnumerable<byte[]> frames) => frames.Select(frame => frame[2]).ToArray();
    private static string Key(byte[] frame)
    {
        using var reader = new BinaryReader(new MemoryStream(frame));
        reader.BaseStream.Position = 6;
        Assert.Equal(2, reader.ReadByte());
        return reader.ReadString();
    }
    private static string Literal(byte[] frame)
    {
        using var reader = new BinaryReader(new MemoryStream(frame));
        reader.BaseStream.Position = 6;
        Assert.Equal(2, reader.ReadByte());
        reader.ReadString();
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        return reader.ReadString();
    }

    private sealed class Fixture : IDisposable
    {
        internal WorldRuntime World { get; }
        internal ConnectionHandle Connection { get; }
        private readonly PlayerJoinSession? session;
        private readonly BoundedOutboundQueue queue;
        private readonly RuntimePlayerInventoryStore? inventory;
        private readonly Action<ServerRuntimeState, RuntimeCommand> apply;
        private readonly Action<ServerRuntimeState> update;
        private bool steppedWorkersStarted;
        internal Fixture(bool player, bool paid)
        {
            var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Moon boundary", 1458,
                32, 24, WorldGenerationOptions.Default);
            var materialized = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance,
                ServerWorldLoadPolicy.CreateLimits()).Materialize(source, CancellationToken.None);
            Assert.True(materialized.Succeeded, materialized.Error);
            var loaded = materialized.World! with { RuntimeMetadata = new WorldFileRuntimeMetadata
                { SpawnX = 10, SpawnY = 10, WorldSurface = 12, RockLayer = 16,
                    Time = (int)RuntimeWorldClock.NightLength, DayTime = false } };
            World = new(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()), source, loaded,
                materialized.Bootstrap!, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 2 });
            World.WorldClock.SetDayRate(1);
            var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 131_072, 4096));
            var handle = new PlayerHandle(new(0), new(1));
            if (player)
            {
                Assert.True(World.Slots.TryAcquireConnection(out var lease));
                session = new(lease!);
                session.ObserveWorldRequest(); session.ObserveSectionRequest();
                handle = session.Handle;
            }
            Connection = new(GameCommandSourceId.FromConnection(1481), handle);
            Assert.True(World.RuntimeConnections.TryRegister(Connection.Source, outbound));
            Assert.True(World.NpcReplication.TryRegister(Connection.Source, outbound));
            var spawn = new PlayerSpawnCommitRequest(handle.Slot, 10, 10, 0, 0, 0, 0, 0);
            if (player)
            {
                World.State.Apply(new PlayerHealthRuntimeCommand(Connection, new(handle.Slot, 200, 200)));
                World.State.Apply(new PlayerSpawnRuntimeCommand(Connection, session!, spawn));
                Assert.Equal(PlayerSpawnCommitResult.Committed, World.State.LastSpawnCommitResult);
                var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World.State)!;
                inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Players)!;
                Assert.True(inventory.TrySet(Connection, new(handle.Slot, 0, 1, 0, 1958, 0)));
                var funding = inventory.CaptureFundedQuantities(Connection);
                funding[0] = paid ? (short)1 : (short)0;
                inventory.AdoptFundedQuantities(Connection, funding);
            }
            else World.RuntimeConnections.PlayerSpawned(Connection, in spawn);
            queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
            Drain();
            apply = (Action<ServerRuntimeState, RuntimeCommand>)typeof(AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>)
                .GetField("applyCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World.GameLoop)!;
            update = (Action<ServerRuntimeState>)typeof(AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>)
                .GetField("update", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World.GameLoop)!;
        }
        internal short ItemStack { get { Assert.True(inventory!.TryGet(Connection, 0, out var item)); return item.Stack; } }
        internal short FundedQuantity { get { Assert.True(inventory!.TryGetFundedQuantity(Connection, 0, out var count)); return count; } }
        internal void Apply(RuntimeCommand command) => apply(World.State, command);
        internal void Update()
        {
            // Exercise the production update closure on this sole test owner without starting
            // a competing simulation thread; its immutable section workers still need normal startup.
            if (!steppedWorkersStarted)
            {
                ((SectionCacheRebuildPipeline)typeof(WorldRuntime).GetField("sectionCacheRebuild",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World)!).Start();
                steppedWorkersStarted = true;
            }
            update(World.State);
        }
        internal List<byte[]> Drain()
        {
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame))
                if (frame.Bytes.Span[2] is 7 or 78 or 98 || frame.Bytes.Span[2] == 82 &&
                    frame.Bytes.Span[3] == 1 && frame.Bytes.Span[4] == 0)
                    frames.Add(frame.Bytes.ToArray());
            return frames;
        }
        public void Dispose() { World.Dispose(); session?.Dispose(); }
    }
}
