using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class NpcSlotAllocationTests
{
    private static readonly JsonElement Reference = ReadReference("NpcSlotAllocation1458", "3066507d07e01edb34f8812ffede7c9671e135d930783efca982496268b60bd6");

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void All_original_npc_types_match_allocation_for_the_supplied_slot_state(int mode)
    {
        foreach (var row in Reference.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("mode").GetInt32() != mode) continue;
            var store = CreateArrangement(mode);
            int type = row.GetProperty("type").GetInt32(), expected = row.GetProperty("slot").GetInt32();
            bool created = store.TrySpawnVanilla(State(type), out var result);
            int actual = created ? result.Handle.Slot : -1;
            Assert.True(expected == actual, $"mode={mode}, type={type}, expected={expected}, actual={actual}");
        }
    }

    [Fact]
    public void Protection_decay_matches_original_update_order()
    {
        foreach (var row in Reference.GetProperty("timeline").EnumerateArray())
        {
            var store = new RuntimeNpcStore();
            Assert.True(store.TrySpawn(0, State(1), out var original));
            for (int tick = 0; tick <= row.GetProperty("tick").GetInt32(); tick++)
            {
                store.UpdateProtectedSpawnSlots();
                if (tick == 0) Assert.True(store.TryDespawn(original.Handle));
            }
            Assert.True(store.TrySpawnVanilla(State(401), out var created));
            Assert.Equal(row.GetProperty("slot").GetInt32(), created.Handle.Slot);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void Nonzero_start_matches_original_for_every_type_and_slot_arrangement(int mode)
    {
        var reference = ReadReference("NpcSlotStart1458", "55ad9275bbc2e8fa15e7d15d97e9767067dbe366c0b96339bcb7a1e09f37526a");
        foreach (var row in reference.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("mode").GetInt32() != mode) continue;
            var store = CreateArrangement(mode);
            int type = row.GetProperty("type").GetInt32(), start = row.GetProperty("start").GetInt32();
            int expected = row.GetProperty("slot").GetInt32();
            bool created = store.TrySpawnVanilla(State(type), out var result, start);
            int actual = created ? result.Handle.Slot : -1;
            Assert.True(expected == actual, $"mode={mode}, type={type}, start={start}, expected={expected}, actual={actual}");
        }
    }

    [Theory]
    [InlineData(-1)] [InlineData(200)] [InlineData(255)] [InlineData(int.MaxValue)]
    public void Invalid_start_is_bounded_without_mutation(int start)
    {
        var store = new RuntimeNpcStore();
        Assert.False(store.TrySpawnVanilla(State(222), out _, start));
        Assert.Equal(0, store.ActiveCount);
        var small = new RuntimeNpcStore(4);
        Assert.False(small.TrySpawnVanilla(State(1), out _, 4));
    }

    [Theory]
    [InlineData(35, 2)] [InlineData(127, 4)]
    public void Skeleton_limbs_start_search_at_parent_even_when_lower_slots_are_free(int type, int count)
    {
        var store = new RuntimeNpcStore(20);
        var state = State(type) with { Ai = default };
        Assert.True(store.TrySpawn(10, in state, out var parent));
        var stepper = new VanillaNpcTargetingAiStepper(new Idle());
        var proposed = state with { Ai = new NpcAiState(1, 0, 0, 0) };
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[4];
        Assert.Equal(count, stepper.PlanNpcSpawns(in parent, in proposed, intents));
        for (int index = 0; index < count; index++)
        {
            Assert.Equal(parent.Handle.Slot, intents[index].StartSlot);
            Assert.True(store.TrySpawnIntent(in intents[index], out var child));
            Assert.Equal(11 + index, child.Handle.Slot);
        }
        Assert.False(store.TryGetActive(0, out _));
    }

    [Fact]
    public void Wall_of_Flesh_children_start_search_at_the_parent_slot()
    {
        var store = new RuntimeNpcStore(32);
        var parentState = State(VanillaNpcIds.WallOfFlesh.Value) with { Ai = default };
        Assert.True(store.TrySpawn(10, in parentState, out var parent));
        var stepper = new VanillaNpcTargetingAiStepper(new Idle());
        stepper.SetWallOfFleshEnvironment(new WallEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1400f, 35_800f, 0, true, false, false, false)]);
        stepper.SetNpcPeers([parent]);
        Assert.True(stepper.TryStepState(in parent, out var proposed));

        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[13];
        Assert.Equal(13, stepper.PlanNpcSpawns(in parent, in proposed, intents));
        for (int index = 0; index < intents.Length; index++)
        {
            Assert.Equal(parent.Handle.Slot, intents[index].StartSlot);
            Assert.True(store.TrySpawnIntent(in intents[index], out var child));
            Assert.Equal(11 + index, child.Handle.Slot);
        }
        Assert.False(store.TryGetActive(0, out _));
    }

    [Fact]
    public void Plantera_hooks_and_free_Golem_head_start_search_at_the_parent_slot()
    {
        var store = new RuntimeNpcStore(32);
        var stepper = new VanillaNpcTargetingAiStepper(new Idle());
        Assert.True(store.TrySpawn(10, State(VanillaNpcIds.Plantera.Value), out var plantera));
        var planteraUpdate = State(VanillaNpcIds.Plantera.Value) with
        {
            Simulation = NpcSimulationState.Initial with { LocalAi = new NpcAiState(1f, 0f, 0f, 0f) }
        };
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[3];
        Assert.Equal(3, stepper.PlanNpcSpawns(in plantera, in planteraUpdate, intents));
        for (int index = 0; index < intents.Length; index++)
        {
            Assert.Equal(plantera.Handle.Slot, intents[index].StartSlot);
            Assert.True(store.TrySpawnIntent(in intents[index], out var hook));
            Assert.Equal(11 + index, hook.Handle.Slot);
        }

        var golemStore = new RuntimeNpcStore(32);
        Assert.True(golemStore.TrySpawn(10, State(VanillaNpcIds.Golem.Value), out var golem));
        var golemUpdate = State(VanillaNpcIds.Golem.Value) with
        {
            Simulation = NpcSimulationState.Initial with { LocalAi = new NpcAiState(0f, 0f, 1f, 0f) }
        };
        Span<NpcAiSpawnIntent> freeHead = stackalloc NpcAiSpawnIntent[1];
        Assert.Equal(1, stepper.PlanNpcSpawns(in golem, in golemUpdate, freeHead));
        Assert.Equal(golem.Handle.Slot, freeHead[0].StartSlot);
        Assert.True(golemStore.TrySpawnIntent(in freeHead[0], out var head));
        Assert.Equal(11, head.Handle.Slot);
    }

    [Fact]
    public void Moon_Lord_shell_parts_start_search_at_the_core_slot()
    {
        var store = new RuntimeNpcStore(32);
        var coreState = State(VanillaNpcIds.MoonLordCore.Value) with { Ai = new NpcAiState(-1f, 59f, 0f, 0f) };
        Assert.True(store.TrySpawn(10, in coreState, out var core));
        var stepper = new VanillaNpcTargetingAiStepper(new Idle());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 500f, 300f, 0, true, false, false, false)]);
        var executor = new RuntimeNpcAiStateExecutor(store);
        executor.Tick(stepper);

        Assert.True(store.TryGet(core.Handle, out core));
        float[] slots = [core.Simulation.LocalAi.Ai0, core.Simulation.LocalAi.Ai1, core.Simulation.LocalAi.Ai2];
        Assert.Equal([11f, 12f, 13f], slots);
        Assert.False(store.TryGetActive(0, out _));
    }

    [Fact]
    public void Lunatic_Cultist_ritual_clones_start_search_at_the_parent_slot()
    {
        var store = new RuntimeNpcStore(32);
        var state = State(VanillaNpcIds.LunaticCultist.Value) with { Ai = new NpcAiState(5f, 29f, 0f, 0f) };
        Assert.True(store.TrySpawn(10, in state, out var cultist));
        var proposed = state with { Ai = new NpcAiState(5f, 30f, 0f, 0f) };
        var stepper = new VanillaNpcTargetingAiStepper(new Idle());
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[2];
        Assert.Equal(2, stepper.PlanNpcSpawns(in cultist, in proposed, intents));
        for (int index = 0; index < intents.Length; index++)
        {
            Assert.Equal(cultist.Handle.Slot, intents[index].StartSlot);
            Assert.True(store.TrySpawnIntent(in intents[index], out var clone));
            Assert.Equal(11 + index, clone.Handle.Slot);
        }
        Assert.False(store.TryGetActive(0, out _));
    }

    [Fact]
    public void Repeated_creation_and_deactivation_match_original_NewNPC_slots_and_generations()
    {
        var store = new RuntimeNpcStore();
        foreach (var row in Reference.GetProperty("creations").EnumerateArray())
        {
            if (row.GetProperty("step").GetInt32() > 0) store.UpdateProtectedSpawnSlots();
            Assert.True(store.TrySpawnVanilla(State(401), out var created));
            Assert.Equal(row.GetProperty("slot").GetInt32(), created.Handle.Slot);
            Assert.Equal(row.GetProperty("generation").GetUInt64(), created.Handle.Generation.Value);
            Assert.True(store.TryDespawn(created.Handle));
        }
    }

    [Fact]
    public void Replacement_advances_identity_without_a_kill_or_extra_active_entity()
    {
        var commits = new Commits();
        var store = new RuntimeNpcStore(1, commits);
        Assert.True(store.TrySpawnVanilla(State(1, replaceable: true), out var old));
        Assert.True(store.TrySpawnVanilla(State(401), out var current));
        Assert.Equal(1, store.ActiveCount);
        Assert.Equal(old.Handle.Slot, current.Handle.Slot);
        Assert.NotEqual(old.Handle.Generation, current.Handle.Generation);
        Assert.False(store.TryDespawn(old.Handle));
        Assert.False(store.TryUpdate(old.Handle, State(1), out _));
        Assert.Equal(new[] { NpcStateCommitKind.Spawn, NpcStateCommitKind.Spawn }, commits.Kinds);
        Assert.False(current.Simulation.CanBeReplacedByOtherNpcs);
    }

    [Fact]
    public void World_ticks_release_protection_and_ai_passes_alone_do_not()
    {
        var store = new RuntimeNpcStore(1);
        Assert.True(store.TrySpawnVanilla(State(1), out var old));
        Assert.True(store.TryDespawn(old.Handle));
        var ai = new Idle();
        var executor = new RuntimeNpcAiStateExecutor(store);
        executor.Tick(ai); executor.Tick(ai);
        Assert.False(store.TrySpawnVanilla(State(1), out _));
        var runtime = new ServerRuntimeState(npcs: store, npcAiStepper: ai);
        runtime.Tick();
        Assert.False(store.TrySpawnVanilla(State(1), out _));
        runtime.Tick();
        Assert.True(store.TrySpawnVanilla(State(1), out _));
    }

    private static RuntimeNpcStore CreateArrangement(int mode)
    {
        var store = new RuntimeNpcStore();
        if (mode == 2)
        {
            foreach (byte slot in new byte[] { 0, 199 }) Assert.True(store.TrySpawn(slot, State(1), out _));
            store.UpdateProtectedSpawnSlots();
            foreach (byte slot in new byte[] { 0, 199 })
            { Assert.True(store.TryGetActive(slot, out var npc)); Assert.True(store.TryDespawn(npc.Handle)); }
            store.UpdateProtectedSpawnSlots();
        }
        for (byte slot = 0; slot < 200; slot++)
        {
            bool active = mode is 1 or 3 or 4 or 5 or 8 || mode == 7 && slot != 0 || mode == 6 && slot != 100 ||
                mode == 2 && slot is not (0 or 2 or 197 or 199);
            if (!active) continue;
            var occupied = State(1, mode == 8 && slot == 0 || mode is 3 or 5 or 6 && slot is 4 or 195);
            Assert.True(store.TrySpawn(slot, in occupied, out _));
        }
        if (mode is 4 or 5)
        {
            store.UpdateProtectedSpawnSlots();
            for (byte slot = 0; slot < 200; slot++)
            { Assert.True(store.TryGetActive(slot, out var npc)); Assert.True(store.TryDespawn(npc.Handle)); }
        }
        return store;
    }

    private static NpcStateUpdate State(int type, bool replaceable = false) =>
        new(type, (short)type, 100, 100, 0, 0, 255, default,
            NpcSimulationState.Initial with { CanBeReplacedByOtherNpcs = replaceable });

    private static JsonElement ReadReference(string resourceName, string hash)
    {
        using var resource = typeof(NpcSlotAllocationTests).Assembly.GetManifestResourceStream(resourceName)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash,
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray()); return json.RootElement.Clone();
    }

    private sealed class Commits : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Kinds { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Kinds.Add(kind);
    }

    private sealed class Idle : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class WallEnvironment : IVanillaWallOfFleshEnvironment
    {
        public int WorldWidthTiles => 8400;
        public int WorldHeightTiles => 2400;
        public int UnderworldLayerTiles => 2200;

        public bool TryResolveCorridor(float positionX, float positionY, int width, int height, out float topPixels, out float bottomPixels)
        {
            topPixels = 35_400f;
            bottomPixels = 36_200f;
            return true;
        }

        public bool CanHit(float sourceX, float sourceY, int sourceWidth, int sourceHeight, float targetX, float targetY, int targetWidth, int targetHeight) => true;
        public bool TryFindGroundSpawn(int tileX, int startTileY, out int bottomX, out int bottomY) { bottomX = 0; bottomY = 0; return false; }
        public bool TryFindTeleportSpot(int targetTileX, int targetTileY, int npcWidth, int npcHeight, out int tileX, out int tileY) { tileX = 0; tileY = 0; return false; }
    }
}
