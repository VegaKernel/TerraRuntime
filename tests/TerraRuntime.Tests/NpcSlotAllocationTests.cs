using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class NpcSlotAllocationTests
{
    private static readonly JsonElement Reference = ReadReference();

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void All_original_npc_types_match_allocation_for_the_supplied_slot_state(int mode)
    {
        foreach (var row in Reference.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("mode").GetInt32() != mode) continue;
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

    private static NpcStateUpdate State(int type, bool replaceable = false) =>
        new(type, (short)type, 100, 100, 0, 0, 255, default,
            NpcSimulationState.Initial with { CanBeReplacedByOtherNpcs = replaceable });

    private static JsonElement ReadReference()
    {
        using var resource = typeof(NpcSlotAllocationTests).Assembly.GetManifestResourceStream("NpcSlotAllocation1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("3066507d07e01edb34f8812ffede7c9671e135d930783efca982496268b60bd6",
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
}
