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
    private static readonly JsonElement[] ReferenceCases = ReadCases();

    public static TheoryData<int> Cases => new(Enumerable.Range(0, 3600));

    [Theory]
    [MemberData(nameof(Cases))]
    public void State_and_projectiles_match_original_hand_AI(int index)
    {
        JsonElement expected = ReferenceCases[index];
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        NpcSnapshot hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 2, -3,
            new NpcAiState(0, expected.GetProperty("tick").GetSingle(), expected.GetProperty("side").GetSingle(), 0),
            new NpcAiState(.2f, .3f, 0, 0), 0);
        var start = new NpcStateUpdate(hand.Type, hand.NetId, 900, 900, 2, -3, 0, hand.Ai,
            hand.Simulation with { FrameCounter = expected.GetProperty("frame").GetDouble(), DamageOverride = 0 });
        Assert.True(npcs.TryUpdate(hand.Handle, in start, out hand));
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore();
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HandOnly(vanilla));
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

    [Fact]
    public void Attack_transition_is_sent_before_normal_replication_cadence()
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
        var hand = Spawn(npcs, VanillaNpcIds.MoonLordHand, 900, 900, 0, 0, new NpcAiState(0, 49, 0, 0), default, 0);
        while (queue.TryRead(out _)) { }
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        new RuntimeNpcAiStateExecutor(npcs).Tick(new HandOnly(vanilla));
        Assert.True(queue.TryRead(out OutboundFrame packet));
        Assert.Equal(23, packet.Bytes.Span[2]);
        Assert.Equal(hand.Handle.Slot, packet.Bytes.Span[3]);
        Assert.False(queue.TryRead(out _));
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

    private static JsonElement[] ReadCases()
    {
        // Unmodified original Linux TerrariaServer 1.4.5.8 AI_078, netMode=0:
        // NewProjectile executes normally. Only synthetic inputs and numeric outputs are retained.
        using Stream stream = typeof(MoonLordHandTests).Assembly.GetManifestResourceStream("MoonLordHands1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("d3a5ecf7d781ab4b548372eaf621a3f606db9de347d1905cac0bed9607898fc1",
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
