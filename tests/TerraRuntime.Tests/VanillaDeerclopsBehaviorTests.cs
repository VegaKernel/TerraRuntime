using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaDeerclopsBehaviorTests
{
    [Fact]
    public void Source_defaults_register_deerclops_as_ai_123_boss()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Deerclops, out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcAiStyles.Deerclops, definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.Deerclops, definition.BehaviorFamily);
        Assert.Equal(NpcArchetypeRole.Boss, definition.Role);
        Assert.Equal((60, 154, 20, 10, 7000, 0f),
            (definition.Width, definition.Height, definition.Damage, definition.Defense, definition.LifeMax, definition.KnockBackResist));
    }

    [Fact]
    public void First_tick_captures_bottom_tile_as_home_and_uses_source_86400_lifetime()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot deerclops = Spawn(store, ai: default);
        var stepper = CreateStepper(new TestEnvironment { PlayerInSnow = true });
        stepper.SetCandidates([Target(0, 220f, 220f)]);

        Assert.True(stepper.TryStepState(in deerclops, out NpcStateUpdate next));

        Assert.Equal(8f, next.Ai.Ai2);
        Assert.Equal(15f, next.Ai.Ai3);
        Assert.Equal(86_399, next.Simulation.TimeLeft);
        Assert.True(next.Simulation.NoGravity);
        Assert.True(next.Simulation.NoTileCollide);
    }

    [Fact]
    public void Chasing_target_beyond_2400_pixels_enters_return_home_state()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot deerclops = Spawn(store, new NpcAiState(0f, 10f, 8f, 15f));
        var stepper = CreateStepper(new TestEnvironment { PlayerInSnow = true });
        stepper.SetCandidates([Target(0, 5000f, 5000f)]);

        Assert.True(stepper.TryStepState(in deerclops, out NpcStateUpdate next));

        Assert.Equal(6f, next.Ai.Ai0);
        Assert.Equal(0f, next.Ai.Ai1);
    }

    [Fact]
    public void Distance_shield_reaches_invulnerability_after_thirtieth_far_tick()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot deerclops = Spawn(store, new NpcAiState(0f, 1f, 8f, 15f));
        deerclops = deerclops with
        {
            Simulation = deerclops.Simulation with { LocalAi = new NpcAiState(0f, 0f, 0f, 29f) }
        };
        var stepper = CreateStepper(new TestEnvironment { PlayerInSnow = true });
        stepper.SetCandidates([Target(0, 700f, 200f)]);

        Assert.True(stepper.TryStepState(in deerclops, out NpcStateUpdate next));

        Assert.Equal(30f, next.Simulation.LocalAi.Ai3);
        Assert.True(next.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Despawn_state_finishes_without_normal_death_loot_life_zero_transition()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot deerclops = Spawn(store, new NpcAiState(8f, 39f, 8f, 15f));
        var stepper = CreateStepper(new TestEnvironment { PlayerInSnow = false });
        stepper.SetCandidates([]);

        Assert.True(stepper.TryStepState(in deerclops, out NpcStateUpdate next));

        Assert.Equal(-1, next.Simulation.Life);
        Assert.Equal(0, next.Simulation.TimeLeft);
        Assert.Equal(0f, next.VelocityX);
        Assert.Equal(0f, next.VelocityY);
    }

    [Fact]
    public void Shadow_hand_attack_tick_30_plans_six_hostile_projectiles()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot deerclops = Spawn(store, new NpcAiState(5f, 29f, 8f, 15f));
        var stepper = CreateStepper(new TestEnvironment { PlayerInSnow = true });
        stepper.SetCandidates([Target(0, 300f, 240f)]);

        Assert.True(stepper.TryStepState(in deerclops, out NpcStateUpdate proposed));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[6];
        int count = stepper.PlanProjectileSpawns(in deerclops, in proposed, intents);

        Assert.Equal(6, count);
        for (int index = 0; index < count; index++)
        {
            Assert.Equal(VanillaProjectileIds.DeerclopsShadowHand, intents[index].Type);
            Assert.Equal(15, intents[index].Damage);
            Assert.Equal(300, intents[index].TimeLeftOverride);
        }
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaDeerclopsEnvironment environment)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: new CenteredRandom());
        stepper.SetDeerclopsEnvironment(environment);
        return stepper;
    }

    private static VanillaNpcTargetCandidate Target(byte slot, float x, float y) =>
        new(slot, x, y, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private static NpcSnapshot Spawn(RuntimeNpcStore store, NpcAiState ai)
    {
        var update = new NpcStateUpdate(
            VanillaNpcIds.Deerclops.Value,
            checked((short)VanillaNpcIds.Deerclops.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: ai,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot spawned));
        return spawned;
    }

    private sealed class TestEnvironment : IVanillaDeerclopsEnvironment
    {
        public int WorldHeightTiles => 2400;
        public bool PlayerInSnow { get; init; }
        public bool IsPlayerInSnow(float playerCenterX, float playerCenterY) => PlayerInSnow;
        public bool IsWalkableTile(int tileX, int tileY) => true;
        public bool IsSolidTile(int tileX, int tileY) => false;
        public bool SolidCollision(float positionX, float positionY, int width, int height, bool acceptTopSurfaces) => false;
    }

    private sealed class CenteredRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }
}
