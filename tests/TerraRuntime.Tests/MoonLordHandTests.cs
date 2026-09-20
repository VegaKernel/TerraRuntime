using System.IO.Compression;
using System.Reflection;
using System.Buffers.Binary;
using TerraRuntime.Network;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonLordHandTests
{
    private static readonly JsonElement[] ReferenceCases = ReadCases("MoonLordHands1458", "d3a5ecf7d781ab4b548372eaf621a3f606db9de347d1905cac0bed9607898fc1");
    private static readonly JsonElement[] TargetCases = ReadCases("MoonLordHandTargets1458", "6bd1013cf7860a9b035e01d8f61a533b1048fad989f42839f90e9769eced23e3");
    private static readonly JsonElement[] ReleaseCases = ReadCases("MoonLordSphereRelease1458", "3e6440a9b0292eabeab9db6738f80b07dc8096ca63a2a85009609548cdc03aef");
    private static readonly JsonElement[] RetiredCases = ReadCases("MoonLordHandRetired1458", "e3d85bf8fe4387551d40cb0b79c9a1d71977f1b2ff7aeefcd41c3768466d8e94");
    public static TheoryData<int> Releases => new(Enumerable.Range(0, 48));

    [Theory]
    [MemberData(nameof(Releases))]
    public void Sphere_release_matches_original(int index) => Verify(ReleaseCases[index], true);

    public static TheoryData<int> Targets => new(Enumerable.Range(0, 80));

    [Theory]
    [MemberData(nameof(Targets))]
    public void Target_prediction_matches_original(int index) => Verify(TargetCases[index]);

    public static TheoryData<int> Cases => new(Enumerable.Range(0, 3600));

    [Theory]
    [MemberData(nameof(Cases))]
    public void State_and_projectiles_match_original_hand_AI(int index) => Verify(ReferenceCases[index]);

    public static TheoryData<int> Retired => new(Enumerable.Range(0, 64));

    [Theory]
    [MemberData(nameof(Retired))]
    public void Retired_state_matches_original_hand_AI(int index) => Verify(RetiredCases[index], retired: true);

    [Fact]
    public void Continuous_hand_attack_cycles_match_original_state_projectiles_and_random_stream()
    {
        JsonElement[] rows = ReadCases("MoonLordHandsContinuous1458", "01138e8d1c53167047314125733ad2df65e449200ba557bd90880eb69d9e1cb2");
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        NpcSnapshot hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 2, -3,
            new NpcAiState(0, 0, 0, 0), new NpcAiState(.2f, .3f, 0, 0), 0);
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore();
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var shots = new ProjectileSnapshot[projectiles.Capacity];
        foreach (JsonElement row in rows)
        {
            Assert.Equal(1, executor.Tick(new HandOnly(vanilla)).Applied);
            Assert.True(npcs.TryGet(hand.Handle, out var actual));
            Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(row.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
            Assert.Equal(row.GetProperty("nextFrame").GetDouble(), actual.Simulation.FrameCounter);
            AssertAi(row.GetProperty("ai"), actual.Ai);
            AssertAi(row.GetProperty("local"), actual.Simulation.LocalAi);
            Assert.Equal(row.GetProperty("target").GetInt32(), actual.Target);
            int count = projectiles.CopyActive(shots);
            JsonElement expectedShots = row.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), count);
            for (int j = 0; j < count; j++)
            {
                JsonElement expected = expectedShots[j]; ProjectileSnapshot shot = shots[j];
                Assert.Equal(expected.GetProperty("type").GetInt32(), shot.Type.Value);
                Assert.Equal(expected.GetProperty("x").GetSingle(), shot.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), shot.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                Assert.Equal(expected.GetProperty("damage").GetInt32(), shot.Damage);
                Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(value => value.GetSingle()),
                    new[] { shot.Ai.Ai0, shot.Ai.Ai1, shot.Ai.Ai2 });
            }
        }
        Assert.Equal(rows[^1].GetProperty("nextRandom").GetInt32(), random.Next());
    }

    [Fact]
    public void Continuous_hand_attack_cycles_with_a_moving_player_match_original_state_projectiles_and_random_stream()
    {
        JsonElement[] rows = ReadCases("MoonLordHandsMoving1458", "be9bb6006eab3144dad9a673551a143b3204a72d09f8e5c2d56a4896f13e5509");
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        NpcSnapshot hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 2, -3,
            new NpcAiState(0, 0, 0, 0), new NpcAiState(.2f, .3f, 0, 0), 0);
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        var projectiles = new RuntimeProjectileStore();
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var shots = new ProjectileSnapshot[projectiles.Capacity];
        for (int tick = 0; tick < rows.Length; tick++)
        {
            vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f + tick * 3.25f, 821f - tick * 1.75f, 0,
                true, false, false, false) { VelocityX = 3.25f, VelocityY = -1.75f }]);
            Assert.Equal(1, executor.Tick(new HandOnly(vanilla)).Applied);
            JsonElement row = rows[tick];
            Assert.True(npcs.TryGet(hand.Handle, out var actual));
            Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(row.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
            Assert.Equal(row.GetProperty("nextFrame").GetDouble(), actual.Simulation.FrameCounter);
            AssertAi(row.GetProperty("ai"), actual.Ai);
            AssertAi(row.GetProperty("local"), actual.Simulation.LocalAi);
            Assert.Equal(row.GetProperty("target").GetInt32(), actual.Target);
            int count = projectiles.CopyActive(shots);
            JsonElement expectedShots = row.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), count);
            for (int j = 0; j < count; j++)
            {
                JsonElement expected = expectedShots[j]; ProjectileSnapshot shot = shots[j];
                Assert.Equal(expected.GetProperty("type").GetInt32(), shot.Type.Value);
                Assert.Equal(expected.GetProperty("x").GetSingle(), shot.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), shot.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                Assert.Equal(expected.GetProperty("damage").GetInt32(), shot.Damage);
                Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(value => value.GetSingle()),
                    new[] { shot.Ai.Ai0, shot.Ai.Ai1, shot.Ai.Ai2 });
            }
        }
        Assert.Equal(rows[^1].GetProperty("nextRandom").GetInt32(), random.Next());
    }

    private static void Verify(JsonElement expected, bool release = false, bool retired = false)
    {
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        NpcSnapshot hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 2, -3,
            new NpcAiState(retired ? -2f : 0f, expected.GetProperty("tick").GetSingle(), expected.GetProperty("side").GetSingle(), 0),
            new NpcAiState(.2f, .3f, 0, 0), 0);
        var start = new NpcStateUpdate(hand.Type, hand.NetId, 900, 900, 2, -3, 0, hand.Ai,
            hand.Simulation with { FrameCounter = expected.GetProperty("frame").GetDouble(), DamageOverride = 0 });
        Assert.True(npcs.TryUpdate(hand.Handle, in start, out hand));
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        int mode = expected.TryGetProperty("mode", out var modeElement) ? modeElement.GetInt32() : 0;
        vanilla.SetCandidates(mode == 3 ? [] : [new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, mode == 2, false, false)
        { VelocityX = mode == 1 ? 3.25f : 0f, VelocityY = mode == 1 ? -1.75f : 0f }]);
        var replication = new RuntimeProjectileReplicationRegistry();
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(12),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(replication.TryRegister(connection.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in spawn);
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        var projectiles = new RuntimeProjectileStore(commitSink: replication);
        if (release)
        {
            for (int j = 0; j < 4; j++)
            {
                var intent = new NpcAiProjectileIntent(VanillaProjectileIds.PhantasmalSphere, 910 + j, 810 + j, 1, -2, 0, 0)
                { InitialAi = new ProjectileAiState(j == 2 ? -1 : 0, j == 3 ? 2 : 1, 0) };
                Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(projectiles, hand.Handle, in intent, out _));
            }
        }
        while (queue.TryRead(out _)) { }
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HandOnly(vanilla));
        if (release)
        {
            bool released = expected.GetProperty("shots")[0].GetProperty("ai")[0].GetSingle() == -1f;
            for (int j = 0; j < (released ? 2 : 0); j++)
            {
                Assert.True(queue.TryRead(out var packet));
                Assert.Equal(27, packet.Bytes.Span[2]);
                // Official packet27: four-byte ProjectileKey, position then velocity.
                var shot = expected.GetProperty("shots")[j];
                Assert.Equal(shot.GetProperty("x").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(packet.Bytes.Span[7..]));
                Assert.Equal(shot.GetProperty("vx").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(packet.Bytes.Span[15..]));
                Assert.Equal(shot.GetProperty("vy").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(packet.Bytes.Span[19..]));
            }
            Assert.False(queue.TryRead(out _));
        }
        Assert.True(npcs.TryGet(hand.Handle, out var actual));
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
        Assert.Equal(expected.GetProperty("nextFrame").GetDouble(), actual.Simulation.FrameCounter);
        AssertAi(expected.GetProperty("ai"), actual.Ai);
        AssertAi(expected.GetProperty("local"), actual.Simulation.LocalAi);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        var shots = new ProjectileSnapshot[projectiles.Capacity];
        int count = projectiles.CopyActive(shots);
        Assert.Equal(expected.GetProperty("shots").GetArrayLength(), count);
        for (int shotIndex = 0; shotIndex < count; shotIndex++)
        {
            var golden = expected.GetProperty("shots")[shotIndex]; var shot = shots[shotIndex];
            Assert.Equal(golden.GetProperty("type").GetInt32(), shot.Type.Value);
            Assert.Equal(golden.GetProperty("x").GetSingle(), shot.PositionX);
            Assert.Equal(golden.GetProperty("y").GetSingle(), shot.PositionY);
            Assert.Equal(golden.GetProperty("vx").GetSingle(), shot.VelocityX);
            Assert.Equal(golden.GetProperty("vy").GetSingle(), shot.VelocityY);
            Assert.Equal(golden.GetProperty("damage").GetInt32(), shot.Damage);
            Assert.Equal(golden.GetProperty("ai").EnumerateArray().Select(v => v.GetSingle()), new[] { shot.Ai.Ai0, shot.Ai.Ai1, shot.Ai.Ai2 });
        }
        Assert.Equal(expected.GetProperty("nextRandom").GetInt32(), random.Next());
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Incoming_frame_controls_authoritative_damage(double frame, bool accepted)
    {
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        var hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 0, 0,
            new NpcAiState(0, 49, 0, 0), default, 0);
        var state = new NpcStateUpdate(hand.Type, hand.NetId, hand.PositionX, hand.PositionY,
            0, 0, 0, hand.Ai, hand.Simulation with { FrameCounter = frame });
        Assert.True(npcs.TryUpdate(hand.Handle, in state, out hand));
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        new RuntimeNpcAiStateExecutor(npcs).Tick(new HandOnly(vanilla));
        var request = new NpcDamageRequest(hand.Handle, DamageSource.Server, BaseDamage: 100);
        Assert.Equal(accepted, new RuntimeNpcDamageExecutor(npcs).TryApply(in request, out _));
    }

    [Theory]
    [InlineData(49, 0)]
    [InlineData(509, 3)]
    public void Attack_transition_is_sent_before_normal_replication_cadence(float tick, float state)
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 131072, 1024));
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        var source = GameCommandSourceId.FromConnection(1);
        Assert.True(replication.TryRegister(source, outbound));
        var player = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(player.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(player, in spawn);
        replication.AdvanceAuthoritativeTick();
        var npcs = new RuntimeNpcStore(commitSink: replication);
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        var hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 0, 0, new NpcAiState(state, tick, 0, 0), default, 0);
        while (queue.TryRead(out _)) { }
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        new RuntimeNpcAiStateExecutor(npcs).Tick(new HandOnly(vanilla));
        Assert.True(queue.TryRead(out OutboundFrame packet));
        Assert.Equal(23, packet.Bytes.Span[2]);
        Assert.Equal(hand.Handle.Slot, packet.Bytes.Span[3]);
        Assert.False(queue.TryRead(out _));
    }

    [Fact]
    public void Live_player_movement_reaches_hand_prediction_through_world_authority()
    {
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, new NpcAiState(1, 0, 0, 0), new NpcAiState(0, 0, 0, 1), 0);
        var hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 2, -3,
            new NpcAiState(3, 150, 1, 0), new NpcAiState(.2f, .3f, 0, 0), 0);
        var runtime = new ServerRuntimeState(npcs: npcs);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(11), session.Handle);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 94, 53, 0, 0, 0, 0, 0)));
        runtime.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0, 4, 0, 0, 0, 1500, 800,
                true, 3.25f, -1.75f, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Assert.True(runtime.TryCapturePlayerSnapshot(connection.Player, out var moving));
        Assert.Equal(3.25f, moving.VelocityX);
        Assert.Equal(-1.75f, moving.VelocityY);
        runtime.Tick();
        Assert.True(npcs.TryGet(hand.Handle, out var actual));
        JsonElement expected = TargetCases.Single(row => row.GetProperty("side").GetInt32() == 1 &&
            row.GetProperty("tick").GetInt32() == 150 && row.GetProperty("mode").GetInt32() == 1);
        AssertAi(expected.GetProperty("local"), actual.Simulation.LocalAi);
    }

    [Fact]
    public void Server_player_movement_reaches_hand_prediction_through_world_authority()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var players = new ServerPlayerStateStore(identities, slots.Capacity);
        var id = new ServerPlayerId("test:moonlord-prediction");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var acquired));
        using var lease = Assert.IsType<ServerPlayerSlotRegistry.ServerPlayerSlotLease>(acquired);
        Assert.True(players.TrySpawn(id, 1500, 800, out _));
        Assert.True(players.TrySetMotion(lease.Player, 1500, 800, 3.25f, -1.75f, out _));
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, new NpcAiState(1, 0, 0, 0), new NpcAiState(0, 0, 0, 1), 0);
        var hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 2, -3,
            new NpcAiState(3, 150, 1, 0), new NpcAiState(.2f, .3f, 0, 0), 0);
        var runtime = new ServerRuntimeState(npcs: npcs, serverPlayers: new ServerPlayerAuthority(players));
        runtime.Tick();
        Assert.True(npcs.TryGet(hand.Handle, out var actual));
        JsonElement expected = TargetCases.Single(row => row.GetProperty("side").GetInt32() == 1 &&
            row.GetProperty("tick").GetInt32() == 150 && row.GetProperty("mode").GetInt32() == 1);
        AssertAi(expected.GetProperty("local"), actual.Simulation.LocalAi);
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore npcs, NpcTypeId type, float x, float y,
        float vx, float vy, NpcAiState ai, NpcAiState local, ushort target, byte? slot = null)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        var state = new NpcStateUpdate(type.Value, checked((short)type.Value), x, y, vx, vy, target, ai,
            NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax, LocalAi = local });
        NpcSnapshot created;
        Assert.True(slot is byte index ? npcs.TrySpawn(index, in state, out created) : npcs.TrySpawnVanilla(in state, out created));
        return created;
    }

    private static void AssertAi(JsonElement expected, NpcAiState actual) =>
        Assert.Equal(expected.EnumerateArray().Select(value => value.GetSingle()),
            new[] { actual.Ai0, actual.Ai1, actual.Ai2, actual.Ai3 });

    private static JsonElement[] ReadCases(string resource, string hash)
    {
        // Unmodified original Linux TerrariaServer 1.4.5.8 AI_078, netMode=0:
        // NewProjectile executes normally. Only synthetic inputs and numeric outputs are retained.
        using Stream stream = typeof(MoonLordHandTests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal(hash,
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private sealed class ReferenceRandom(int seed) : IVanillaNpcRandom
    {
        private readonly Random random = new(seed);
        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
        public double NextDouble() => random.NextDouble();
        public int Next() => random.Next();
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class HandOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Handle.Slot == 1) return inner.TryStepState(in npc, out next);
            next = default;
            return false;
        }
    }
}
