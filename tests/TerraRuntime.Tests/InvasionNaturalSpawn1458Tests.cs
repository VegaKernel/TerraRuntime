using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InvasionNaturalSpawn1458Tests
{
    [Fact]
    public void Actual_selected_Goblin_birth_and_sync_match_source_body_cursor_and_packet23()
    {
        using var stream = typeof(InvasionNaturalSpawn1458Tests).Assembly.GetManifestResourceStream("InvasionGoblinBirth1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            var registry = new RuntimeNpcReplicationRegistry();
            var queue = Endpoint(registry);
            var store = new RuntimeNpcStore(commitSink: registry);
            store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
            Assert.True(store.TryCreateNaturalSpawnPreview(random, out var preview));
            var type = RuntimeInvasionSpawn1458.SelectGoblin(false, false, new SystemVanillaNpcRandom(preview!.Random));
            var update = new NpcStateUpdate(type.Value, checked((short)type.Value), 0f, 0f, 0f, 0f, 0,
                default, NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
            Assert.True(preview.TryStage(in update, 808f, 480f, out var staged));
            Assert.Empty(Drain(queue));
            Assert.Equal(0, store.ActiveCount);
            Assert.True(preview.ValidateContext());
            Assert.True(preview.TryAdoptOwned(out var accepted));
            var born = Assert.IsType<NpcSnapshot>(accepted);
            Assert.Equal(row.GetProperty("type").GetInt32(), born.Type);
            Assert.Equal(row.GetProperty("x").GetSingle(), born.PositionX);
            Assert.Equal(row.GetProperty("y").GetSingle(), born.PositionY);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), born.Simulation.LifeMax);
            Assert.False(born.Simulation.TownNpc);
            Assert.Equal(staged, born);
            Assert.Empty(Drain(queue)); // Retained baseline does not prematurely flush a pending birth.
            store.PublishPendingBirths();
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()).ToArray(), Drain(queue).Select(Convert.ToHexString).ToArray());
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        }
    }

    [Fact]
    public void Source_town_flag_is_generation_owned_and_detached_birth_rejects_late_owner_or_random_changes()
    {
        using var stream = typeof(InvasionNaturalSpawn1458Tests).Assembly.GetManifestResourceStream("InvasionGoblinBirth1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        int townCount = 0;
        foreach (var row in json.RootElement.GetProperty("townFlags").EnumerateArray())
        {
            Assert.True(VanillaNpcTownFlags1458.TryGet(new(row.GetProperty("type").GetInt32()), out bool town));
            Assert.Equal(row.GetProperty("town").GetBoolean(), town);
            if (town) townCount++;
        }
        Assert.Equal(39, townCount);
        var loaded = new RuntimeNpcStore();
        var residents = new RuntimeTownNpcStateStore(new WorldNpcPersistence(
            [], [new WorldTownNpc(22, "Andrew", 100f, 100f, false, 25, 29, null, false)], []), [], new WorldDimensions(120, 100));
        Assert.True(residents.TryReserveRuntimeSlots(loaded));
        Assert.True(loaded.TryGetActive(0, out var restored));
        Assert.True(restored.Simulation.TownNpc);
        Assert.True(loaded.TryDespawn(restored.Handle));
        Span<VanillaNpcRetainedSlot> loadedSlots = stackalloc VanillaNpcRetainedSlot[256];
        loaded.CopyRetainedSlots(loadedSlots);
        Assert.False(loadedSlots[0].IsActive);
        Assert.True(loadedSlots[0].Simulation.TownNpc);

        var store = new RuntimeNpcStore();
        var input = new NpcStateUpdate(22, 22, 0f, 0f, 0f, 0f, 255, default, NpcSimulationState.Initial with { TownNpc = null });
        Assert.True(store.TrySpawn(8, in input, out var imported));
        Assert.Null(imported.Simulation.TownNpc);
        Assert.True(store.TrySpawnVanilla(in input, out var guide));
        Assert.True(guide.Simulation.TownNpc);
        var transformed = input with { Type = 3, NetId = 3 };
        Assert.True(store.TryUpdate(imported.Handle, in transformed, out var zombie));
        Assert.False(zombie.Simulation.TownNpc);
        Assert.True(store.TryDespawn(guide.Handle));
        Span<VanillaNpcRetainedSlot> retained = stackalloc VanillaNpcRetainedSlot[256];
        store.CopyRetainedSlots(retained);
        Assert.False(retained[guide.Handle.Slot].IsActive);
        Assert.True(retained[guide.Handle.Slot].Simulation.TownNpc);
        var random = new VanillaUnifiedRandom1458(1458);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        Assert.True(store.TryCreateNaturalSpawnPreview(random, out var preview));
        var goblin = input with { Type = 28, NetId = 28, Simulation = NpcSimulationState.Initial };
        Assert.True(preview!.TryStage(in goblin, 808f, 480f, out _));
        var before = random.Clone();
        Assert.True(store.TrySpawn(12, in goblin, out _));
        Assert.False(preview.TryAdoptOwned(out _));
        Assert.True(random.HasSameState(before));
        Assert.True(store.TryCreateNaturalSpawnPreview(random, out preview));
        Assert.True(preview!.TryStage(in goblin, 808f, 480f, out _));
        _ = random.Next();
        before = random.Clone();
        Assert.False(preview.TryAdoptOwned(out _));
        Assert.True(random.HasSameState(before));
    }

    [Fact]
    public void Live_Goblin_spawn_uses_current_invasion_and_refuses_selected_unknown_context_atomically()
    {
        int seed = Enumerable.Range(0, 10000).First(value => new VanillaUnifiedRandom1458(value).Next(20) == 0);
        var registry = new RuntimeNpcReplicationRegistry();
        var queue = Endpoint(registry);
        var store = new RuntimeNpcStore(commitSink: registry);
        var world = new WorldTileStore(new WorldDimensions(400, 220));
        for (int x = 0; x < 400; x++)
            world.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var random = new SystemVanillaNpcRandom(seed);
        var owner = new RuntimeWorldInvasion1458(default(InvasionState1458) with { Type = 1, Size = 120, SizeStart = 120, X = 200 });
        var state = new ServerRuntimeState(npcs: store, npcAiStepper: new Quiet(), npcReplication: registry,
            worldTiles: world, worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 120, RockLayer = 160, SpawnTileY = 100 },
            invasion: owner, naturalSpawnRandom: random, invasionProgressPublisher: _ => { });
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9912), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 60, 0, 0, 0, 0, 0)));
        _ = Drain(queue);
        state.Tick();
        Assert.Equal(1, store.ActiveCount);
        Assert.True(store.TryGetActive(0, out var born));
        Assert.Contains(born.Type, new[] { 26, 27, 28, 29, 111 });
        Assert.Equal(1, state.AppliedNpcSpawns);
        Assert.Single(Drain(queue), frame => frame[2] == 23);
        Assert.True(owner.TryCapture(out var before));
        Assert.True(owner.TryAdopt(in before, new InvasionTransition1458(before.State with { Type = 4 }, default), out _));
        var beforeRandom = random.SourceRandom.Clone();
        state.Tick();
        Assert.True(random.SourceRandom.HasSameState(beforeRandom));
        Assert.Equal(1, store.ActiveCount);
        AssertLateSpawnContextMutation(seed, mutateTile: false);
        AssertLateSpawnContextMutation(seed, mutateTile: true);
    }

    private static void AssertLateSpawnContextMutation(int seed, bool mutateTile)
    {
        var registry = new RuntimeNpcReplicationRegistry();
        var queue = Endpoint(registry);
        var store = new RuntimeNpcStore(commitSink: registry);
        var world = new WorldTileStore(new WorldDimensions(400, 220));
        for (int x = 0; x < 400; x++) world.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var random = new SystemVanillaNpcRandom(seed);
        var owner = new RuntimeWorldInvasion1458(default(InvasionState1458) with { Type = 1, Size = 120, SizeStart = 120, X = 200 });
        var state = new ServerRuntimeState(npcs: store, npcAiStepper: new Quiet(), npcReplication: registry,
            worldTiles: world, worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 120, RockLayer = 160, SpawnTileY = 100 },
            invasion: owner, naturalSpawnRandom: random, invasionProgressPublisher: _ => { });
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9921), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 60, 0, 0, 0, 0, 0)));
        _ = Drain(queue);
        int calls = 0;
        store.SetVanillaSpawnContextSource(() =>
        {
            if (++calls == 2)
            {
                if (mutateTile)
                    world.Set(200, 100, world.Get(200, 100)); // Equal bytes still change owned section history.
                else
                {
                    Assert.True(owner.TryCapture(out var current));
                    Assert.True(owner.TryAdopt(in current, new InvasionTransition1458(current.State with { Type = 3 }, default), out _));
                }
            }
            return new(1f, 1, false);
        });
        var before = random.SourceRandom.Clone();
        state.Tick();
        Assert.Equal(2, calls);
        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(0, state.AppliedNpcSpawns);
        Assert.True(random.SourceRandom.HasSameState(before));
        Assert.Empty(Drain(queue));
    }

    private sealed class Quiet : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry)
    {
        var source = GameCommandSourceId.FromConnection(9911);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(registry.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new(1), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(connection, in spawn);
        return queue;
    }
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var frames = new List<byte[]>();
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (owned.TryRead(out OutboundFrame frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
