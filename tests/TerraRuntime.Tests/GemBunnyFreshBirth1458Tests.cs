using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GemBunnyFreshBirth1458Tests
{
    [Fact]
    public void Real_NewNPC_birth_publishes_the_active_network_offer_before_CheckActive_removal()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyFreshLinux1458");
        int updates = 0, removed = 0;
        foreach (var group in json.RootElement.EnumerateArray().GroupBy(row => (
                     Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!)))
        {
            // The far player remains inside a real world, beyond NPC.activeRangeX.
            var tiles = new WorldTileStore(new(1000, 200));
            for (int x = 0; x < 1000; x++)
                tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Assert.True(tiles.TryAttachWorldSurface(80d));
            var replication = new RuntimeNpcReplicationRegistry();
            var npcs = new RuntimeNpcStore(commitSink: replication);
            var random = new VanillaUnifiedRandom1458(1458);
            var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, npcReplication: replication,
                naturalSpawnRandom: new SystemVanillaNpcRandom(random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9060), session.Handle);
            if (group.Key.Scene != "freshNoPlayer")
            {
                state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
                state.Apply(new PlayerMovementRuntimeCommand(connection,
                    new(session.Slot, 0, 0, 0, 0, 0, group.Key.Scene == "freshFar" ? 6000f : 660f,
                        1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
                if (group.Key.Scene == "freshDead")
                    state.Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 0, 100)));
            }
            // Raw NewNPC retains spawnNeedsSyncing until its first source publication. The
            // existing pending-birth path models that owner, unlike an immediately published host spawn.
            Assert.True(npcs.TrySpawnVanillaPending(new(group.Key.Type, (short)group.Key.Type,
                639f, 1580f, 0, 0, 255, default,
                NpcSimulationState.Initial with { TimeLeft = 937 }), out var birth));
            Assert.Equal(937, birth.Simulation.TimeLeft);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9061, 1);
            GemBunnyWire1458Tests.Drain(outbound);
            foreach (var row in group)
            {
                state.Tick();
                var expected = row.GetProperty("after");
                bool alive = expected.GetProperty("active").GetBoolean();
                bool retained = npcs.TryGet(birth.Handle, out var current);
                Assert.True(alive == retained, $"{group.Key} step{row.GetProperty("step")}: expected active={alive}, " +
                    $"actual active={retained}, x={current.PositionX},y={current.PositionY},life={current.Simulation.Life},time={current.Simulation.TimeLeft}");
                if (alive) GemBunnyLifecycle1458Tests.Same(expected, current);
                else removed++;
                Assert.Equal(expected.GetProperty("next").GetInt32(), random.Clone().Next());
                var expectedFrames = row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
                var actual = GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray();
                Assert.True(expectedFrames.SequenceEqual(actual), $"{group.Key} step{row.GetProperty("step")}: " +
                    $"expected {string.Join(';', expectedFrames)}, actual {string.Join(';', actual)}");
                updates++;
            }
        }
        Assert.Equal(98, updates);
        Assert.Equal(14, removed);
    }
}
