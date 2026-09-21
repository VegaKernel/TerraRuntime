using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonEventProgressReplicationTests
{
    [Fact]
    public async Task Live_world_broadcasts_packet_78_after_a_qualifying_moon_event_death()
    {
        var source = new SandboxWorldSource.Generated(
            FlatProvider.GeneratorId,
            "Moon progress",
            1458,
            32,
            24,
            WorldGenerationOptions.Default);
        var materializer = new SandboxWorldMaterializer(
            BuiltInWorldGeneratorSource.Instance,
            ServerWorldLoadPolicy.CreateLimits());
        SandboxWorldMaterializationResult materialized = materializer.Materialize(source, CancellationToken.None);
        Assert.True(materialized.Succeeded, materialized.Error);
        WorldFileData loaded = materialized.World! with
        {
            RuntimeMetadata = new WorldFileRuntimeMetadata
            {
                SpawnX = 10,
                SpawnY = 10,
                Time = 1_000,
                DayTime = false,
                WorldSurface = 12,
                RockLayer = 16
            }
        };

        using var world = new WorldRuntime(
            new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
            source,
            loaded,
            materialized.Bootstrap!,
            new InterestManagementControl(),
            new WorldRuntimeOptions { MaxPlayers = 2 });
        world.WorldClock.SetDayRate(0);
        Assert.True(world.WorldClock.TryStartSnowMoon());

        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65_536, 1_024));
        var connection = new ConnectionHandle(
            GameCommandSourceId.FromConnection(1_478),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
        world.RuntimeConnections.PlayerSpawned(connection, in spawn);
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(outbound));

        world.Start();
        try
        {
            Assert.True(world.WorldClock.TryAdvanceMoonEventDeath(new NpcTypeId(338), expertMode: false, masterMode: false));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!deadline.IsCancellationRequested)
            {
                while (queue.TryRead(out OutboundFrame received))
                {
                    if (received.Bytes.Span[2] != 78)
                        continue;

                    Assert.Equal("0D004E01000000190000000101", Convert.ToHexString(received.Bytes.Span));
                    return;
                }

                await Task.Delay(10, deadline.Token);
            }

            throw new Xunit.Sdk.XunitException("The live world did not broadcast Moon-event packet 78.");
        }
        finally
        {
            Assert.True(await world.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false));
        }
    }
}
