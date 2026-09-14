using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonLordIntroductionTests
{
    public static TheoryData<int> Cases => new(Enumerable.Range(0, 384));

    [Fact]
    public void Parts_use_original_center_before_outer_world_motion_and_integer_offsets()
    {
        var npcs = new RuntimeNpcStore();
        NpcSnapshot core = Spawn(npcs, VanillaNpcIds.MoonLordCore, 100.25f, 100.75f, 2f, -3f,
            new NpcAiState(-1f, 59f, 2f, 0f), new NpcAiState(-1f, -1f, -1f, 1f), 0);
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ReferenceRandom(1458));
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 510f, 321f, 0, true, false, false, false)]);
        var motion = new VanillaNpcWorldMotionAiStepper(vanilla, new WorldTileStore(new WorldDimensions(200, 150)));
        var executor = new RuntimeNpcAiStateExecutor(npcs);
        executor.Tick(new CoreOnly(motion));
        Assert.True(npcs.TryGet(core.Handle, out NpcSnapshot moved));
        Assert.Equal(102.5f, moved.PositionX);
        Assert.Equal(98.25f, moved.PositionY);
        Assert.Equal(0f, moved.Ai.Ai0);
        (float x, float y)[] originalPositions = [(-300f, -33f), (500f, -33f), (104f, -323f)];
        for (byte slot = 1; slot <= 3; slot++)
        {
            Assert.True(npcs.TryGetActive(slot, out NpcSnapshot child));
            Assert.Equal(originalPositions[slot - 1].x, child.PositionX);
            Assert.Equal(originalPositions[slot - 1].y, child.PositionY);
            Assert.Equal((ushort)255, child.Target);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_server_initialization_intro_and_return_match_through_spawn_executor(int index)
    {
        JsonElement expected = ReadCases()[index];
        bool initialized = expected.GetProperty("initialized").GetBoolean();
        float phase = expected.GetProperty("state").GetSingle();
        int tick = expected.GetProperty("tick").GetInt32();
        bool alive = expected.GetProperty("alive").GetBoolean();
        bool existingShell = initialized && phase != -1f;
        var npcs = new RuntimeNpcStore();
        var coreAi = new NpcAiState(phase, tick, 2f, 0f);
        var local = new NpcAiState(existingShell ? 1f : -1f, existingShell ? 2f : -1f,
            existingShell ? 3f : -1f, initialized ? 1f : 0f);
        NpcSnapshot core = Spawn(npcs, VanillaNpcIds.MoonLordCore, expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(), 2f, -3f, coreAi, local, 0);
        if (existingShell)
        {
            Spawn(npcs, VanillaNpcIds.MoonLordHand, 0f, 0f, 0f, 0f, default, default, byte.MaxValue);
            Spawn(npcs, VanillaNpcIds.MoonLordHand, 0f, 0f, 0f, 0f, default, default, byte.MaxValue);
            Spawn(npcs, VanillaNpcIds.MoonLordHead, 0f, 0f, 0f, 0f, default, default, byte.MaxValue);
        }
        var random = new ReferenceRandom(expected.GetProperty("seed").GetInt32());
        var vanilla = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 510f, 321f, 0, alive, false, false, false)]);
        var executor = new RuntimeNpcAiStateExecutor(npcs);
        executor.Tick(new CoreOnly(vanilla));
        Assert.Equal(expected.GetProperty("active").GetBoolean(), npcs.TryGet(core.Handle, out NpcSnapshot actual));
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
            int target = child.GetProperty("target").GetInt32();
            // Original freshly constructed, pre-existing fixture NPCs use -1 for unset target.
            // Runtime's unassigned wire target is 255; newly allocated original parts also use 255.
            Assert.Equal(target < 0 ? (ushort)255 : (ushort)target, part.Target);
            AssertAi(child.GetProperty("ai"), part.Ai);
        }
        Assert.Equal(expected.GetProperty("nextRandom").GetInt32(), random.Next());
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore npcs, NpcTypeId type, float x, float y,
        float vx, float vy, NpcAiState ai, NpcAiState local, ushort target)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        var state = new NpcStateUpdate(type.Value, checked((short)type.Value), x, y, vx, vy, target, ai,
            NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax, LocalAi = local });
        Assert.True(npcs.TrySpawnVanilla(in state, out NpcSnapshot created));
        return created;
    }

    private static void AssertAi(JsonElement expected, NpcAiState actual) =>
        Assert.Equal(expected.EnumerateArray().Select(value => value.GetSingle()),
            new[] { actual.Ai0, actual.Ai1, actual.Ai2, actual.Ai3 });

    private static JsonElement[] ReadCases()
    {
        // Unmodified original Linux TerrariaServer 1.4.5.8 AI_077, netMode=0:
        // NewNPC executes normally. Only synthetic inputs and numeric outputs are retained.
        using Stream stream = typeof(MoonLordIntroductionTests).Assembly.GetManifestResourceStream("MoonLordIntroduction1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("4a06b98adb31bd9568477fc34dab4932746c8f8fe57a935e7f9ef8cefc34b99d",
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
