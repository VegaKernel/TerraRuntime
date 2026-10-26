using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class GemBunnyStatueCatch1458Tests
{
    [Fact]
    public void Original_statue_catch_after_runtime_update_publishes_inactive23_then106_and_consumes_source_smoke_rng()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyStatueCatchLinux1458");
        int catches = 0;
        foreach (var group in json.RootElement.EnumerateArray().GroupBy(row => (
                     Type: row.GetProperty("type").GetInt32(), Seed: row.GetProperty("seed").GetInt32())))
        {
            var rows = group.ToArray();
            var replication = new RuntimeNpcReplicationRegistry();
            var f = new GemBunnyLifecycle1458Tests.Fixture(group.Key.Type, "catchStatue", group.Key.Seed,
                rows[0].GetProperty("before"), replication);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9080, 1);
            GemBunnyWire1458Tests.Drain(outbound);
            f.Tick();
            Assert.Equal(rows[0].GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray(),
                GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray());
            var state = new ServerRuntimeState(npcs: f.Npcs, worldItems: f.Items, npcReplication: replication,
                naturalSpawnRandom: new SystemVanillaNpcRandom(f.Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(f.Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9081), session.Handle);
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
            state.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 660f, 1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            state.Apply(new ClientNpcCatchRuntimeCommand(connection, new TerrariaNpcCatchState(f.Birth.Handle.Slot)));
            Assert.False(f.Npcs.TryGet(f.Birth.Handle, out _));
            Assert.Equal(0, f.Items.ActiveCount);
            Assert.Equal(rows[1].GetProperty("after").GetProperty("next").GetInt32(), f.Random.Clone().Next());
            Assert.Equal(rows[1].GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray(),
                GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray());
            Assert.Empty(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9082, 2)));
            catches++;
        }
        Assert.Equal(21, catches);
    }
}
