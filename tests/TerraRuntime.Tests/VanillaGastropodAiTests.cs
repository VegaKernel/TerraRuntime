using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaGastropodAiTests
{
    [Fact]
    public void Expert_clock_commits_before_spawning_the_source_bolt()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new MinimumRandom());
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 225f, 110f, 0, true, false, false, false)]);
        NpcSnapshot before = new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 163, 163,
            100f, 100f, 0f, 0f, 7, default, NpcSimulationState.Initial with { LocalAi = new NpcAiState(899f, 0f, 0f, 0f) });
        NpcSnapshot committed = before with { Revision = new NpcRevision(2) };
        var mutations = new CapturingSink();

        NpcSnapshot completed = stepper.CompleteCommittedState(in before, in committed, mutations);

        Assert.True(mutations.StateUpdated);
        Assert.Equal(0f, completed.Simulation.LocalAi.Ai0);
        Assert.True(mutations.ProjectileSpawned);
        Assert.Equal(VanillaProjectileIds.GastropodBolt, mutations.Projectile.Type);
        Assert.Equal((121f, 106f), (mutations.Projectile.PositionX, mutations.Projectile.PositionY));
        Assert.Equal(18, mutations.Projectile.Damage);
        Assert.Equal(8f, MathF.Sqrt(mutations.Projectile.VelocityX * mutations.Projectile.VelocityX + mutations.Projectile.VelocityY * mutations.Projectile.VelocityY), 5);
    }

    [Fact]
    public void Gastropod_bolt_keeps_source_arrow_defaults()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.GastropodBolt, out var definition));
        Assert.Equal((8, 8, VanillaProjectileAiStyles.Arrow), (definition.Width, definition.Height, definition.AiStyle));
        Assert.True(definition.TileCollide);
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.GastropodBolt, out int penetrate));
        Assert.Equal(-1, penetrate);
    }

    private sealed class RejectingStepper : INpcAiStateStepper { public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; } }
    private sealed class MinimumRandom : IVanillaNpcRandom { public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin; }
    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    { public bool CanHit(float a, float b, int c, int d, float e, float f, int g, int h) => true; }
    private sealed class CapturingSink : INpcAiCommittedNpcMutationSink
    {
        public bool StateUpdated { get; private set; }
        public bool ProjectileSpawned { get; private set; }
        public NpcAiProjectileIntent Projectile { get; private set; }
        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed) { committed = expected with { Ai = ai }; return true; }
        public bool TryUpdateState(in NpcSnapshot expected, in NpcStateUpdate update, out NpcSnapshot committed)
        { StateUpdated = true; committed = expected with { Simulation = update.Simulation }; return true; }
        public int TryHeal(NpcHandle npc, int maximumAmount) => 0;
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned) { ProjectileSpawned = true; Projectile = intent; spawned = default; return true; }
        public bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant) => false;
        public bool TryUpdateVelocity(NpcHandle npc, float x, float y, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryGetActive(byte slot, out NpcSnapshot npc) { npc = default; return false; }
        public bool TryDespawn(NpcHandle npc) => false;
        public bool TryTranslate(NpcHandle npc, float x, float y, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryLinkFollower(NpcHandle npc, byte slot) => false;
    }
}
