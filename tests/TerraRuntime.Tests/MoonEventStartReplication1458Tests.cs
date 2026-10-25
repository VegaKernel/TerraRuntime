using System.Diagnostics;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonEventStartReplication1458Tests
{
    [Fact]
    public async Task Live_start_publishes_world_before_initial_indicator_and_keeps_real_join_requirement()
    {
        // Independent source contract: MessageBuffer case 61 (-4/-5) sends 7 then 78(0,1,icon,1).
        // Main.SyncAnInvasion instead sends the current wave requirement (25 for wave one).
        foreach (bool pumpkin in new[] { false, true })
        {
            var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Moon start", 1458,
                32, 24, WorldGenerationOptions.Default);
            var materializer = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance,
                ServerWorldLoadPolicy.CreateLimits());
            var materialized = materializer.Materialize(source, CancellationToken.None);
            Assert.True(materialized.Succeeded, materialized.Error);
            var loaded = materialized.World! with
            {
                RuntimeMetadata = new WorldFileRuntimeMetadata
                {
                    SpawnX = 10, SpawnY = 10, Time = 1_000, DayTime = false,
                    WorldSurface = 12, RockLayer = 16
                }
            };
            using var world = new WorldRuntime(
                new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
                source, loaded, materialized.Bootstrap!, new InterestManagementControl(),
                new WorldRuntimeOptions { MaxPlayers = 2 });
            world.WorldClock.SetDayRate(0);
            // Setup runs before the owner starts; the test never writes a live clock from a worker.
            Assert.True(pumpkin ? world.WorldClock.TryStartPumpkinMoon() : world.WorldClock.TryStartSnowMoon());
            var initialSnapshot = world.CaptureFreshWorldBootstrap();
            string joinHex = pumpkin ? "0D004E00000000190000000201" : "0D004E00000000190000000101";
            Assert.Equal(joinHex, Convert.ToHexString(initialSnapshot.ProgressFrame.Span));

            var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65_536, 1_024));
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1479),
                new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
            Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outbound));
            var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
            world.RuntimeConnections.PlayerSpawned(connection, in spawn);
            var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
            while (queue.TryRead(out _)) { }

            world.Start();
            try
            {
                var received = new List<byte[]>();
                var deadline = Stopwatch.StartNew();
                while (received.Count < 2 && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    while (queue.TryRead(out OutboundFrame frame))
                        if (frame.Bytes.Span[2] is 7 or 78)
                            received.Add(frame.Bytes.ToArray());
                    if (received.Count < 2)
                        await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                Assert.Equal(new byte[] { 7, 78 }, received.Select(frame => frame[2]).ToArray());
                string startHex = pumpkin ? "0D004E00000000010000000201" : "0D004E00000000010000000101";
                Assert.Equal(startHex, Convert.ToHexString(received[1]));
                // Socket readers receive one immutable baseline with actual wave progress.
                Assert.Equal(joinHex, Convert.ToHexString(world.CaptureWorldBootstrap().ProgressFrame.Span));
                Assert.Equal(joinHex, Convert.ToHexString(initialSnapshot.ProgressFrame.Span));
            }
            finally
            {
                Assert.True(await world.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false));
            }
        }
    }

    [Fact]
    public void Initial_indicator_is_consumed_once_and_day_stop_cancels_an_unpublished_start()
    {
        var clock = new RuntimeWorldClock(0, false, default, 0, 1, freezeWind: true, freezeRain: true);
        Assert.True(clock.TryStartSnowMoon());
        Assert.Equal((sbyte)1, clock.ConsumeMoonEventStartSyncRequest());
        Assert.Equal((sbyte)0, clock.ConsumeMoonEventStartSyncRequest());

        var ending = new RuntimeWorldClock(RuntimeWorldClock.NightLength, false, default, 0, 1,
            freezeWind: true, freezeRain: true);
        Assert.True(ending.TryStartPumpkinMoon());
        ending.Tick();
        Assert.False(ending.MoonEventActive);
        Assert.Equal((sbyte)0, ending.ConsumeMoonEventStartSyncRequest());
    }
}
