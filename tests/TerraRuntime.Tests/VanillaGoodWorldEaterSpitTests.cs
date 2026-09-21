using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaGoodWorldEaterSpitTests
{
    [Fact]
    public void Good_world_eater_spit_has_source_defaults_and_ai_009_coverage()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.EaterOfWorldsSpit, out VanillaNpcDefinition definition));
        Assert.Equal((16, 16, 65, 0, 1, .9f),
            (definition.BaseWidth, definition.BaseHeight, definition.Damage, definition.Defense, definition.LifeMax, definition.Scale));
        Assert.Equal(VanillaNpcAiStyles.BurningSphere, definition.AiStyle);
        Assert.True(definition.NoGravityAtSpawn);
        Assert.True(definition.NoTileCollideAtSpawn);
        Assert.Equal(80, definition.AlphaAtSpawn);

        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.EaterOfWorldsSpit, out VanillaNpcAiCoverage spitCoverage));
        Assert.True(spitCoverage.Has(VanillaNpcAiCapability.DefinitionDefaults | VanillaNpcAiCapability.StateTransitionSlice));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.EaterOfSouls, out VanillaNpcAiCoverage eaterCoverage));
        Assert.True(eaterCoverage.Has(VanillaNpcAiCapability.ChildSpawnSlice));
    }

    [Fact]
    public void Good_world_eater_timer_spawns_npc_666_on_the_exact_sixtieth_tick()
    {
        var stepper = CreateStepper();
        NpcSnapshot before = Eater(localAi0: 59f, justHit: false);

        Assert.True(stepper.TryStepState(in before, out NpcStateUpdate next));
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
        Assert.False(next.Simulation.JustHit);

        NpcSnapshot committed = before with
        {
            Revision = new NpcRevision(2),
            Target = next.Target,
            VelocityX = next.VelocityX,
            VelocityY = next.VelocityY,
            Simulation = next.Simulation
        };
        var mutations = new CapturingMutationSink();

        stepper.ApplyCommittedEffect(in before, in committed, mutations);

        Assert.True(mutations.Spawned);
        Assert.Equal(VanillaNpcIds.EaterOfWorldsSpit, mutations.Intent.Type);
        Assert.Equal((27, 34, 0f, 0f, (ushort)byte.MaxValue),
            (mutations.Intent.BottomX, mutations.Intent.BottomY, mutations.Intent.VelocityX, mutations.Intent.VelocityY, mutations.Intent.Target));
    }

    [Fact]
    public void Eater_hit_restarts_good_world_timer_before_the_same_tick_increment()
    {
        var stepper = CreateStepper();
        NpcSnapshot before = Eater(localAi0: 59f, justHit: true);

        Assert.True(stepper.TryStepState(in before, out NpcStateUpdate next));
        Assert.Equal(1f, next.Simulation.LocalAi.Ai0);
        Assert.False(next.Simulation.JustHit);

        NpcSnapshot committed = before with { Revision = new NpcRevision(2), Target = next.Target, Simulation = next.Simulation };
        var mutations = new CapturingMutationSink();
        stepper.ApplyCommittedEffect(in before, in committed, mutations);
        Assert.False(mutations.Spawned);
    }

    [Fact]
    public void Good_world_eater_timer_keeps_advancing_without_a_player_target()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetCandidates([]);
        stepper.SetNpcPeers([Head()]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, goodWorld: true);
        NpcSnapshot before = Eater(localAi0: 59f, justHit: false) with { Target = byte.MaxValue };

        Assert.True(stepper.TryStepState(in before, out NpcStateUpdate next));
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
        Assert.False(next.Simulation.JustHit);
    }

    [Fact]
    public void Npc_666_acquires_target_at_good_world_speed_and_expires_inside_solid_tiles()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetCandidates([Target(100f, 50f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, goodWorld: true);
        NpcSnapshot before = new(new NpcHandle(2, new NpcGeneration(1)), new NpcRevision(1),
            VanillaNpcIds.EaterOfWorldsSpit.Value, (short)VanillaNpcIds.EaterOfWorldsSpit.Value,
            0f, 0f, 0f, 0f, byte.MaxValue, new NpcAiState(1f, 0f, 0f, 0f),
            NpcSimulationState.Initial with
            {
                Life = 1, LifeMax = 1, Scale = .9f, TimeLeft = 937, SpawnDifficulty = 2f, SolidCollision = true
            });

        Assert.True(stepper.TryStepState(in before, out NpcStateUpdate next));
        Assert.Equal((ushort)0, next.Target);
        Assert.Equal(2f, next.Ai.Ai0);
        Assert.Equal(64, next.Simulation.DamageOverride);
        Assert.Equal(0, next.Simulation.Life);
        Assert.Equal(0, next.Simulation.TimeLeft);
        Assert.InRange(MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY), 9.999f, 10.001f);
        Assert.Equal((next.VelocityX, next.VelocityY), (next.PositionX, next.PositionY));
    }

    [Fact]
    public void Corruptor_spawns_npc_112_after_its_source_post_hit_clock()
    {
        var stepper = CreateStepper();
        NpcSnapshot before = new(new NpcHandle(4, new NpcGeneration(1)), new NpcRevision(1),
            VanillaNpcIds.Corruptor.Value, (short)VanillaNpcIds.Corruptor.Value,
            10f, 20f, 2f, -1f, 0, default,
            NpcSimulationState.Initial with
            {
                Life = 230, LifeMax = 230, Scale = 1f, TimeLeft = 750,
                LocalAi = new NpcAiState(179f, 0f, 0f, 0f)
            });

        Assert.True(stepper.TryStepState(in before, out NpcStateUpdate next));
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
        Assert.False(next.Simulation.JustHit);

        NpcSnapshot committed = before with
        {
            Revision = new NpcRevision(2), Target = next.Target, VelocityX = next.VelocityX, VelocityY = next.VelocityY,
            Simulation = next.Simulation
        };
        var mutations = new CapturingMutationSink();
        stepper.ApplyCommittedEffect(in before, in committed, mutations);

        Assert.True(mutations.Spawned);
        Assert.Equal(VanillaNpcIds.CorruptorSpit, mutations.Intent.Type);
        Assert.Equal((int)(committed.PositionX + 22f + committed.VelocityX), mutations.Intent.BottomX);
        Assert.Equal((int)(committed.PositionY + 22f + committed.VelocityY), mutations.Intent.BottomY);
        Assert.Equal((0f, 0f, (ushort)byte.MaxValue),
            (mutations.Intent.VelocityX, mutations.Intent.VelocityY, mutations.Intent.Target));
    }

    [Fact]
    public void Npc_112_uses_the_source_ai009_launch_clock_and_solid_collision_expiry()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetCandidates([Target(100f, 50f)]);
        NpcSnapshot before = new(new NpcHandle(5, new NpcGeneration(1)), new NpcRevision(1),
            VanillaNpcIds.CorruptorSpit.Value, (short)VanillaNpcIds.CorruptorSpit.Value,
            0f, 0f, 1f, 2f, byte.MaxValue, new NpcAiState(1f, 0f, 0f, 0f),
            NpcSimulationState.Initial with
            {
                Life = 1, LifeMax = 1, Scale = .9f, TimeLeft = 937, SolidCollision = true
            });

        Assert.True(stepper.TryStepState(in before, out NpcStateUpdate next));
        Assert.Equal((ushort)0, next.Target);
        Assert.Equal(2f, next.Ai.Ai0);
        Assert.Equal(65, next.Simulation.DamageOverride);
        Assert.Equal(0, next.Simulation.Life);
        Assert.Equal(0, next.Simulation.TimeLeft);
        Assert.Equal((next.VelocityX, next.VelocityY), (next.PositionX, next.PositionY));

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.CorruptorSpit, out VanillaNpcDefinition definition));
        Assert.Equal((16, 16, 65, 0, 1, .9f),
            (definition.BaseWidth, definition.BaseHeight, definition.Damage, definition.Defense, definition.LifeMax, definition.Scale));
        Assert.Equal(80, definition.AlphaAtSpawn);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([Target(100f, 50f)]);
        stepper.SetNpcPeers([Head()]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, goodWorld: true);
        return stepper;
    }

    private static NpcSnapshot Eater(float localAi0, bool justHit) =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.EaterOfSouls.Value,
            (short)VanillaNpcIds.EaterOfSouls.Value, 10f, 20f, 2f, -1f, 0, default,
            NpcSimulationState.Initial with
            {
                Life = 40, LifeMax = 40, Scale = 1f, TimeLeft = 750, LocalAi = new NpcAiState(localAi0, 0f, 0f, 0f),
                JustHit = justHit
            });

    private static NpcSnapshot Head() =>
        new(new NpcHandle(3, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.EaterOfWorldsHead.Value,
            (short)VanillaNpcIds.EaterOfWorldsHead.Value, 400f, 400f, 0f, 0f, 0, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100, Scale = 1f, TimeLeft = 750 });

    private static VanillaNpcTargetCandidate Target(float x, float y) => new(0, x, y, 0, true, false, false, false);

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
    }

    private sealed class CapturingMutationSink : INpcAiCommittedNpcMutationSink
    {
        public bool Spawned { get; private set; }
        public NpcAiSpawnIntent Intent { get; private set; }
        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed) { committed = default; return false; }
        public int TryHeal(NpcHandle npc, int maximumAmount) => 0;
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned)
        { Intent = intent; Spawned = true; spawned = default; return true; }
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned) { spawned = default; return false; }
        public bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant) => false;
        public bool TryUpdateVelocity(NpcHandle npc, float velocityX, float velocityY, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryGetActive(byte slot, out NpcSnapshot npc) { npc = default; return false; }
        public bool TryDespawn(NpcHandle npc) => false;
        public bool TryTranslate(NpcHandle npc, float deltaX, float deltaY, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryLinkFollower(NpcHandle npc, byte followerSlot) => false;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
