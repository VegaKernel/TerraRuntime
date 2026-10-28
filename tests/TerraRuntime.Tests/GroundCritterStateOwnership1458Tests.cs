using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundCritterStateOwnership1458Tests
{
    [Fact]
    public void Admitted_roster_matches_the_exact_source_identities_without_granting_full_ai_parity()
    {
        // Original initialized SetDefaults/FindFrame captures: old twelve bunnies
        // plus exactly22 identities in the new eight-family component corpus.
        int[] expected = [46,148,149,299,300,303,337,361,366,367,443,445,447,538,539,540,
            610,616,617,625,639,640,641,642,643,644,645,646,647,648,649,650,651,652];
        Assert.Equal(expected, VanillaNonresidentGroundCritterCatalog1458.Types.ToArray());
        for (int type = 0; type <= 696; type++)
            Assert.Equal(expected.Contains(type), VanillaNonresidentGroundCritterCatalog1458.IsSupported(type));
        foreach (int type in expected)
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out var definition));
            Assert.Equal(7, definition.AiStyle.Value);
            Assert.True(definition.SupportsRuntimeLifecycle);
            Assert.Equal(VanillaNpcPhysicsFamily.GenericGround, definition.PhysicsFamily);
            Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new(type), out var coverage));
            Assert.False(coverage.FullVanillaAiParity);
        }
    }

    [Fact]
    public void Fresh_squirrel_uses_definition_style_without_inventing_a_retained_override()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(0, Create(299), out var birth));
        Assert.Null(birth.Simulation.AiStyleOverride);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(299), out var definition));
        Assert.Equal(7, definition.AiStyle.Value);
        Assert.Equal(7, (birth.Simulation.AiStyleOverride ?? definition.AiStyle).Value);
        Assert.False(VanillaNonresidentGroundCritterCatalog1458.TryGetEffectiveFighterDefinition(
            birth.Type, birth.Simulation, definition, out _));
    }

    [Fact]
    public void Same_identity_retains_style_three_when_sparse_updates_and_stuck_counter_omit_it()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(0, Create(299), out var birth));
        var turned = Create(299) with
        {
            Ai = new(1, 0, 0, 2),
            Simulation = birth.Simulation with { AiStyleOverride = new(3), Life = 25, LifeMax = 25,
                DefenseOverride = 6, DamageOverride = 20 }
        };
        Assert.True(npcs.TryUpdate(birth.Handle, in turned, out var hostile));
        var sparse = Create(299) with { Ai = new(1, 0, 0, 0), PositionX = 700 };
        Assert.True(npcs.TryUpdate(hostile.Handle, in sparse, out var retained));
        Assert.Equal(hostile.Handle, retained.Handle);
        Assert.Equal(3, retained.Simulation.AiStyleOverride?.Value);
        Assert.Equal(0, retained.Ai.Ai3);
        Assert.Equal(25, retained.Simulation.LifeMax);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(299), out var definition));
        Assert.True(VanillaNonresidentGroundCritterCatalog1458.TryGetEffectiveFighterDefinition(
            retained.Type, retained.Simulation, definition, out var effective));
        Assert.Equal(3, effective.AiStyle.Value);
        Assert.Equal(definition.Type, effective.Type);
        Assert.Equal(7, definition.AiStyle.Value);
    }

    [Fact]
    public void Identity_transform_clears_inherited_style_before_validation_and_slot_reuse_starts_fresh()
    {
        var npcs = new RuntimeNpcStore();
        var initial = Create(299) with { Simulation = NpcSimulationState.Initial with { AiStyleOverride = new(3) } };
        Assert.True(npcs.TrySpawn(0, in initial, out var hostile));
        // The trusted transform copies the previous state, including its now obsolete style.
        var transform = Create(46) with { Simulation = hostile.Simulation };
        Assert.True(npcs.TryUpdate(hostile.Handle, in transform, out var bunny));
        Assert.Equal(hostile.Handle, bunny.Handle);
        Assert.Null(bunny.Simulation.AiStyleOverride);
        Assert.True(npcs.TryDespawn(bunny.Handle));
        Assert.True(npcs.TrySpawn(0, Create(299), out var replacement));
        Assert.NotEqual(hostile.Handle, replacement.Handle);
        Assert.Null(replacement.Simulation.AiStyleOverride);
        Assert.False(npcs.TryUpdate(hostile.Handle, in initial, out _));
        Assert.True(npcs.TryGet(replacement.Handle, out var current));
        Assert.Equal(replacement, current);
    }

    [Fact]
    public void Unsupported_styles_and_forged_transform_styles_refuse_without_publication_or_random_draws()
    {
        var sink = new Commits();
        var random = new CountingRandom();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        npcs.SetVanillaSpawnRandomSource(random);
        Assert.True(npcs.TrySpawn(0, Create(299), out var birth));
        sink.Values.Clear();
        var invalidSquirrel = Create(299) with { Simulation = birth.Simulation with { AiStyleOverride = new(999) } };
        var invalidTransform = invalidSquirrel with { Type = 46, NetId = 46 };
        var invalidBunny = Create(46) with { Simulation = NpcSimulationState.Initial with { AiStyleOverride = new(3) } };
        Assert.False(npcs.TryUpdate(birth.Handle, in invalidSquirrel, out _));
        Assert.False(npcs.TryUpdate(birth.Handle, in invalidTransform, out _));
        Assert.False(npcs.TrySpawn(1, in invalidBunny, out _));
        Assert.False(npcs.TrySpawn(1, in invalidSquirrel, out _));
        Assert.True(npcs.TryGet(birth.Handle, out var unchanged));
        Assert.Equal(birth, unchanged);
        Assert.Equal(1, npcs.ActiveCount);
        Assert.Empty(sink.Values);
        Assert.Equal(0, random.Calls);
    }

    private static NpcStateUpdate Create(int type) => new(type, checked((short)type), 639, 1580,
        0, 0, 0, default, NpcSimulationState.Initial);

    private sealed class Commits : INpcStateCommitSink
    {
        public List<NpcSnapshot> Values { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Values.Add(snapshot);
    }

    private sealed class CountingRandom : IVanillaNpcRandom
    {
        public int Calls { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Calls++; return inclusiveMin; }
    }
}
