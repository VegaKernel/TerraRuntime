using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WiringMoonBoulder1458Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<object[]> OriginalFlights()
    {
        using var input = typeof(WiringMoonBoulder1458Tests).Assembly.GetManifestResourceStream("WiringMoonBoulderLinux1458")!;
        using var compressed = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(compressed);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalFlights))]
    public void Moon_Lord_boulder_pulses_directional_and_ordinary_plates_in_source_subupdates(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var board = new Board(row);
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            board.Tick();
            Assert.Equal(WiringLogicGate1458Tests.Decode(point.GetProperty("torch")), board.Tiles.Get(33, 40));
            Assert.Equal(point.GetProperty("mechanisms").GetInt32(), board.Circuit.RegisteredMechanismCount);
            // The existing Moon Boulder motion sync cadence is outside this plate continuation.
            // Keep every circuit frame, emitter birth, flame birth and projectile destruction.
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(f => f.GetString()!),
                GemBunnyWire1458Tests.Drain(board.Queue).Where(frame => !IsOrdinaryBoulderSync(frame)).Select(Convert.ToHexString));
            Assert.Equal(point.GetProperty("birthImage").EnumerateArray().Select(s =>
                (s.GetProperty("x").GetSingle(), s.GetProperty("y").GetSingle(), s.GetProperty("local0").GetSingle())),
                board.EmitterBirthBodies);
            board.EmitterBirthBodies.Clear();

            var expectedShots = point.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), board.Shots.ActiveCount);
            foreach (var expected in expectedShots.EnumerateArray())
            {
                Assert.True(board.Shots.TryGetActive(expected.GetProperty("slot").GetUInt16(), out var shot));
                Assert.True(board.Shots.TryGetLifecycle(shot.Handle, out var life));
                Assert.Equal(expected.GetProperty("type").GetUInt16(), shot.Type.Value);
                Assert.Equal(expected.GetProperty("owner").GetByte(), shot.Spawner);
                Assert.Equal(expected.GetProperty("x").GetSingle(), shot.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), shot.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                Assert.Equal(expected.GetProperty("damage").GetInt16(), shot.Damage);
                Assert.Equal(expected.GetProperty("knockBack").GetSingle(), shot.KnockBack);
                Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
                var ai = expected.GetProperty("ai");
                var local = expected.GetProperty("localAi");
                Assert.Equal(new ProjectileAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle()), shot.Ai);
                Assert.Equal(new ProjectileLocalAiState(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle()), life.LocalAi);
            }
            var rng = point.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(),
                Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(board.Random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(board.Random)!);
        }
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("statue")]
    [InlineData("friendly NPC")]
    [InlineData("mine")]
    public void Unrepresented_source_or_damage_phase_keeps_boulder_physics_without_pulsing(string scenario)
    {
        using var json = JsonDocument.Parse((string)OriginalFlights().First()[0]);
        var board = new Board(json.RootElement, scenario != "unowned");
        if (scenario == "statue")
            board.Tiles.Set(31, 40, new WorldTile { Type = 105, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        if (scenario == "mine")
            board.Tiles.Set(31, 39, new WorldTile { Type = 210, Flags = WorldTileFlags.Active });
        if (scenario == "friendly NPC")
            Assert.True(board.Npcs.TrySpawn(1, new NpcStateUpdate(22, 22, 478, 620, 0, 0, ushort.MaxValue, default,
                NpcSimulationState.Initial with { Friendly = true }), out _));

        board.Tick();
        Assert.True(board.Shots.TryGetActive(0, out var shot));
        Assert.True(board.Shots.TryGetLifecycle(shot.Handle, out var life));
        Assert.Equal(2, life.LocalAi.Ai2);
        Assert.Equal(0, board.Tiles.Get(33, 40).FrameX);
        Assert.Equal(0, board.Circuit.RegisteredMechanismCount);
        Assert.All(GemBunnyWire1458Tests.Drain(board.Queue), frame => Assert.True(IsOrdinaryBoulderSync(frame)));
    }

    private static bool IsOrdinaryBoulderSync(byte[] packet)
    {
        var frame = new TerrariaFrame((ushort)packet.Length, packet[2], new ReadOnlySequence<byte>(packet),
            new ReadOnlySequence<byte>(packet.AsMemory(3)));
        return TerrariaProjectileDecoder.TryDecodeUpdate(in frame, out var state) == TerrariaProjectileDecodeResult.Decoded &&
            state.ProjectileType == VanillaProjectileIds.MoonBoulder.Value && state.Key.ProjectileIndex == 0;
    }

    private sealed class Board
    {
        internal readonly WorldTileStore Tiles = new(new(80, 100));
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeProjectileStore Shots;
        internal readonly TerrariaConnectionOutboundQueue Queue = new(new(512, 262144, 8192));
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly WorldWiringCircuit1458 Circuit;
        internal readonly List<(float X, float Y, float LocalAi2)> EmitterBirthBodies = [];
        private readonly ServerRuntimeComposition graph;

        internal Board(JsonElement row, bool linkSource = true)
        {
            Assert.True(Tiles.TryAttachWorldSurface(15));
            var wires = row.GetProperty("colors").GetInt32() == 15 ? WorldTileFlagMasks.Wires : WorldTileFlags.WireRed;
            var active = wires | WorldTileFlags.Active;
            int facing = row.GetProperty("facing").GetInt32();
            for (int x = 30; x <= 33; x++) Tiles.Set(x, 40, new WorldTile { Flags = wires });
            Tiles.Set(30, 40, facing == 4 ? new WorldTile { Type = 135, Flags = active, FrameY = 90 } :
                new WorldTile { Type = 442, Flags = active, FrameX = checked((short)(22 * facing)) });
            Tiles.Set(33, 40, new WorldTile { Type = 4, Flags = active });
            if (row.GetProperty("birth").GetBoolean())
            {
                Tiles.Set(32, 39, new WorldTile { Flags = wires });
                Tiles.Set(32, 38, new WorldTile { Type = 137, Flags = active, FrameY = 36 });
            }

            var tileReplication = new RuntimeTileManipulationReplicationRegistry();
            var shotReplication = new RuntimeProjectileReplicationRegistry();
            var sink = new ShotObserver(shotReplication, CaptureEmitterBirth);
            Shots = new RuntimeProjectileStore(1001, sink);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(1458221), new(new PlayerSlotId(1), new(1)));
            Assert.True(tileReplication.TryRegister(peer.Source, Queue));
            Assert.True(shotReplication.TryRegister(peer.Source, Queue));
            var request = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            tileReplication.PlayerSpawned(peer, request);
            shotReplication.PlayerSpawned(peer, request);
            var state = new ServerRuntimeState(worldTiles: Tiles, npcs: Npcs, projectiles: Shots, projectileReplication: shotReplication,
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random), tileManipulationReplication: tileReplication);
            graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(state)!;
            Circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Hidden)!.GetValue(graph.WorldTileAuthority)!;
            Assert.True(Npcs.TrySpawn(0, new NpcStateUpdate(VanillaNpcIds.MoonLordHead.Value, (short)VanillaNpcIds.MoonLordHead.Value,
                480, 614, 0, 0, ushort.MaxValue, default, NpcSimulationState.Initial with { Friendly = false }), out var source));

            var before = row.GetProperty("before");
            var intent = new NpcAiProjectileIntent(VanillaProjectileIds.MoonBoulder,
                before.GetProperty("x").GetSingle(), before.GetProperty("y").GetSingle(),
                before.GetProperty("vx").GetSingle(), before.GetProperty("vy").GetSingle(),
                before.GetProperty("damage").GetInt32(), before.GetProperty("knockBack").GetSingle());
            Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(Shots, linkSource ? source.Handle : default, intent, out var parent));
            Assert.Equal(0, parent.Handle.Slot);
            _ = GemBunnyWire1458Tests.Drain(Queue);
            Random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        }

        internal void Tick()
        {
            graph.WorldTileAuthority.TickMechanisms();
            Assert.True(graph.Projectiles.TryTickState());
        }

        private void CaptureEmitterBirth()
        {
            Assert.True(Shots.TryGetActive(0, out var parent));
            Assert.True(Shots.TryGetLifecycle(parent.Handle, out var life));
            EmitterBirthBodies.Add((parent.PositionX, parent.PositionY, life.LocalAi.Ai2));
        }
    }

    private sealed class ShotObserver(RuntimeProjectileReplicationRegistry replication, Action onEmitterBirth) : IProjectileStateCommitSink
    {
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            replication.ProjectileStateCommitted(kind, in snapshot);
            if (kind == ProjectileStateCommitKind.Spawn && snapshot.Type.Value == 187)
                onEmitterBirth();
        }
    }
}
