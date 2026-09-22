using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaHornetStingerAiTests
{
    [Fact]
    public void Hornet_stinger_has_source_hostile_arrow_defaults_and_coverage()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.HornetStinger, out VanillaProjectileDefinition definition));
        Assert.Equal((10, 10, VanillaProjectileAiStyles.Arrow), (definition.Width, definition.Height, definition.AiStyle));
        Assert.True(definition.TileCollide);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.HornetStinger));
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.HornetStinger, out int penetrate));
        Assert.Equal(-1, penetrate);
        foreach (NpcTypeId type in new[] { VanillaNpcIds.Hornet, VanillaNpcIds.MossHornet, VanillaNpcIds.FattyHornet })
        {
            Assert.True(VanillaNpcAiCoverageCatalog.TryGet(type, out VanillaNpcAiCoverage coverage));
            Assert.True(coverage.Has(VanillaNpcAiCapability.FlyerProjectileSideEffectSlice));
        }
    }

    [Fact]
    public void Hornet_post_commit_timer_uses_player_state_gate_then_shoots_with_source_jitter()
    {
        var random = new SequenceRandom(5, 0, 0);
        var stepper = CreateStepper(random, Target(100f, 50f) with { Stealth = 1f });
        NpcSnapshot before = Snapshot(VanillaNpcIds.Hornet, ai1: 129.5f, velocityX: 2f);
        NpcSnapshot committed = before with { Revision = new NpcRevision(2) };
        var mutations = new CapturingMutationSink();

        NpcSnapshot completed = stepper.CompleteCommittedState(in before, in committed, mutations);

        Assert.Equal(101f, completed.Ai.Ai1);
        Assert.Equal(101f, mutations.LastAiUpdate.Ai1);
        Assert.True(mutations.ProjectileSpawned);
        Assert.Equal(VanillaProjectileIds.HornetStinger, mutations.Projectile.Type);
        Assert.Equal(10, mutations.Projectile.Damage);
        Assert.Equal(300, mutations.Projectile.TimeLeftOverride);
        Assert.Equal((17f, 16f), (mutations.Projectile.PositionX, mutations.Projectile.PositionY));
        Assert.InRange(MathF.Sqrt(mutations.Projectile.VelocityX * mutations.Projectile.VelocityX +
            mutations.Projectile.VelocityY * mutations.Projectile.VelocityY), 7.999f, 8.001f);
        Assert.Equal(3, random.CallCount);
    }

    [Fact]
    public void Idle_nonstealthed_player_resets_hornet_timer_after_source_increment()
    {
        var random = new SequenceRandom(19);
        var stepper = CreateStepper(random, Target(100f, 50f));
        NpcSnapshot before = Snapshot(VanillaNpcIds.Hornet, ai1: 129.9f, velocityX: 2f);
        NpcSnapshot committed = before with { Revision = new NpcRevision(2) };
        var mutations = new CapturingMutationSink();

        NpcSnapshot completed = stepper.CompleteCommittedState(in before, in committed, mutations);

        Assert.Equal(0f, completed.Ai.Ai1);
        Assert.False(mutations.ProjectileSpawned);
        Assert.Equal(1, random.CallCount);
    }

    [Fact]
    public void Moss_hornet_uses_second_timer_roll_and_thirty_damage_stinger()
    {
        var random = new SequenceRandom(5, 5, 0, 0);
        var stepper = CreateStepper(random, Target(100f, 50f) with { Stealth = 1f });
        NpcSnapshot before = Snapshot(VanillaNpcIds.MossHornet, ai1: 129f, velocityX: 2f);
        NpcSnapshot committed = before with { Revision = new NpcRevision(2) };
        var mutations = new CapturingMutationSink();

        stepper.CompleteCommittedState(in before, in committed, mutations);

        Assert.True(mutations.ProjectileSpawned);
        Assert.Equal(30, mutations.Projectile.Damage);
        Assert.Equal(4, random.CallCount);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaNpcRandom random, VanillaNpcTargetCandidate target)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([target]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        return stepper;
    }

    private static NpcSnapshot Snapshot(NpcTypeId type, float ai1, float velocityX) =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), type.Value, checked((short)type.Value),
            0f, 0f, velocityX, 0f, 0, new NpcAiState(0f, ai1, 0f, 0f),
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private static VanillaNpcTargetCandidate Target(float x, float y) =>
        new(0, x, y, 0, true, false, false, false);

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int index;
        public int CallCount => index;
        public int NextInt32(int inclusiveMin, int exclusiveMax) => values[index++];
    }

    private sealed class CapturingMutationSink : INpcAiCommittedNpcMutationSink
    {
        public bool ProjectileSpawned { get; private set; }
        public NpcAiState LastAiUpdate { get; private set; }
        public NpcAiProjectileIntent Projectile { get; private set; }
        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed)
        { LastAiUpdate = ai; committed = expected with { Ai = ai }; return true; }
        public int TryHeal(NpcHandle npc, int maximumAmount) => 0;
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned)
        { Projectile = intent; ProjectileSpawned = true; spawned = default; return true; }
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
