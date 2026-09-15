using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkeletronPhaseTests
{
    private static readonly JsonElement[] Phases = Read("SkeletronPhase1458", "26144c9b8a19d5d0d004b0ebcd786678d714084ebb175dd860e0aafc3673272c");
    public static TheoryData<int> PhaseCases => new(Enumerable.Range(0, Phases.Length));

    [Theory]
    [MemberData(nameof(PhaseCases))]
    public void Head_phase_restores_spawn_baselines_and_matches_original_motion(int index)
    {
        var row = Phases[index];
        int count = row.GetProperty("players").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        var store = new RuntimeNpcStore();
        float difficulty = row.GetProperty("difficulty").GetSingle();
        store.SetVanillaSpawnContextSource(() => new(difficulty, count, good));
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
        {
            StartSlot = 10,
            InitialAi = new NpcAiState(1, row.GetProperty("phase").GetInt32(), row.GetProperty("timer").GetInt32(), 0)
        };
        Assert.True(store.TrySpawnIntent(in intent, out var head));
        for (int hand = 0; hand < row.GetProperty("hands").GetInt32(); hand++)
            Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
                { StartSlot = 11, InitialAi = new NpcAiState(-1, 10, 0, 0) }, out _));
        var oldLiveState = new NpcStateUpdate(head.Type, head.NetId, head.PositionX, head.PositionY, 0, 0, 0, head.Ai,
            head.Simulation with { DamageOverride = 777, DefenseOverride = 888 });
        Assert.True(store.TryUpdate(head.Handle, in oldLiveState, out _));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(false, false, goodWorld: good, expertMode: difficulty >= 2f, masterMode: difficulty >= 3f);
        var target = new VanillaNpcTargetCandidate(0, row.GetProperty("targetX").GetSingle(), row.GetProperty("targetY").GetSingle(), 0, true, false, false, false);
        ai.SetCandidates(count == 1 ? [target] : [target, new VanillaNpcTargetCandidate(1, 10, 21, 0, true, false, false, false)]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(new HeadOnly(ai)).Applied);
        Assert.True(store.TryGet(head.Handle, out var after));
        var expectedAi = row.GetProperty("ai");
        Assert.Equal(new NpcAiState(expectedAi[0].GetSingle(), expectedAi[1].GetSingle(), expectedAi[2].GetSingle(), expectedAi[3].GetSingle()), after.Ai);
        Assert.Equal(row.GetProperty("x").GetSingle(), after.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), after.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), after.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), after.VelocityY);
        Assert.Equal(row.GetProperty("damage").GetInt32(), after.Simulation.DamageOverride);
        Assert.Equal(row.GetProperty("defense").GetInt32(), after.Simulation.DefenseOverride);
        Assert.Equal(row.GetProperty("defDamage").GetInt32(), after.Simulation.BaseDamage);
        Assert.Equal(row.GetProperty("defDefense").GetInt32(), after.Simulation.BaseDefense);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), after.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("timeLeft").GetInt32(), after.Simulation.TimeLeft);
        Assert.Equal(row.GetProperty("reflects").GetBoolean(), after.Simulation.ReflectsProjectiles);
    }

    private static JsonElement[] Read(string name, string hash)
    {
        using var resource = typeof(SkeletronPhaseTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    [Fact]
    public void Spin_damage_retains_fractional_spawn_difficulty_after_context_changes()
    {
        var store = new RuntimeNpcStore();
        var context = new VanillaNpcSpawnContext(1.5f, 1, false);
        store.SetVanillaSpawnContextSource(() => context);
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { InitialAi = new NpcAiState(1, 1, 399, 0) }, out var head));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        context = new(4, 8, true);
        ai.SetWorldConditions(false, false, expertMode: true, masterMode: true, goodWorld: true);
        var executor = new RuntimeNpcAiStateExecutor(store);
        Assert.Equal(1, executor.Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var spin));
        Assert.Equal(1.5f, spin.Simulation.SpawnDifficulty);
        Assert.Equal(50, spin.Simulation.BaseDamage);
        Assert.Equal(57, spin.Simulation.DamageOverride);
        Assert.Equal(0f, spin.Ai.Ai1);
        Assert.Equal(1, executor.Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var hover));
        Assert.Equal(50, hover.Simulation.DamageOverride);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    [InlineData(0.49f)] [InlineData(4.01f)]
    public void Invalid_spawn_difficulty_cannot_allocate_or_update(float difficulty)
    {
        var store = new RuntimeNpcStore();
        var state = new NpcStateUpdate(35, 35, 1000, 1000, 0, 0, 0, default,
            NpcSimulationState.Initial with { SpawnDifficulty = difficulty });
        Assert.False(store.TrySpawn(10, in state, out _));
        Assert.False(store.TrySpawnVanilla(in state, out _));
        Assert.Equal(0, store.ActiveCount);
        var valid = state with { Simulation = NpcSimulationState.Initial with { SpawnDifficulty = 1.5f } };
        Assert.True(store.TrySpawn(10, in valid, out var head));
        Assert.False(store.TryUpdate(head.Handle, in state, out _));
        Assert.True(store.TryGet(head.Handle, out var current));
        Assert.Equal(head, current);
    }

    [Fact]
    public void Difficulty_capture_is_once_per_spawn_and_is_preserved_or_reset_with_ownership()
    {
        var store = new RuntimeNpcStore();
        int samples = 0;
        store.SetVanillaSpawnContextSource(() => new(++samples == 1 ? 1.5f : 3f, 1, false));
        // Type 1 has no implemented scaling profile yet, but still retains the sampled NPC.difficulty.
        var state = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in state, out var npc));
        Assert.Equal(1, samples);
        Assert.Equal(1.5f, npc.Simulation.SpawnDifficulty);
        Assert.True(store.TryUpdate(npc.Handle, in state, out var retained));
        Assert.Equal(1.5f, retained.Simulation.SpawnDifficulty);
        state = state with { Type = 35, NetId = 35, Simulation = retained.Simulation };
        Assert.True(store.TryUpdate(npc.Handle, in state, out var changed));
        Assert.Equal(1f, changed.Simulation.SpawnDifficulty);
        Assert.True(store.TryDespawn(npc.Handle));
        state = state with { Simulation = NpcSimulationState.Initial };
        Assert.True(store.TrySpawn(npc.Handle.Slot, in state, out var fresh));
        Assert.NotEqual(npc.Handle.Generation, fresh.Handle.Generation);
        Assert.Equal(1f, fresh.Simulation.SpawnDifficulty);
        Assert.Equal(1, samples);
    }

    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.TypeIdentity == VanillaNpcIds.SkeletronHead && inner.TryStepState(in npc, out next);
        }
    }
}
