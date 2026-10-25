using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InvasionPendingDeath1458Tests
{
    [Fact]
    public void Pending_Captain_source39_birth_keeps_parent_wire_and_local_death_journal_through_callbacks()
    {
        using var resource = typeof(InvasionPendingDeath1458Tests).Assembly.GetManifestResourceStream("InvasionPirateBirth1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        var row = document.RootElement.GetProperty("rows").EnumerateArray().Single(value =>
            value.GetProperty("seed").GetInt32() == 39 && value.GetProperty("type").GetInt32() == 216);
        var expectedBirth = Convert.FromHexString(Assert.Single(row.GetProperty("frames").EnumerateArray()).GetString()!);
        foreach (string mode in new[] { "normal", "throw", "nested" })
        {
            var fixture = new Fixture(row, pirate: true);
            bool observedBirth = false, observedLoot = false;
            VanillaUnifiedRandom1458? nestedRandom = null;
            fixture.OnSpawn = () =>
            {
                if (observedBirth) return;
                observedBirth = true;
                Assert.False(fixture.Npcs.TryGet(fixture.Target.Handle, out _));
                var peer = fixture.Join(2);
                Assert.DoesNotContain(Drain(peer), frame => frame[2] == 23 && frame[3] == fixture.Target.Handle.Slot);
                if (mode == "throw") throw new InvalidOperationException("pending Captain observer witness");
            };
            fixture.OnDrop = () =>
            {
                if (observedLoot) return;
                observedLoot = true;
                Assert.True(fixture.Invasion.TryCapture(out var accepted));
                Assert.Equal(7, accepted.State.Size);
                var peer = fixture.Join(3);
                Assert.DoesNotContain(Drain(peer), frame => frame[2] == 23 && frame[3] == fixture.Target.Handle.Slot);
                if (mode == "nested")
                {
                    Assert.True(fixture.Npcs.TryGetActive(1, out var ghost));
                    Assert.Equal(662, ghost.Type);
                    Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                        fixture.Pipeline.TryStrikeEnvironment(ghost.Handle, 100000));
                    nestedRandom = fixture.Random.Clone();
                }
            };
            if (mode == "throw")
            {
                var error = Assert.Throws<InvalidOperationException>(() =>
                    fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, 100000));
                Assert.Equal("pending Captain observer witness", error.Message);
            }
            else
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                    fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, 100000));
            Assert.True(observedBirth);
            Assert.True(observedLoot);
            var frames = Drain(fixture.First);
            var parentFrames = frames.Where(frame => frame[2] is 23 or 28 && frame[3] == fixture.Target.Handle.Slot).ToArray();
            Assert.Equal(3, parentFrames.Length);
            Assert.Equal(expectedBirth, parentFrames[0]);
            Assert.Equal(28, parentFrames[1][2]);
            Assert.Equal(23, parentFrames[2][2]);
            Assert.Equal(0, parentFrames[2][24] & 8);
            Assert.True(fixture.Invasion.TryCapture(out var final));
            Assert.Equal(mode == "nested" ? 6 : 7, final.State.Size);
            Assert.Equal(new[] { mode == "nested" ? 6 : 7 }, fixture.PublishedSizes);
            if (nestedRandom is not null) Assert.True(fixture.Random.HasSameState(nestedRandom));
            var late = fixture.Join(4);
            Assert.DoesNotContain(Drain(late), frame => frame[2] == 23 && frame[3] == fixture.Target.Handle.Slot);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, 100000));
            Assert.Empty(Drain(fixture.First));
        }
    }

    [Fact]
    public void Pending_goblin_death_keeps_official_birth_before_strike_and_never_restores_dead_join_baseline()
    {
        using var resource = typeof(InvasionPendingDeath1458Tests).Assembly.GetManifestResourceStream("InvasionGoblinBirth1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        foreach (bool throwObserver in new[] { false, true })
        {
            var fixture = new Fixture(row);
            var expectedBirth = Convert.FromHexString(Assert.Single(row.GetProperty("frames").EnumerateArray()).GetString()!);
            TerrariaConnectionOutboundQueue? duringBirth = null;
            TerrariaConnectionOutboundQueue? duringLoot = null;
            bool observedBirth = false;
            fixture.OnSpawn = () =>
            {
                if (observedBirth) return;
                observedBirth = true;
                Assert.False(fixture.Npcs.TryGet(fixture.Target.Handle, out _));
                duringBirth = fixture.Join(2);
                Assert.DoesNotContain(Drain(duringBirth), frame => frame[2] == 23);
                if (throwObserver) throw new InvalidOperationException("pending birth observer witness");
            };
            fixture.OnDrop = () =>
            {
                if (duringLoot is not null) return;
                duringLoot = fixture.Join(3);
                // A historical Spawn sent to existing clients is not a live NPC baseline.
                Assert.DoesNotContain(Drain(duringLoot), frame => frame[2] == 23);
            };
            if (throwObserver)
            {
                var error = Assert.Throws<InvalidOperationException>(() =>
                    fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, 100000));
                Assert.Equal("pending birth observer witness", error.Message);
            }
            else
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                    fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, 100000));
            Assert.True(observedBirth);
            Assert.NotNull(duringBirth);
            Assert.NotNull(duringLoot);
            var frames = Drain(fixture.First);
            var npcFrames = frames.Where(frame => frame[2] is 23 or 28).ToArray();
            Assert.Equal(expectedBirth, npcFrames[0]);
            Assert.Equal(28, npcFrames[1][2]);
            Assert.Equal(23, npcFrames[^1][2]);
            Assert.Equal(3, npcFrames.Length);
            Assert.Equal(0, npcFrames[^1][24] & 8); // Final inactive23 has no SpawnNeedsSyncing.
            Assert.True(Array.FindIndex(frames, frame => frame[2] == 28) <
                Array.FindIndex(frames, frame => frame[2] == 21));
            Assert.Equal(new byte[] { 78, 23 }, frames.TakeLast(2).Select(frame => frame[2]));
            var joinedDuringBirth = Drain(duringBirth).Where(frame => frame[2] is 23 or 28).ToArray();
            Assert.Equal(expectedBirth, joinedDuringBirth[0]);
            Assert.Equal(3, joinedDuringBirth.Length);
            Assert.Single(Drain(duringLoot), frame => frame[2] == 23);
            Assert.False(fixture.Npcs.TryGet(fixture.Target.Handle, out _));
            Assert.True(fixture.Invasion.TryCapture(out var accepted));
            Assert.Equal(11, accepted.State.Size);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, 100000));
            Assert.Empty(Drain(fixture.First));
            var afterDeath = fixture.Join(4);
            Assert.DoesNotContain(Drain(afterDeath), frame => frame[2] == 23);
        }
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeWorldItemReplicationRegistry ItemRegistry = new();
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly RuntimeWorldInvasion1458 Invasion;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly List<int> PublishedSizes = [];
        internal readonly NpcSnapshot Target;
        internal readonly TerrariaConnectionOutboundQueue First;
        internal Action? OnSpawn;
        internal Action? OnDrop;
        private readonly List<TerrariaConnectionOutboundQueue> peers = [];

        internal Fixture(JsonElement row, bool pirate = false)
        {
            Invasion = new(new InvasionState1458(pirate ? 3 : 1, 12, 120, 0, 50, 0,
                0, 120, pirate ? 6 : 4, 0, false, false, false, false));
            First = Join(1);
            Npcs = new(pirate ? 200 : 10, new RuntimeNpcStateCommitFanout(this, Registry));
            Npcs.SetVanillaSpawnContextSource(() => new(1f, 1, false));
            Random = new(row.GetProperty("seed").GetInt32());
            var random = Random;
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
            Assert.True(Npcs.TryCreateNaturalSpawnPreview(random, out var preview));
            var sourceInvasion = pirate ? default(InvasionState1458) with
            { Type = 3, Size = row.GetProperty("size").GetInt32(), SizeStart = row.GetProperty("sizeStart").GetInt32(), X = 50 } : default;
            var type = pirate
                ? RuntimeInvasionSpawn1458.SelectPirate(in sourceInvasion, false, false, static () => false,
                    new SystemVanillaNpcRandom(preview!.Random))
                : RuntimeInvasionSpawn1458.SelectGoblin(false, false, new SystemVanillaNpcRandom(preview!.Random));
            var input = new NpcStateUpdate(type.Value, (short)type.Value, 0, 0, 0, 0, 0,
                default, NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
            Assert.True(preview.TryStage(in input, 808,
                pirate ? row.GetProperty("spawnY").GetInt32() * 16 : 480, out _));
            Assert.True(preview.ValidateContext());
            Assert.True(preview.TryAdoptOwned(out var accepted));
            Target = Assert.IsType<NpcSnapshot>(accepted);
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Clone().Next());
            Assert.True(Npcs.HasPendingBirth(Target.Handle));
            Assert.Empty(Drain(First));
            var items = new RuntimeWorldItemStore(this);
            Pipeline = new(Npcs, items, this, new PlayerAuthority(null, null), () => 0,
                Registry, new(items), ItemRegistry, new(0, true, 0, 0, 0), new(), false, false,
                lootRandom: random, lootRemixWorld: false,
                globalLootWorld: new(100, 80, 1, false, false, false, false, 35, 40, false, false),
                invasion: Invasion, invasionProgressPublisher: capture =>
                {
                    PublishedSizes.Add(capture.State.Size);
                    var state = new TerrariaInvasionProgressState(capture.State.Progress,
                        capture.State.ProgressMax, (sbyte)capture.State.ProgressIcon, 0);
                    Assert.True(TerrariaInvasionProgressCodec.TryEncode(in state, out var encoded));
                    foreach (var peer in peers) Assert.Equal(OutboundEnqueueResult.Enqueued, peer.TryEnqueue(new(encoded)));
                });
        }

        internal TerrariaConnectionOutboundQueue Join(int slot)
        {
            var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(256, 131072, 16384));
            var source = GameCommandSourceId.FromConnection(14580 + slot);
            Assert.True(Registry.TryRegister(source, queue));
            Assert.True(ItemRegistry.TryRegister(source, queue));
            var connection = new ConnectionHandle(source, new(new((byte)slot), new(1)));
            Registry.PlayerSpawned(connection, new(connection.Player.Slot, 1, 1, 0, 0, 0, 0, 0));
            ItemRegistry.PlayerSpawned(connection, new(connection.Player.Slot, 1, 1, 0, 0, 0, 0, 0));
            peers.Add(queue);
            return queue;
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with
            {
                Player = new(new(0), new(1)), Revision = new(1), Zones = new(0, 0, 0, 0, 0, 0),
                HasHealth = true, Life = 400, MaxLife = 400, DerivedLifeMax = 400,
                HasMana = true, Mana = 200, MaxMana = 200, NpcLifeCurrent = true
            };
            return slot.Value == 0;
        }

        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            if (kind == NpcStateCommitKind.Spawn) OnSpawn?.Invoke();
        }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            if (kind == WorldItemStateCommitKind.Drop) OnDrop?.Invoke();
            ItemRegistry.WorldItemStateCommitted(kind, in snapshot);
        }
    }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
