using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

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
    public void Type_325_defaults_and_ai57_state_choice_match_source()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(325), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(325), out VanillaNpcAiCoverage coverage));
        Assert.Equal(new NpcAiStyleId(57), definition.AiStyle);
        Assert.Equal((164, 154, 120, 34, 14000), (definition.BaseWidth, definition.BaseHeight, definition.Damage, definition.Defense, definition.LifeMax));
        Assert.True(definition.NoGravityAtSpawn); Assert.True(definition.NoTileCollideAtSpawn);
        Assert.True(coverage.Has(VanillaNpcAiCapability.MoonEventEverscreamSlice));

        VanillaNpcTargetingAiStepper stepper = CreateStepper(new SequenceRandom(), solid: false);
        var update = new NpcStateUpdate(325, 325, 100f, 200f, 0f, 0f, 3, new NpcAiState(0f, 298f, 0f, 0f),
            NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 3000, LifeMax = 14000, TimeLeft = 750 });
        var npcs = new RuntimeNpcStore(); Assert.True(npcs.TrySpawn(1, update, out NpcSnapshot npc));
        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Assert.Equal(3f, next.Ai.Ai0); Assert.Equal(0f, next.Ai.Ai1);
    }

    [Fact]
    public void Pine_needles_and_ornaments_spawn_only_after_the_exact_committed_attack_tick()
    {
        AssertProjectile(new NpcAiState(1f, 4f, 0f, 0f), VanillaProjectileIds.EverscreamPineNeedle, 43, expectedDraws: 11);
        AssertProjectile(new NpcAiState(2f, 74f, 0f, 0f), VanillaProjectileIds.EverscreamOrnament, 57, expectedDraws: 7);
    }

    [Fact]
    public void Mourning_wood_projectile_branches_and_source_rng_order_match_ai57()
    {
        AssertPumpkingProjectile(new NpcAiState(1f, 14f, 0f, 0f), VanillaProjectileIds.FlamingWood, 50, expectedDraws: 2);
        AssertPumpkingProjectile(new NpcAiState(2f, 71f, 0f, 0f), VanillaProjectileIds.GreekFire1, 40, expectedDraws: 5);
        AssertPumpkingProjectile(new NpcAiState(3f, 29f, 0f, 0f), VanillaProjectileIds.FlamingWood, 75, expectedDraws: 2);
        AssertPumpkingProjectile(new NpcAiState(4f, 9f, 0f, 0f), VanillaProjectileIds.GreekFire1, 50, expectedDraws: 5);
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.FlamingWood, out VanillaProjectileDefinition scythe));
        Assert.Equal((14, 14), (scythe.Width, scythe.Height));
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.GreekFire3, out VanillaProjectileDefinition attack328));
        Assert.Equal((6, 12), (attack328.Width, attack328.Height));
    }

    [Fact]
    public void Ai57_sustained_attack_halts_horizontal_pursuit_before_common_hover_tail()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(new SequenceRandom(), solid: false);
        NpcSnapshot pumpking = CreatePumpking(new NpcAiState(1f, 0f, 0f, 0f)) with { VelocityX = 3f };
        Assert.True(stepper.TryStepState(in pumpking, out NpcStateUpdate next));
        Assert.Equal(2.7f, next.VelocityX, 5);
    }

    [Fact]
    public void Pumpking_and_blades_keep_source_defaults_initial_links_and_parent_motion()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(327), out VanillaNpcDefinition pumpking));
        Assert.Equal((58, 100, 100, 50, 40, 26000), (pumpking.AiStyle.Value, pumpking.BaseWidth, pumpking.BaseHeight, pumpking.Damage, pumpking.Defense, pumpking.LifeMax));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(328), out VanillaNpcDefinition blade));
        Assert.Equal((59, 80, 80, 65, 14, 5000), (blade.AiStyle.Value, blade.BaseWidth, blade.BaseHeight, blade.Damage, blade.Defense, blade.LifeMax));
        Assert.True(blade.DontTakeDamageAtSpawn);

        VanillaNpcTargetingAiStepper stepper = CreateStepper(new SequenceRandom(), solid: false);
        var state = new NpcStateUpdate(327, 327, 100f, 200f, 0f, 0f, 3, default,
            NpcSimulationState.Initial with { Life = 26000, LifeMax = 26000 });
        var npcs = new RuntimeNpcStore(); Assert.True(npcs.TrySpawn(7, state, out NpcSnapshot source));
        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next)); Assert.Equal(1f, next.Ai.Ai0);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[2];
        Assert.Equal(2, stepper.PlanNpcSpawns(in source, in next, intents));
        Assert.Equal(new NpcAiState(-1f, 7f, 0f, 0f), intents[0].InitialAi);
        Assert.Equal(new NpcAiState(1f, 7f, 0f, 150f), intents[1].InitialAi);

        var bladeState = new NpcStateUpdate(328, 328, 400f, 400f, 0f, 0f, 3, intents[0].InitialAi,
            NpcSimulationState.Initial with { Life = 5000, LifeMax = 5000, DontTakeDamage = true });
        Assert.True(npcs.TrySpawn(8, bladeState, out NpcSnapshot bladeNpc)); stepper.SetNpcPeers([source]);
        Assert.True(stepper.TryStepState(in bladeNpc, out NpcStateUpdate bladeNext));
        Assert.True(bladeNext.VelocityX < 0f); Assert.True(bladeNext.VelocityY < 0f);
    }

    [Fact]
    public void Pumpking_greek_fire_uses_the_committed_local_ai58_clock()
    {
        var npcs = new RuntimeNpcStore();
        var state = new NpcStateUpdate(327, 327, 100f, 200f, 0f, 0f, 3, new NpcAiState(1f, 0f, 0f, 0f),
            NpcSimulationState.Initial with { Life = 26000, LifeMax = 26000, LocalAi = new NpcAiState(0f, 0f, 59f, 0f) });
        Assert.True(npcs.TrySpawn(7, state, out _)); var projectiles = new RuntimeProjectileStore();
        var random = new SequenceRandom(); VanillaNpcTargetingAiStepper stepper = CreateStepper(random, solid: false);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EverscreamOnly(stepper)).Applied);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.GreekFire1, projectile.Type); Assert.Equal((short)40, projectile.Damage);
        Assert.Equal(5, random.Draws);
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

    private static void AssertPumpkingProjectile(NpcAiState ai, ProjectileTypeId expectedType, int expectedDamage, int expectedDraws)
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(1, PumpkingUpdate(ai), out NpcSnapshot source));
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
        stepper.SetProjectileEnvironment(new EmptyProjectileEnvironment());
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

    private static NpcSnapshot CreatePumpking(NpcAiState ai) => new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
        325, 325, 100f, 200f, 0f, 0f, 3, ai,
        NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 14000, LifeMax = 14000, TimeLeft = 750 });

    private static NpcStateUpdate PumpkingUpdate(NpcAiState ai) => new(325, 325, 100f, 200f, 0f, 0f, 3, ai,
        NpcSimulationState.Initial with { DirectionX = 1, SpriteDirection = 1, Life = 14000, LifeMax = 14000, TimeLeft = 750 });

    private sealed class Environment(bool solid) : IVanillaEverscreamEnvironment
    {
        public bool SolidCollision(float positionX, float positionY, int width, int height) => solid;
    }

    private sealed class EmptyProjectileEnvironment : IVanillaNpcProjectileEnvironment, IVanillaNpcSolidTileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
        public bool IsSolidTile(int tileX, int tileY) => false;
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
