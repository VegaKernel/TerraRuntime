using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class GoblinSorcererAiTests
{
    [Fact]
    public void Goblin_sorcerer_ai008_spawns_chaos_ball_after_the_source_countdown()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetWallOfFleshEnvironment(new Environment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 300f, 120f, 0, true, false, false, false)]);
        var source = new NpcSnapshot(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 29, 29,
            100f, 100f, 0f, 0f, 0, new NpcAiState(10f, 26f, 0f, 0f), NpcSimulationState.Initial);

        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next));
        Assert.Equal(25f, next.Ai.Ai1);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[1];
        Assert.Equal(1, stepper.PlanNpcSpawns(in source, in next, intents));
        Assert.Equal(VanillaNpcIds.ChaosBall, intents[0].Type);
        Assert.Equal(109, intents[0].BottomX);
        Assert.Equal(92, intents[0].BottomY);
    }

    [Fact]
    public void Tim_ai008_spawns_his_source_fireball_after_the_source_countdown()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetWallOfFleshEnvironment(new Environment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 300f, 120f, 0, true, false, false, false)]);
        var source = new NpcSnapshot(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 45, 45,
            100f, 100f, 0f, 0f, 0, new NpcAiState(10f, 26f, 0f, 0f), NpcSimulationState.Initial);

        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next));
        Assert.Equal(25f, next.Ai.Ai1);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[1];
        Assert.Equal(1, stepper.PlanNpcSpawns(in source, in next, intents));
        Assert.Equal(VanillaNpcIds.TimFireball, intents[0].Type);
        Assert.Equal(109, intents[0].BottomX);
        Assert.Equal(92, intents[0].BottomY);
    }

    [Fact]
    public void Tim_ai008_keeps_the_global_firing_distance_while_goblin_sorcerer_bypasses_it()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetWallOfFleshEnvironment(new Environment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 3_000f, 120f, 0, true, false, false, false)]);
        var tim = new NpcSnapshot(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 45, 45,
            100f, 100f, 0f, 0f, 0, new NpcAiState(99f, 0f, 0f, 0f), NpcSimulationState.Initial);
        var goblin = tim with { Type = 29, NetId = 29 };

        Assert.True(stepper.TryStepState(in tim, out NpcStateUpdate timNext));
        Assert.True(stepper.TryStepState(in goblin, out NpcStateUpdate goblinNext));
        Assert.Equal(0f, timNext.Ai.Ai1);
        Assert.Equal(29f, goblinNext.Ai.Ai1);
    }

    [Fact]
    public void Rune_wizard_ai008_uses_its_75_tick_cadence_fade_and_post_commit_rune_blast()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetWallOfFleshEnvironment(new Environment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 300f, 120f, 0, true, false, false, false)]);
        var source = new NpcSnapshot(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 172, 172,
            100f, 100f, 0f, 0f, 0, new NpcAiState(74f, 26f, 0f, 0f),
            NpcSimulationState.Initial with { Alpha = 199 });

        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next));
        Assert.Equal(75f, next.Ai.Ai0);
        Assert.Equal(29f, next.Ai.Ai1);
        Assert.Equal(200, next.Simulation.Alpha);

        var shotSource = source with { Ai = source.Ai with { Ai0 = 73f } };
        Assert.True(stepper.TryStepState(in shotSource, out NpcStateUpdate shotNext));
        Assert.Equal(25f, shotNext.Ai.Ai1);
        var committed = shotSource with { Revision = new NpcRevision(2), Ai = shotNext.Ai, Simulation = shotNext.Simulation };
        var mutations = new CapturingMutationSink();
        stepper.CompleteCommittedState(in shotSource, in committed, mutations);
        Assert.True(mutations.ProjectileSpawned);
        Assert.Equal(VanillaProjectileIds.RuneBlast, mutations.Projectile.Type);
        Assert.Equal(102f, mutations.Projectile.PositionX);
        Assert.Equal(113f, mutations.Projectile.PositionY);
        Assert.Equal(40, mutations.Projectile.Damage);
        Assert.Equal(300, mutations.Projectile.TimeLeftOverride);
        Assert.InRange(MathF.Sqrt(mutations.Projectile.VelocityX * mutations.Projectile.VelocityX + mutations.Projectile.VelocityY * mutations.Projectile.VelocityY), 9.999f, 10.001f);
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; } }
    private sealed class ZeroRandom : IVanillaNpcRandom
    { public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin; }
    private sealed class Environment : IVanillaWallOfFleshEnvironment
    {
        public int WorldWidthTiles => 400; public int WorldHeightTiles => 400; public int UnderworldLayerTiles => 200;
        public bool TryResolveCorridor(float x, float y, int w, int h, out float top, out float bottom) { top = bottom = 0; return false; }
        public bool CanHit(float a,float b,int c,int d,float e,float f,int g,int h) => true;
        public bool TryFindGroundSpawn(int x,int y,out int a,out int b) { a=b=0; return false; }
        public bool TryFindTeleportSpot(float a,float b,int c,int d,ReadOnlySpan<VanillaNpcTargetCandidate> e,IVanillaNpcRandom f,out int x,out int y) { x=y=0; return false; }
    }

    private sealed class CapturingMutationSink : INpcAiCommittedNpcMutationSink
    {
        public bool ProjectileSpawned { get; private set; }
        public NpcAiProjectileIntent Projectile { get; private set; }
        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed) { committed = expected with { Ai = ai }; return true; }
        public int TryHeal(NpcHandle npc, int maximumAmount) => 0;
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned) { spawned = default; return false; }
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned) { Projectile = intent; ProjectileSpawned = true; spawned = default; return true; }
        public bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant) => false;
        public bool TryUpdateVelocity(NpcHandle npc, float velocityX, float velocityY, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryGetActive(byte slot, out NpcSnapshot npc) { npc = default; return false; }
        public bool TryDespawn(NpcHandle npc) => false;
        public bool TryTranslate(NpcHandle npc, float deltaX, float deltaY, out NpcSnapshot committed) { committed = default; return false; }
        public bool TryLinkFollower(NpcHandle npc, byte followerSlot) => false;
    }
}
