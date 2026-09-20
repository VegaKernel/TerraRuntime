using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class DestroyerChainSpawnTests
{
    public static TheoryData<int> Cases => new(Enumerable.Range(0, 32));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Head_call_creates_original_chain_links_before_any_body_AI(int index)
    {
        using var resource = typeof(DestroyerChainSpawnTests).Assembly.GetManifestResourceStream("DestroyerChainSpawn1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("7632ceed1534deca2283c2a28e2ea9aaae327d3d6e32e394ea0958ddcb704dd3",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        var row = json.RootElement[index];
        bool good = row.GetProperty("good").GetBoolean();
        int root = row.GetProperty("root").GetInt32(), mode = row.GetProperty("mode").GetInt32();
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 0)
            { StartSlot = (byte)root }, out var head));
        for (int slot = root + 1; slot < 200; slot++)
        {
            bool occupied = mode == 1 && (slot == root + 2 || slot == root + 4) ||
                mode == 2 && slot > root + 3 || mode == 3;
            if (!occupied) continue;
            var filler = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 0, default,
                NpcSimulationState.Initial with { CanBeReplacedByOtherNpcs = mode == 3 && slot == root + 2 });
            Assert.True(store.TrySpawn((byte)slot, in filler, out _));
        }
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetWorldConditions(false, false, goodWorld: good);
        vanilla.SetWorldBounds(400, 150);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        vanilla.SetWormEnvironment(new EmptyTerrain());
        var executor = new RuntimeNpcAiStateExecutor(store);
        Assert.Equal(1, executor.Tick(new HeadOnly(vanilla)).Applied);
        Assert.True(store.TryGet(head.Handle, out head));
        Assert.Equal(Ai(row.GetProperty("headAi")), head.Ai);
        var children = new List<NpcSnapshot>();
        for (byte slot = 0; slot < 200; slot++)
            if (store.TryGetActive(slot, out var npc) && npc.Type is 135 or 136) children.Add(npc);
        var expected = row.GetProperty("children");
        Assert.Equal(expected.GetArrayLength(), children.Count);
        for (int childIndex = 0; childIndex < children.Count; childIndex++)
        {
            var actual = children[childIndex]; var reference = expected[childIndex];
            Assert.Equal(reference.GetProperty("slot").GetInt32(), actual.Handle.Slot);
            Assert.Equal(reference.GetProperty("type").GetInt32(), actual.Type);
            Assert.Equal(Ai(reference.GetProperty("ai")), actual.Ai);
            Assert.Equal(Ai(reference.GetProperty("local")), actual.Simulation.LocalAi);
            Assert.Equal(255, actual.Target);
            Assert.Equal(reference.GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
            Assert.Equal(reference.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(reference.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(reference.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(reference.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(reference.GetProperty("life").GetInt32(), actual.Simulation.Life);
            Assert.Equal(reference.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
        }
        // A second head call must not retry a partial chain or duplicate a complete one.
        int count = store.ActiveCount;
        Assert.Equal(1, executor.Tick(new HeadOnly(vanilla)).Applied);
        Assert.Equal(count, store.ActiveCount);
    }

    private static NpcAiState Ai(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle());

    [Fact]
    public void New_chain_overwrites_prior_root_and_stale_generation_cannot_change_follower()
    {
        var store = new RuntimeNpcStore(12);
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 0)
            { StartSlot = 10, InitialAi = new NpcAiState(0, 0, 0, 77) }, out var head));
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetWormEnvironment(new EmptyTerrain());
        var executor = new RuntimeNpcAiStateExecutor(store);
        Assert.Equal(1, executor.Tick(new HeadOnly(vanilla)).Applied);
        Assert.True(store.TryGet(head.Handle, out var current));
        Assert.Equal(new NpcAiState(11, 0, 0, 10), current.Ai);
        Assert.True(store.TryGetActive(11, out var body));
        Assert.Equal(new NpcAiState(200, 10, 0, 10), body.Ai);
        Assert.True(store.TryDespawn(head.Handle));
        var replacement = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(10, in replacement, out var replaced));
        var mutations = (INpcAiCommittedNpcMutationSink)executor;
        Assert.False(mutations.TryLinkFollower(head.Handle, 11));
        Assert.False(mutations.TryLinkFollower(replaced.Handle, 201));
        Assert.True(store.TryGet(replaced.Handle, out current));
        Assert.Equal(default, current.Ai);
    }

    [Fact]
    public void First_body_sees_the_whole_chain_and_all_new_slots_run_in_the_same_world_pass()
    {
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 0)
            { StartSlot = 10 }, out _));
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetWorldConditions(false, false);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        var world = new VanillaNpcWorldMotionAiStepper(vanilla,
            new TerraRuntime.World.WorldTileStore(new TerraRuntime.World.WorldDimensions(400, 400)));
        var observation = new ChainObserver(world, store);
        var result = new RuntimeNpcAiStateExecutor(store).Tick(observation);
        Assert.Equal(82, result.Applied);
        Assert.Equal(81, observation.Children);
    }

    [Fact]
    public void Daytime_rock_layer_boundary_despawns_the_entire_destroyer_chain_in_the_head_step()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 4000, 0, 0, 0)
            { InitialAi = new NpcAiState(1, 0, 0, 0) }, out NpcSnapshot head));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.DestroyerBody, 1000, 3240, 0, 0, 0), out _));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.DestroyerTail, 1000, 3280, 0, 0, 0), out _));
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetWorldBounds(400, 150, 200);
        vanilla.SetWorldConditions(dayTime: true, slimeRainActive: false);
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 100, 100, 0, true, false, false, false)]);
        vanilla.SetWormEnvironment(new EmptyTerrain());
        Assert.Equal(VanillaNpcIds.Destroyer, head.TypeIdentity);
        Assert.True(head.PositionY > 3200f);
        Assert.True(vanilla.TryStepState(in head, out NpcStateUpdate planned));
        Assert.True(vanilla.DeactivatesAfterStep(in head, in planned));

        NpcAiStateTickSummary result = new RuntimeNpcAiStateExecutor(store).Tick(vanilla);

        Assert.Equal(1, result.Applied);
        Assert.Equal(0, store.ActiveCount);
    }

    private sealed class ChainObserver(INpcAiStateStepper inner, RuntimeNpcStore store) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public int Children { get; private set; }
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Type is 135 or 136)
            {
                Assert.Equal(82, store.ActiveCount);
                Assert.True(store.TryGetActive(91, out var tail));
                Assert.Equal(VanillaNpcIds.DestroyerTail, tail.TypeIdentity);
                Children++;
            }
            return inner.TryStepState(in npc, out next);
        }
    }

    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.TypeIdentity == VanillaNpcIds.Destroyer && inner.TryStepState(in npc, out next);
        }
    }

    private sealed class EmptyTerrain : IVanillaWormEnvironment
    {
        public bool IsDigging(float x, float y, int width, int height) => false;
    }
}
