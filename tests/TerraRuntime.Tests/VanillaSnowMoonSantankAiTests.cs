using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaSnowMoonSantankAiTests
{
    [Fact]
    public void Type_345_defaults_coverage_and_ai060_pursuit_match_source()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(345), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(345), out VanillaNpcAiCoverage coverage));
        Assert.Equal((60, 130, 140, 120, 38, 34000), (definition.AiStyle.Value, definition.BaseWidth, definition.BaseHeight,
            definition.Damage, definition.Defense, definition.LifeMax));
        Assert.Equal(VanillaNpcBehaviorFamily.SnowMoonSantank, definition.BehaviorFamily);
        Assert.True(definition.NoGravityAtSpawn); Assert.True(definition.NoTileCollideAtSpawn);
        Assert.True(coverage.Has(VanillaNpcAiCapability.MoonEventProjectileSlice));

        VanillaNpcTargetingAiStepper stepper = CreateStepper(new SequenceRandom(), solid: false);
        NpcSnapshot npc = CreateNpc(default);
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(new NpcAiState(0f, 1f, 1f, 1f), next.Ai);
        Assert.Equal(.45f, next.VelocityX, 5); Assert.Equal(-.2f, next.VelocityY, 5);
        Assert.Equal(.0225f, next.Simulation.Rotation!.Value, 5);
        Assert.True(next.Simulation.NoGravity); Assert.True(next.Simulation.NoTileCollide);
    }

    [Fact]
    public void Ai060_emits_rocket_and_bomb_only_after_the_committed_source_clock_transition()
    {
        var rocketNpcs = new RuntimeNpcStore();
        Assert.True(rocketNpcs.TrySpawn(1, Update(new NpcAiState(0f, 0f, 1f, -1f)), out NpcSnapshot rocketSource));
        var rocketProjectiles = new RuntimeProjectileStore();
        var rocketRandom = new SequenceRandom();
        VanillaNpcTargetingAiStepper rocketStepper = CreateStepper(rocketRandom, solid: false);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(rocketNpcs, rocketProjectiles).Tick(new SantankOnly(rocketStepper)).Applied);
        Assert.True(rocketProjectiles.TryGetActive(0, out ProjectileSnapshot rocket));
        Assert.Equal(VanillaProjectileIds.SantankRocket, rocket.Type); Assert.Equal((short)42, rocket.Damage);
        Assert.True(rocketProjectiles.TryGetServerNpcSource(rocket.Handle, out NpcHandle rocketProvenance)); Assert.Equal(rocketSource.Handle, rocketProvenance);

        var bombNpcs = new RuntimeNpcStore();
        Assert.True(bombNpcs.TrySpawn(1, Update(new NpcAiState(1f, 0f, 0f, 17f)) with { VelocityX = 4f, VelocityY = 1f }, out _));
        var bombProjectiles = new RuntimeProjectileStore();
        var bombRandom = new SequenceRandom();
        VanillaNpcTargetingAiStepper bombStepper = CreateStepper(bombRandom, solid: false);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(bombNpcs, bombProjectiles).Tick(new SantankOnly(bombStepper)).Applied);
        Assert.True(bombProjectiles.TryGetActive(0, out ProjectileSnapshot bomb));
        Assert.Equal(VanillaProjectileIds.SantankBomb, bomb.Type); Assert.Equal((short)37, bomb.Damage);
        Assert.Equal(1.025f, bomb.VelocityX, 5); Assert.Equal(3.9f, bomb.VelocityY, 5);
        Assert.Equal(2, bombRandom.Draws);
    }

    [Fact]
    public void Ai060_bomb_checks_solid_tile_and_barrage_uses_the_committed_random_vector()
    {
        var solidNpcs = new RuntimeNpcStore();
        Assert.True(solidNpcs.TrySpawn(1, Update(new NpcAiState(1f, 0f, 0f, 17f)), out _));
        var solidProjectiles = new RuntimeProjectileStore();
        VanillaNpcTargetingAiStepper solidStepper = CreateStepper(new SequenceRandom(), solid: true);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(solidNpcs, solidProjectiles).Tick(new SantankOnly(solidStepper)).Applied);
        Assert.Equal(0, solidProjectiles.ActiveCount);

        var barrageNpcs = new RuntimeNpcStore();
        Assert.True(barrageNpcs.TrySpawn(1, Update(new NpcAiState(2f, 0f, 0f, 7f)), out _));
        var barrageProjectiles = new RuntimeProjectileStore();
        VanillaNpcTargetingAiStepper barrageStepper = CreateStepper(new SequenceRandom(), solid: false);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(barrageNpcs, barrageProjectiles).Tick(new SantankOnly(barrageStepper)).Applied);
        Assert.True(barrageProjectiles.TryGetActive(0, out ProjectileSnapshot barrage));
        Assert.Equal(VanillaProjectileIds.SantankBomb, barrage.Type); Assert.Equal((short)35, barrage.Damage);
        Assert.True(barrage.VelocityX < 0f); Assert.True(barrage.VelocityY < 0f);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaNpcRandom random, bool solid)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(100d); stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetProjectileEnvironment(new Environment(solid));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 300f, 300f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc(NpcAiState ai) => new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
        345, 345, 100f, 0f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget, ai,
        NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 34000, LifeMax = 34000, TimeLeft = 750 });

    private static NpcStateUpdate Update(NpcAiState ai) => new(345, 345, 100f, 0f, 0f, 0f, 3, ai,
        NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 34000, LifeMax = 34000, TimeLeft = 750 });

    private sealed class Environment(bool solid) : IVanillaNpcProjectileEnvironment, IVanillaNpcSolidTileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
        public bool IsSolidTile(int tileX, int tileY) => solid;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class SantankOnly(VanillaNpcTargetingAiStepper stepper) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) => stepper.TryStepState(in npc, out next);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class SequenceRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Draws++; return inclusiveMin; }
        public double NextDouble() { Draws++; return 0d; }
    }
}
