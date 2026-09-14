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

public sealed class MoonLordTeleportTests
{
    public static TheoryData<int> Cases => new(Enumerable.Range(0, 48));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Core_and_all_parts_match_original_distance_transition(int index) => VerifyCase(index, false);

    [Theory]
    [InlineData(12)]
    [InlineData(44)]
    public void Outer_world_motion_preserves_inner_effects_and_does_not_shift_part_offsets(int index) =>
        VerifyCase(index, true);

    private static void VerifyCase(int index, bool worldMotion)
    {
        JsonElement expected = ReadCases()[index];
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
        float state = expected.GetProperty("state").GetSingle();
        bool intro = state == -1f;
        var local = intro ? new NpcAiState(-1f, -1f, -1f, 1f) : new NpcAiState(1f, 2f, 3f, 1f);
        NpcSnapshot core = Spawn(npcs, VanillaNpcIds.MoonLordCore, 100.25f, 100.75f, 2f, -3f,
            new NpcAiState(state, intro ? 59f : 17f, 2f, 0f), local, 0);
        if (!intro)
            for (byte slot = 1; slot <= 3; slot++)
                Spawn(npcs, slot == 3 ? VanillaNpcIds.MoonLordHead : VanillaNpcIds.MoonLordHand,
                    slot * 100, slot * 200, 0, 0, default, default, 255);
        for (byte slot = 10; slot <= 12; slot++)
            Spawn(npcs, slot == 12 ? VanillaNpcIds.BlueSlime : VanillaNpcIds.MoonLordFreeEye,
                slot * 10, slot * 20, 0, 0, default, default, 255, slot);
        while (queue.TryRead(out _)) { }
        var random = new ReferenceRandom(1458);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0,expected.GetProperty("px").GetSingle(),
            expected.GetProperty("py").GetSingle(),0,true,false,false,false)]);
        INpcAiStateStepper stepper = worldMotion
            ? new VanillaNpcWorldMotionAiStepper(vanilla, new WorldTileStore(new WorldDimensions(1000, 1000)))
            : vanilla;
        new RuntimeNpcAiStateExecutor(npcs).Tick(new CoreOnly(stepper));
        Assert.True(npcs.TryGet(core.Handle, out NpcSnapshot actual));
        Assert.Equal(expected.GetProperty("nextX").GetSingle() + (worldMotion ? expected.GetProperty("vx").GetSingle() : 0f), actual.PositionX);
        Assert.Equal(expected.GetProperty("nextY").GetSingle() + (worldMotion ? expected.GetProperty("vy").GetSingle() : 0f), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
        AssertAi(expected.GetProperty("ai"), actual.Ai);
        AssertAi(expected.GetProperty("local"), actual.Simulation.LocalAi);
        JsonElement children = expected.GetProperty("spawned");
        Assert.Equal(children.GetArrayLength() + 1, npcs.ActiveCount);
        foreach (JsonElement child in children.EnumerateArray())
        {
            Assert.True(npcs.TryGetActive(child.GetProperty("slot").GetByte(), out NpcSnapshot part));
            Assert.Equal(child.GetProperty("type").GetInt32(), part.Type);
            Assert.Equal(child.GetProperty("x").GetSingle(), part.PositionX);
            Assert.Equal(child.GetProperty("y").GetSingle(), part.PositionY);
            AssertAi(child.GetProperty("ai"), part.Ai);
        }
        var latest = new Dictionary<short, byte[]>();
        while (queue.TryRead(out OutboundFrame frame))
        {
            byte[] bytes = frame.Bytes.ToArray();
            Assert.Equal(23, bytes[2]);
            latest[bytes[3]] = bytes;
        }
        if (expected.GetProperty("ai")[0].GetSingle() == -2f)
        {
            AssertWirePosition(latest[0], actual.PositionX, actual.PositionY);
            foreach (JsonElement child in children.EnumerateArray())
                if (child.GetProperty("netUpdate").GetBoolean())
                    AssertWirePosition(latest[child.GetProperty("slot").GetInt16()],
                        child.GetProperty("x").GetSingle(), child.GetProperty("y").GetSingle());
        }
        Assert.Equal(expected.GetProperty("nextRandom").GetInt32(), random.Next());
    }

    private static void AssertWirePosition(byte[] packet, float x, float y)
    {
        // Original 1.4.5.8 packet23: Byte slot, Byte generation, then two position Singles.
        Assert.Equal(x, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(5)));
        Assert.Equal(y, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(9)));
    }

    [Fact]
    public void Replacement_after_core_commit_cannot_receive_old_teleport_effects()
    {
        var npcs = new RuntimeNpcStore();
        var core = Spawn(npcs, VanillaNpcIds.MoonLordCore, 100.25f, 100.75f, 2f, -3f,
            new NpcAiState(1f, 17f, 2f, 0f), new NpcAiState(1f, 2f, 3f, 1f), 0);
        var eye = Spawn(npcs, VanillaNpcIds.MoonLordFreeEye, 100f, 200f, 0f, 0f, default, default, 255, 10);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ReferenceRandom(1458));
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 3123.25f, 133.75f, 0, true, false, false, false)]);
        new RuntimeNpcAiStateExecutor(npcs).Tick(new CoreOnly(vanilla), new ReplacingSink(npcs));
        Assert.False(npcs.TryGet(core.Handle, out _));
        Assert.True(npcs.TryGetActive(0, out var replacement));
        Assert.Equal(VanillaNpcIds.BlueSlime, replacement.TypeIdentity);
        Assert.Equal(100f, replacement.PositionX);
        Assert.True(npcs.TryGet(eye.Handle, out var unchanged));
        Assert.Equal(eye, unchanged);
    }

    private sealed class ReplacingSink(RuntimeNpcStore npcs) : INpcAiStateCommitSink
    {
        public void NpcAiStateCommitted(in NpcSnapshot snapshot)
        {
            Assert.True(npcs.TryDespawn(snapshot.Handle));
            Spawn(npcs, VanillaNpcIds.BlueSlime, 100, 100, 0, 0, default, default, 255, 0);
        }
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
        // Unmodified original Linux TerrariaServer 1.4.5.8 AI_077, netMode=0:
        // NewNPC executes normally. Only synthetic inputs and numeric outputs are retained.
        using Stream stream = typeof(MoonLordTeleportTests).Assembly.GetManifestResourceStream("MoonLordTeleport1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("7428a2723ef5be3760291cfca2c08bc4641a88199a15024bd75cd7c16cb26852",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private sealed class ReferenceRandom(int seed) : IVanillaNpcRandom
    {
        private readonly Random random = new(seed);
        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
        public int Next() => random.Next();
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class CoreOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Handle.Slot == 0) return inner.TryStepState(in npc, out next);
            next = default;
            return false;
        }
    }
}
