using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FishingNpcBirth1458Tests
{
    [Fact]
    public void Actual_original_direct_and_dedicated_births_pin_quantization_defaults_names_and_next_rng()
    {
        using var resource = typeof(FishingNpcBirth1458Tests).Assembly.GetManifestResourceStream("FishingNpcBirthOriginal1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        int compared = 0, differences = 0;
        foreach (var row in original.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (!row.GetProperty("dedicated").GetBoolean()) continue;
            int type = row.GetProperty("type").GetInt32();
            int seed = row.GetProperty("seed").GetInt32();
            using var f = new Fixture();
            f.Random.CopyStateFrom(new(seed));
            Assert.True(f.Runtime.Npcs.TrySpawnFishingNpc(f.Connection, type,
                row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle()));
            var actors = new NpcSnapshot[200];
            Assert.Equal(1, f.Npcs.CopyActive(actors));
            var actor = actors[0];
            Assert.Equal(row.GetProperty("slot").GetInt32(), actor.Handle.Slot);
            Assert.Equal(type, actor.Type);
            Assert.Equal(row.GetProperty("netId").GetInt32(), actor.NetId);
            Assert.Equal(row.GetProperty("positionX").GetSingle(), actor.PositionX);
            Assert.Equal(row.GetProperty("positionY").GetSingle(), actor.PositionY);
            Assert.Equal(row.GetProperty("target").GetInt32(), actor.Target);
            Assert.Equal(row.GetProperty("life").GetInt32(), actor.Simulation.Life);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), actor.Simulation.LifeMax);
            Assert.Equal(row.GetProperty("scale").GetSingle(), actor.Simulation.Scale);
            Assert.Equal(row.GetProperty("direction").GetInt32(), actor.Simulation.DirectionX);
            Assert.Equal(row.GetProperty("directionY").GetInt32(), actor.Simulation.DirectionY);
            Assert.Equal(default, actor.Ai);
            Assert.Equal(default, actor.Simulation.LocalAi);
            Assert.Equal(row.GetProperty("timeLeft").GetInt32(), actor.Simulation.TimeLeft);
            Assert.Equal(row.GetProperty("wet").GetBoolean(), actor.Simulation.Wet);
            Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), actor.Simulation.NoTileCollide);
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
            if (type == 682)
            {
                Assert.True(f.Town.TryGet(actor.Handle.Slot, out var resident));
                Assert.Equal(row.GetProperty("name").GetString(), resident.GivenName);
                Assert.Equal(row.GetProperty("variation").GetInt32(), resident.TownNpcVariationIndex);
                Assert.True(f.State.WorldProgression.IsSlimeRedSpawnUnlocked);
                Assert.Equal(new[] { "world7", "retained", "spawn23" }, f.Sink.Events);
                Assert.False(f.Runtime.Npcs.TrySpawnFishingNpc(f.Connection, type, 1605.75f, 1613.875f));
            }
            var direct = original.RootElement.GetProperty("rows").EnumerateArray().Single(candidate =>
                !candidate.GetProperty("dedicated").GetBoolean() && candidate.GetProperty("type").GetInt32() == type &&
                candidate.GetProperty("seed").GetInt32() == seed);
            if (type != 370)
            {
                Assert.NotEqual(direct.GetProperty("positionX").GetSingle(), actor.PositionX);
                Assert.NotEqual(direct.GetProperty("positionY").GetSingle(), actor.PositionY);
                differences++;
            }
            compared++;
        }
        Assert.Equal(21, compared);
        Assert.Equal(18, differences);
    }

    [Fact]
    public void Fished_resident_state_and_world_info_precede_retention_observers_and_checkpoint_capture()
    {
        using var f = new Fixture();
        f.Sink.Check = npc =>
        {
            Assert.True(f.State.WorldProgression.IsSlimeRedSpawnUnlocked);
            Assert.True(f.Town.TryGet(npc.Handle.Slot, out var resident));
            Assert.False(string.IsNullOrEmpty(resident.GivenName));
            Assert.Equal(682, resident.NetId);
            Assert.True(resident.Homeless);
            Assert.Equal(255, npc.Target);
        };
        Assert.True(f.Runtime.Npcs.TrySpawnFishingNpc(f.Connection, 682, 1605.75f, 1613.875f));
        Assert.Equal(new[] { "world7", "retained", "spawn23" }, f.Sink.Events);
        var snapshotSource = new RuntimeWorldCheckpointSnapshotSource(f.Tiles, new RuntimeChestStore([]), 1,
            townNpcStore: f.Town, progressionMutations: f.State.WorldProgression);
        while (!snapshotSource.IsTileShadowReady) snapshotSource.CaptureTileBootstrap(16);
        Assert.True(snapshotSource.TryCapture(out var snapshot));
        Assert.NotNull(snapshot);
        Assert.True(snapshot.ProgressionMutations!.Value.UnlockSlimeRedSpawn);
        var saved = Assert.Single(snapshot.Npcs!.TownNpcs);
        Assert.Equal(682, saved.NetId);
        Assert.False(string.IsNullOrEmpty(saved.GivenName));
        Assert.Equal(1591f, saved.X);
        Assert.Equal(1580f, saved.Y);
        var before = f.Random.Clone();
        Assert.False(f.Runtime.Npcs.TrySpawnFishingNpc(f.Connection, 682, 1605.75f, 1613.875f));
        Assert.True(before.HasSameState(f.Random));
        Assert.Equal(1, f.Npcs.ActiveCount);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random = new(0);
        internal readonly Sink Sink = new();
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeTownNpcStateStore Town;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Runtime;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerJoinSession session;

        internal Fixture()
        {
            for (int x = 80; x <= 120; x++) for (int y = 100; y <= 115; y++)
                Tiles.Set(x, y, new() { LiquidAmount = 255 });
            Npcs = new(200, Sink);
            Town = new(new WorldNpcPersistence([], [], []), [], Tiles.Dimensions);
            State = new(npcs: Npcs, worldTiles: Tiles, townNpcs: Town,
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random),
                worldClock: new RuntimeWorldClock(0, true, default, 0, 0, freezeWind: true, freezeRain: true),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80, RockLayer = 180, GameMode = 0 });
            Runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!;
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99811), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.BindFishingWorldInfoPublisher(() =>
            {
                Assert.True(State.WorldProgression.IsSlimeRedSpawnUnlocked);
                var header = new WorldFileHeader("fished", "0", 1, Guid.Empty, 1, 0, 6400, 0, 4800, Tiles.Dimensions);
                var packet = WorldInfoPacketMapper.Create(header, new WorldFileRuntimeMetadata(), runtime:
                    new WorldInfoRuntimeState(0, true, 0, false, false) { ProgressionMutations = State.WorldProgression.CaptureSnapshot() });
                Assert.Equal(16, packet.EventInfo9 & 16);
                using var wire = new MemoryStream(); packet.ToStream(wire, includeHeader: true);
                Assert.Equal(7, wire.ToArray()[2]);
                Sink.Events.Add("world7");
            });
        }
        public void Dispose() => session.Dispose();
    }

    private sealed class Sink : INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal readonly List<string> Events = [];
        internal Action<NpcSnapshot>? Check;
        public void NpcBirthRetained(in NpcSnapshot snapshot)
        {
            Check?.Invoke(snapshot);
            Events.Add("retained");
        }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            if (kind != NpcStateCommitKind.Spawn) return;
            Check?.Invoke(snapshot);
            Assert.True(RuntimeNpcPacketProjection.TryCreate(snapshot, RuntimeNpcSyncKind.Spawn, out var state));
            Assert.True(TerrariaNpcUpdateEncoder.TryEncode(state, out var frame));
            Assert.Equal(23, frame[2]);
            Events.Add("spawn23");
        }
    }
}
