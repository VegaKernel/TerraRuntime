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

public sealed class MoonLordHeadTests
{
    private static readonly JsonElement[] ReferenceCases = ReadCases("MoonLordHead1458", "b53358c6945b688feaca399e9a24327e72e667acdf3e084d57c1137b33f3a770");
    private static readonly JsonElement[] RetiredCases = ReadCases("MoonLordHeadRetired1458", "55c486f5d618095e952f21e97b81ad31218d55f4563543c5ae6aa894986963ac");
    public static TheoryData<int> Retired => new(Enumerable.Range(0, 36));
    [Theory]
    [MemberData(nameof(Retired))]
    public void Retired_head_matches_original(int index) => Verify(RetiredCases[index]);

    public static TheoryData<int> Cases => new(Enumerable.Range(0, 3600));
    [Theory]
    [MemberData(nameof(Cases))]
    public void Head_state_and_projectiles_match_original(int index) => Verify(ReferenceCases[index]);

    private static void Verify(JsonElement expected)
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(19),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(replication.TryRegister(connection.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in spawn);
        replication.AdvanceAuthoritativeTick();
        var npcs = new RuntimeNpcStore(commitSink: replication);
        int coreState = expected.TryGetProperty("coreState", out var coreStateElement) ? coreStateElement.GetInt32() : 0;
        float retired = expected.TryGetProperty("retired", out var retiredElement) ? retiredElement.GetSingle() : 0;
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, new NpcAiState(coreState, 0, 0, 0), new NpcAiState(0, 0, 0, 1), 0);
        NpcSnapshot hand = Spawn(npcs, VanillaNpcIds.MoonLordHead, 900, 900, 2, -3,
            new NpcAiState(retired, expected.GetProperty("tick").GetSingle(), expected.GetProperty("side").GetSingle(), 0),
            new NpcAiState(.2f, .3f, 0, expected.GetProperty("frame").GetSingle()), 0);
        var start = new NpcStateUpdate(hand.Type, hand.NetId, 900, 900, 2, -3, 0, hand.Ai,
            hand.Simulation with { FrameCounter = expected.GetProperty("frame").GetDouble(), DamageOverride = 0 });
        Assert.True(npcs.TryUpdate(hand.Handle, in start, out hand));
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        int mode = expected.TryGetProperty("mode", out var modeElement) ? modeElement.GetInt32() : 0;
        vanilla.SetCandidates(mode == 3 ? [] : [new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, mode == 2, false, false)
        { VelocityX = mode == 1 ? 3.25f : 0f, VelocityY = mode == 1 ? -1.75f : 0f }]);
        var projectiles = new RuntimeProjectileStore();
        while (queue.TryRead(out _)) { }
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(vanilla));
        bool synced = queue.TryRead(out var packet);
        Assert.Equal(expected.GetProperty("netUpdate").GetBoolean(), synced);
        if (synced)
        {
            Assert.Equal(23, packet.Bytes.Span[2]);
            Assert.Equal(hand.Handle.Slot, packet.Bytes.Span[3]);
            Assert.Equal(expected.GetProperty("x").GetSingle(), BinaryPrimitives.ReadSingleLittleEndian(packet.Bytes.Span[5..]));
        }
        Assert.False(queue.TryRead(out _));
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
    [InlineData(14, true)]
    [InlineData(15, false)]
    public void Eyelid_frame_controls_authoritative_strikes(float eyelid, bool accepted)
    {
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        var head = Spawn(npcs, VanillaNpcIds.MoonLordHead, 900, 900, 0, 0,
            default, new NpcAiState(0, 0, 0, eyelid), 0);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        new RuntimeNpcAiStateExecutor(npcs).Tick(new HeadOnly(vanilla));
        var request = new NpcDamageRequest(head.Handle, DamageSource.Server, BaseDamage: 100);
        Assert.Equal(accepted, new RuntimeNpcDamageExecutor(npcs).TryApply(in request, out _));
    }

    [Fact]
    public void Core_spawn_does_not_encode_owner_in_head_eyelid()
    {
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, new NpcAiState(-1, 59, 0, 0),
            new NpcAiState(-1, -1, -1, 1), 0, 20);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        new RuntimeNpcAiStateExecutor(npcs).Tick(new CoreOnly(vanilla));
        var active = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(active);
        var head = Assert.Single(active.Take(count), npc => npc.TypeIdentity == VanillaNpcIds.MoonLordHead);
        Assert.Equal(20f, head.Ai.Ai3);
        Assert.Equal(default, head.Simulation.LocalAi);
    }

    private sealed class CoreOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.TypeIdentity == VanillaNpcIds.MoonLordCore) return inner.TryStepState(in npc, out next);
            next = default; return false;
        }
    }

    [Fact]
    public void Two_continuous_head_cycles_match_original_state_projectiles_and_random_stream()
    {
        JsonElement[] rows = ReadCases("MoonLordHeadContinuous1458", "405a45608d1796c0b3e8be07d9d04433399dcd0ec102893967bbd12a57cd6062");
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, VanillaNpcIds.MoonLordCore, 1000, 1000, 0, 0, default, new NpcAiState(0, 0, 0, 1), 0);
        var head = Spawn(npcs, VanillaNpcIds.MoonLordHead, 900, 900, 2, -3, default, new NpcAiState(.2f, .3f, 0, 0), 0);
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore();
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var stepper = new HeadOnly(vanilla);
        var shots = new ProjectileSnapshot[projectiles.Capacity];
        foreach (JsonElement row in rows)
        {
            executor.Tick(stepper);
            Assert.True(npcs.TryGet(head.Handle, out var actual));
            Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
            AssertAi(row.GetProperty("ai"), actual.Ai);
            AssertAi(row.GetProperty("local"), actual.Simulation.LocalAi);
            Assert.Equal(row.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
            int count = projectiles.CopyActive(shots);
            var expectedShots = row.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), count);
            for (int j = 0; j < count; j++)
            {
                var expected = expectedShots[j]; var shot = shots[j];
                Assert.Equal(expected.GetProperty("type").GetInt32(), shot.Type.Value);
                Assert.Equal(expected.GetProperty("x").GetSingle(), shot.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), shot.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(value => value.GetSingle()),
                    new[] { shot.Ai.Ai0, shot.Ai.Ai1, shot.Ai.Ai2 });
            }
        }
        Assert.Equal(rows[^1].GetProperty("nextRandom").GetInt32(), random.Next());
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
        // Unmodified original Linux TerrariaServer 1.4.5.8 AI_079, netMode=0:
        // NewProjectile executes normally. Only synthetic inputs and numeric outputs are retained.
        using Stream stream = typeof(MoonLordHeadTests).Assembly.GetManifestResourceStream(resource)!;
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

    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
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
