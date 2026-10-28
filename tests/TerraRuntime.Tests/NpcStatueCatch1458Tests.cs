using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcStatueCatch1458Tests
{
    public static IEnumerable<object[]> OriginalCatches()
    {
        using var stream = typeof(NpcStatueCatch1458Tests).Assembly
            .GetManifestResourceStream("GoldCritterCatchInitializedLinux1458v2")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("rows").EnumerateArray())
                yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalCatches))]
    public void Admitted_statue_catches_match_original_inactive23_smoke106_and_complete_rng(int seed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        using var f = new Fixture(seed, row.GetProperty("before").GetProperty("npc"));
        GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("before").GetProperty("rng"), f.Random);
        f.World.Apply(f.Command);
        Assert.Equal(row.GetProperty("after").GetProperty("npc").GetProperty("active").GetBoolean(),
            f.Npcs.TryGet(f.Npc.Handle, out var retained));
        if (retained.Handle.IsAssigned) Assert.Equal(f.Npc, retained);
        Assert.Equal(row.GetProperty("activeItems").GetInt32(), f.Items.ActiveCount);
        GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()!).ToArray(),
            GemBunnyWire1458Tests.Drain(f.Outbound).Select(Convert.ToHexString).ToArray());
        // Sea Turtle 625 remains active: its source catchItem=0 forbids both
        // ordinary and statue catch before PoofOfSmoke or any random offers.
        if (row.GetProperty("type").GetInt32() == 625)
        {
            Assert.Equal(0, row.GetProperty("before").GetProperty("npc").GetProperty("catchItem").GetInt32());
            Assert.Equal(f.Npc, retained);
        }
        else
        {
            var after = f.Random.Clone();
            f.World.Apply(f.Command);
            Assert.True(after.HasSameState(f.Random));
            Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
            Assert.Empty(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(f.Registry, 9112, 2)));
        }
    }

    [Theory]
    [InlineData(46)] [InlineData(303)] [InlineData(337)]
    [InlineData(443)] [InlineData(540)] [InlineData(646)]
    public void A_custom_random_adapter_cannot_partially_despawn_a_statue_catch(int type)
    {
        using var document = SourceCase(type);
        var random = new RefuseRandom();
        using var f = new Fixture(1458, document.RootElement.GetProperty("before").GetProperty("npc"), random);
        var before = f.Random.Clone();
        f.World.Apply(f.Command);
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged));
        Assert.Equal(f.Npc, unchanged);
        Assert.True(before.HasSameState(f.Random));
        Assert.Equal(0, random.Calls);
        Assert.Equal(0, f.Items.ActiveCount);
        Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
    }

    [Fact]
    public void Removal_and_smoke_entropy_are_adopted_before_a_reentrant_catch_observer()
    {
        using var document = SourceCase(46);
        var row = document.RootElement;
        var sink = new CatchObserver();
        using var f = new Fixture(1458, row.GetProperty("before").GetProperty("npc"), observer: sink);
        int calls = 0;
        sink.OnCatch = () => {
            calls++;
            Assert.False(f.Npcs.TryGet(f.Npc.Handle, out _));
            GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
            f.World.Apply(f.Command);
            Assert.Equal(0, f.Items.ActiveCount);
        };
        f.World.Apply(f.Command);
        Assert.Equal(1, calls);
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(v => v.GetString()!).ToArray(),
            GemBunnyWire1458Tests.Drain(f.Outbound).Select(Convert.ToHexString).ToArray());
        GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
    }

    [Theory]
    [InlineData(46)] [InlineData(443)] [InlineData(646)]
    public void A_detached_player_cannot_spend_smoke_entropy_or_remove_an_actor(int type)
    {
        using var document = SourceCase(type);
        using var f = new Fixture(1458, document.RootElement.GetProperty("before").GetProperty("npc"));
        f.World.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        GemBunnyWire1458Tests.Drain(f.Outbound);
        var before = f.Random.Clone();
        f.World.Apply(f.Command);
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged));
        Assert.Equal(f.Npc, unchanged);
        Assert.True(before.HasSameState(f.Random));
        Assert.Equal(0, f.Items.ActiveCount);
        Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
    }

    [Theory]
    [InlineData(74, true)] [InlineData(355, true)] [InlineData(444, true)]
    [InlineData(625, false)] [InlineData(687, false)]
    public void Smoke_transport_uses_the_source_positive_catch_item_condition_without_an_ai7_family_gate(int type, bool expected)
    {
        // Transport-only capability: the real catch owner separately admits
        // lifecycle/defaults. This does not admit another NPC's autonomous AI.
        var registry = new RuntimeNpcReplicationRegistry();
        var outbound = GemBunnyWire1458Tests.Endpoint(registry, 9113, 1);
        GemBunnyWire1458Tests.Drain(outbound);
        var caught = new NpcSnapshot(new(0, new(1)), new(1), type, (short)type,
            1023.25f, 1560.75f, 0, 0, 255, default,
            NpcSimulationState.Initial with { SpawnedFromStatue = true });
        byte[] literalSmoke = Convert.FromHexString("07006AE8630F66");
        registry.PublishStatueCatchSmoke(in caught, literalSmoke);
        var frames = GemBunnyWire1458Tests.Drain(outbound);
        if (expected) Assert.Equal(literalSmoke, Assert.Single(frames));
        else Assert.Empty(frames);
    }

    private static JsonDocument SourceCase(int type)
    {
        foreach (var data in OriginalCatches())
        {
            if ((int)data[0] != 1458) continue;
            var document = JsonDocument.Parse((string)data[1]);
            if (document.RootElement.GetProperty("type").GetInt32() == type &&
                document.RootElement.GetProperty("statue").GetBoolean()) return document;
            document.Dispose();
        }
        throw new InvalidOperationException($"No independently captured statue case for {type}.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ServerRuntimeState World;
        internal readonly NpcSnapshot Npc;
        internal readonly ConnectionHandle Connection;
        internal readonly ClientNpcCatchRuntimeCommand Command;
        internal readonly TerrariaConnectionOutboundQueue Outbound;
        private readonly PlayerJoinSession session;

        internal Fixture(int seed, JsonElement source, IVanillaNpcRandom? npcRandom = null, CatchObserver? observer = null)
        {
            Random = new(seed);
            if (observer is not null) observer.Registry = Registry;
            Npcs = new(commitSink: observer is null ? Registry : observer);
            int type = source.GetProperty("type").GetInt32();
            float Float(string property) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(source.GetProperty(property).GetString(), 16));
            var update = new NpcStateUpdate(type, source.GetProperty("netId").GetInt16(), Float("x"), Float("y"),
                0, 0, 255, default,
                NpcSimulationState.Initial with { Life = source.GetProperty("life").GetInt32(),
                    LifeMax = source.GetProperty("lifeMax").GetInt32(), SpawnedFromStatue = source.GetProperty("statue").GetBoolean(),
                    HitboxOverride = new(source.GetProperty("width").GetInt32(), source.GetProperty("height").GetInt32()),
                    DirectionX = 1, DirectionY = 1, SpriteDirection = 1, ReleaseOwner = 255 });
            Assert.True(Npcs.TrySpawnVanilla(in update, out Npc));
            Assert.Equal((ulong)source.GetProperty("generation").GetByte(), Npc.Handle.Generation.Value);
            Assert.Equal(source.GetProperty("spawnNeedsSyncing").GetBoolean(), Npcs.HasPendingBirth(Npc.Handle));
            Assert.Equal(source.GetProperty("releaseOwner").GetInt16(), Npc.Simulation.ReleaseOwner);
            Assert.Equal(source.GetProperty("friendly").GetBoolean(), Npc.Simulation.Friendly);
            Assert.Equal(source.GetProperty("townNpc").GetBoolean(), Npc.Simulation.TownNpc);
            Assert.Equal(Float("difficulty"), Npc.Simulation.SpawnDifficulty);
            Assert.Equal(source.GetProperty("direction").GetInt32(), Npc.Simulation.DirectionX);
            Assert.Equal(source.GetProperty("directionY").GetInt32(), Npc.Simulation.DirectionY);
            Assert.Equal(source.GetProperty("spriteDirection").GetInt32(), Npc.Simulation.SpriteDirection);
            Assert.Equal(source.GetProperty("target").GetUInt16(), Npc.Target);
            Assert.Equal(source.GetProperty("ai").EnumerateArray().Select(v =>
                BitConverter.UInt32BitsToSingle(Convert.ToUInt32(v.GetString(), 16))).ToArray(),
                new[] { Npc.Ai.Ai0, Npc.Ai.Ai1, Npc.Ai.Ai2, Npc.Ai.Ai3 });
            // Source NewNPC and its birth publication precede the fresh CatchNPC RNG.
            // Runtime creation likewise precedes binding the shared stream.
            World = new(npcs: Npcs, worldItems: Items, npcReplication: Registry,
                naturalSpawnRandom: npcRandom ?? new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9110), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(Connection, session,
                new(session.Slot, 64, 100, 0, 0, 0, 0, 0)));
            Command = new(Connection, new(Npc.Handle.Slot));
            Outbound = GemBunnyWire1458Tests.Endpoint(Registry, 9111, 1);
            GemBunnyWire1458Tests.Drain(Outbound);
        }

        public void Dispose() => session.Dispose();
    }

    private sealed class RefuseRandom : IVanillaNpcRandom
    {
        internal int Calls;
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Calls++; throw new InvalidOperationException("unowned RNG"); }
    }

    private sealed class CatchObserver : INpcStateCommitSink
    {
        internal RuntimeNpcReplicationRegistry Registry = null!;
        internal Action? OnCatch;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Registry.NpcStateCommitted(kind, in npc);
        public void NpcCatchCommitted(in NpcSnapshot npc, bool spawnNeedsSyncing)
        { Registry.NpcCatchCommitted(in npc, spawnNeedsSyncing); OnCatch?.Invoke(); }
    }
}
