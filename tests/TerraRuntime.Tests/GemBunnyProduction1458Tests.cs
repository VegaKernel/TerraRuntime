using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class GemBunnyProduction1458Tests
{
    [Fact]
    public void Default_server_composition_creates_updates_and_catches_each_exact_nonresident_subtype()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyWireRuntimeLinux1458");
        foreach (int type in Enumerable.Range(646, 7))
        {
            var rows = json.RootElement.EnumerateArray().Where(row => row.GetProperty("outer").GetBoolean() &&
                row.GetProperty("type").GetInt32() == type && row.GetProperty("scene").GetString() == "birth").ToArray();
            var tiles = GemBunnyRuntime1458Tests.Scene("birth");
            Assert.True(tiles.TryAttachWorldSurface(80d));
            var replication = new RuntimeNpcReplicationRegistry();
            var npcs = new RuntimeNpcStore(commitSink: replication);
            var items = new RuntimeWorldItemStore();
            var random = new VanillaUnifiedRandom1458(1458);
            var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldItems: items,
                npcReplication: replication, naturalSpawnRandom: new SystemVanillaNpcRandom(random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9040), session.Handle);
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
            state.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 660f, 1558f, false, 0, 0, false, 0,
                    false, 0, 0, 0, 0, false, 0, 0)));
            var creation = new NpcStateUpdate(type, (short)type, 639f, 1580f, 0, 0, 255,
                new(0, 40, 0, 0), NpcSimulationState.Initial with {
                    DirectionX = 1, SpriteDirection = -1, LocalAi = new(0, 0, 0, 9) });
            Assert.True(npcs.TrySpawnVanilla(in creation, out var birth));
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(birth.TypeIdentity, birth.NetIdentity, out var definition));
            Assert.True(definition.SupportsRuntimeLifecycle);
            Assert.Equal(false, birth.Simulation.TownNpc);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9041, 1);
            GemBunnyWire1458Tests.Drain(outbound);
            foreach (var row in rows)
            {
                state.Tick();
                Assert.True(npcs.TryGet(birth.Handle, out var current));
                GemBunnyLifecycle1458Tests.Same(row.GetProperty("after"), current);
                Assert.Equal(row.GetProperty("after").GetProperty("next").GetInt32(), random.Clone().Next());
                Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray(),
                    GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray());
            }
            state.Apply(new ClientNpcCatchRuntimeCommand(connection, new TerrariaNpcCatchState(birth.Handle.Slot)));
            Assert.False(npcs.TryGet(birth.Handle, out _));
            Assert.True(items.TryGetActive(0, out var caught));
            Assert.Equal((short)(4838 + type - 646), caught.ItemNetId);
            Assert.DoesNotContain(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9042, 2)),
                frame => frame[2] == 60);
        }
    }
}
