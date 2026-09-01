using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaSkeletronSkullProjectileTests
{
    [Fact]
    public void Expert_head_plans_source_owned_skull_with_300_tick_lifetime()
    {
        var npcs = new RuntimeNpcStore(capacity: 8);
        NpcSnapshot head = SpawnHead(npcs, new NpcAiState(1f, 0f, 40f, 0f));
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: new CenteredRandom());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 500f, 300f, 0, true, false, false, false)]);
        stepper.SetNpcPeers([head]);
        stepper.SetProjectileEnvironment(new AlwaysHitEnvironment());
        Assert.True(stepper.TryStepState(in head, out NpcStateUpdate proposed));

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in head, in proposed, intents));

        NpcAiProjectileIntent skull = intents[0];
        Assert.Equal(VanillaProjectileIds.SkeletronSkull, skull.Type);
        Assert.Equal(17, skull.Damage);
        Assert.Equal(0f, skull.KnockBack);
        Assert.Equal(-1f, skull.InitialAi.Ai0);
        Assert.Equal(300, skull.TimeLeftOverride);
        Assert.True(float.IsFinite(skull.VelocityX));
        Assert.True(float.IsFinite(skull.VelocityY));
    }

    [Fact]
    public void Skull_intent_applies_ai_and_lifetime_atomically_with_spawn_generation()
    {
        var store = new RuntimeProjectileStore(capacity: 8);
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.SkeletronSkull,
            100f,
            120f,
            5f,
            0f,
            17,
            0f)
        {
            InitialAi = new ProjectileAiState(-1f, 0f, 0f),
            TimeLeftOverride = 300
        };

        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(store, in intent, out ProjectileSnapshot spawned));
        Assert.Equal(-1f, spawned.Ai.Ai0);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(300, lifecycle.TimeLeft);
    }

    [Fact]
    public void Skull_ai_homes_during_ticks_31_through_109_and_accelerates_below_18()
    {
        var projectile = new ProjectileSnapshot(
            Handle: default,
            Revision: default,
            Type: VanillaProjectileIds.SkeletronSkull,
            Spawner: byte.MaxValue,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 5f,
            VelocityY: 0f,
            Ai: new ProjectileAiState(-1f, 30f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 17,
            KnockBack: 0f,
            OriginalDamage: 17);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, new SinglePlayerLookup());

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out VanillaProjectileBehaviorResult next));

        Assert.Equal(31f, next.Ai1Override);
        Assert.True(next.VelocityY > 0f);
        float speed = MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY);
        Assert.Equal(5.1f, speed, 3);
    }

    private static NpcSnapshot SpawnHead(RuntimeNpcStore store, NpcAiState ai)
    {
        var update = new NpcStateUpdate(
            VanillaNpcIds.SkeletronHead.Value,
            checked((short)VanillaNpcIds.SkeletronHead.Value),
            100f,
            100f,
            0f,
            0f,
            0,
            ai,
            NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot head));
        return head;
    }

    private sealed class CenteredRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) =>
            inclusiveMin <= 0 && exclusiveMax > 0 ? 0 : inclusiveMin;
    }

    private sealed class AlwaysHitEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight) => true;
    }

    private sealed class SinglePlayerLookup : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot.Value != 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = new PlayerStateSnapshot(
                new PlayerHandle(slot, new PlayerSessionGeneration(1)),
                new PlayerStateRevision(1),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX: 100f,
                PositionY: 300f,
                VelocityX: 0f,
                VelocityY: 0f,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f)
            {
                IsDead = false
            };
            return true;
        }
    }
}
