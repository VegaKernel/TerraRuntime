using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventEverscreamAiTests
{
    [Fact]
    public void Type_344_defaults_coverage_and_hover_state_match_source()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(344), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(344), out VanillaNpcAiCoverage coverage));
        Assert.Equal(new NpcAiStyleId(57), definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.MoonEventEverscream, definition.BehaviorFamily);
        Assert.Equal((172, 130, 110, 38, 13000), (definition.BaseWidth, definition.BaseHeight, definition.Damage, definition.Defense, definition.LifeMax));
        Assert.True(definition.NoGravityAtSpawn); Assert.True(definition.NoTileCollideAtSpawn);
        Assert.True(coverage.Has(VanillaNpcAiCapability.MoonEventEverscreamSlice));
        Assert.True(coverage.Has(VanillaNpcAiCapability.MoonEventProjectileSlice));

        VanillaNpcTargetingAiStepper stepper = CreateStepper(new SequenceRandom(), solid: true);
        NpcSnapshot npc = CreateNpc();
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(-.025f, next.VelocityY, 5);
        Assert.Equal(2f / 21f, next.VelocityX, 5);
        Assert.True(next.Simulation.NoGravity); Assert.True(next.Simulation.NoTileCollide);

        VanillaNpcTargetingAiStepper openSky = CreateStepper(new SequenceRandom(), solid: false);
        NpcSnapshot rising = CreateNpc() with { VelocityY = -2f };
        Assert.True(openSky.TryStepState(in rising, out NpcStateUpdate falling));
        Assert.Equal(.025f, falling.VelocityY, 5);
        NpcSnapshot descending = CreateNpc() with { VelocityY = .2f };
        Assert.True(openSky.TryStepState(in descending, out NpcStateUpdate fasterFall));
        Assert.Equal(.7f, fasterFall.VelocityY, 5);
    }

    [Fact]
    public void Pine_needles_and_ornaments_spawn_only_after_the_exact_committed_attack_tick()
    {
        AssertProjectile(new NpcAiState(1f, 4f, 0f, 0f), VanillaProjectileIds.EverscreamPineNeedle, 43, expectedDraws: 11);
        AssertProjectile(new NpcAiState(2f, 74f, 0f, 0f), VanillaProjectileIds.EverscreamOrnament, 57, expectedDraws: 7);
    }

    [Fact]
    public void Rejected_attack_transition_does_not_consume_rng_or_create_a_projectile()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(1, Update(new NpcAiState(1f, 4f, 0f, 0f)), out _));
        var projectiles = new RuntimeProjectileStore();
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, solid: false);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ReplacingEverscream(stepper, npcs)).Rejected);
        Assert.Equal(0, projectiles.ActiveCount); Assert.Equal(0, random.Draws);
    }

    private static void AssertProjectile(NpcAiState ai, ProjectileTypeId expectedType, int expectedDamage, int expectedDraws)
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(1, Update(ai), out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore();
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, solid: false);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EverscreamOnly(stepper)).Applied);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(expectedType, projectile.Type); Assert.Equal((short)expectedDamage, projectile.Damage);
        Assert.True(projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle provenance)); Assert.Equal(source.Handle, provenance);
        Assert.Equal(expectedDraws, random.Draws);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaNpcRandom random, bool solid)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetEverscreamEnvironment(new Environment(solid));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 300f, 115f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc() => new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
        344, 344, 100f, 200f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget, default,
        NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 13000, LifeMax = 13000, TimeLeft = 750 });

    private static NpcStateUpdate Update(NpcAiState ai) => new(344, 344, 100f, 200f, 0f, 0f, 3, ai,
        NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 13000, LifeMax = 13000, TimeLeft = 750 });

    private sealed class Environment(bool solid) : IVanillaEverscreamEnvironment
    {
        public bool SolidCollision(float positionX, float positionY, int width, int height) => solid;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private class EverscreamOnly(VanillaNpcTargetingAiStepper stepper) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public virtual bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) => stepper.TryStepState(in npc, out next);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class ReplacingEverscream(VanillaNpcTargetingAiStepper stepper, RuntimeNpcStore store) : EverscreamOnly(stepper)
    {
        public override bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (!base.TryStepState(in npc, out next)) return false;
            NpcStateUpdate replacement = next with { PositionX = next.PositionX + 1f };
            Assert.True(store.TryUpdate(npc.Handle, in replacement, out _));
            return true;
        }
    }

    private sealed class SequenceRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Draws++; return inclusiveMin; }
        public double NextDouble() { Draws++; return 0d; }
    }
}
