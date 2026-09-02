using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeArchetypeBehaviorDispatchTests
{
    [Fact]
    public void Two_npc_archetypes_can_share_zombie_presentation_and_use_distinct_replacements()
    {
        var identities = new RuntimeNpcArchetypeIdentityStore(capacity: 4);
        var store = new RuntimeNpcStore(capacity: 4, commitSink: identities);
        var archetypes = new RuntimeNpcArchetypeRegistry();
        var namedBehaviors = new RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper>();
        GameplayArchetypeId guard = new("worldslicer:guard");
        GameplayArchetypeId runner = new("worldslicer:runner");
        GameplayExtensionId guardAi = new("worldslicer:guard-ai");
        GameplayExtensionId runnerAi = new("worldslicer:runner-ai");

        Assert.Equal(GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(new NpcArchetypeDescriptor(guard, VanillaNpcIds.Zombie, guardAi), out _));
        Assert.Equal(GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(new NpcArchetypeDescriptor(runner, VanillaNpcIds.Zombie, runnerAi), out _));
        archetypes.CommitPending();
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered,
            namedBehaviors.TryRegister(guardAi, new NpcVelocityBehavior(1f), out _));
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered,
            namedBehaviors.TryRegister(runnerAi, new NpcVelocityBehavior(4f), out _));
        namedBehaviors.CommitPending();

        var spawner = new RuntimeNpcArchetypeSpawner(store, archetypes, identities);
        var guardRequest = new NpcArchetypeSpawnRequest(guard, 0, 0f, 0f);
        var runnerRequest = new NpcArchetypeSpawnRequest(runner, 1, 0f, 0f);
        Assert.True(spawner.TrySpawn(in guardRequest, out NpcSnapshot guardNpc));
        Assert.True(spawner.TrySpawn(in runnerRequest, out NpcSnapshot runnerNpc));

        var composite = new RuntimeNpcBehaviorStateStepper(
            new NoNpcBehavior(),
            new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>(),
            archetypeBehaviors: namedBehaviors,
            archetypes: archetypes,
            identities: identities);

        Assert.True(composite.TryStepState(in guardNpc, out NpcStateUpdate guardNext));
        Assert.True(composite.TryStepState(in runnerNpc, out NpcStateUpdate runnerNext));
        Assert.Equal(1f, guardNext.VelocityX);
        Assert.Equal(4f, runnerNext.VelocityX);
        Assert.Equal(guardNpc.Type, guardNext.Type);
        Assert.Equal(runnerNpc.Type, runnerNext.Type);
    }

    [Fact]
    public void Projectile_archetype_behavior_resolves_by_generation_safe_identity_not_presentation_only()
    {
        var identities = new RuntimeProjectileArchetypeIdentityStore(capacity: 2);
        var store = new RuntimeProjectileStore(capacity: 2, commitSink: identities);
        var archetypes = new RuntimeProjectileArchetypeRegistry();
        var namedBehaviors = new RuntimeArchetypeBehaviorRegistry<IProjectileStateStepper>();
        GameplayArchetypeId archetypeId = new("worldslicer:fast-shuriken");
        GameplayExtensionId behaviorId = new("worldslicer:fast-shuriken-ai");
        Assert.Equal(GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(
                new ProjectileArchetypeDescriptor(archetypeId, VanillaProjectileIds.Shuriken, behaviorId),
                out _));
        archetypes.CommitPending();
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered,
            namedBehaviors.TryRegister(behaviorId, new ProjectileVelocityBehavior(9f), out _));
        namedBehaviors.CommitPending();

        var spawner = new RuntimeProjectileArchetypeSpawner(store, archetypes, identities);
        var request = new ProjectileArchetypeSpawnRequest(
            archetypeId,
            VanillaProjectileOwnership.ServerOwner,
            0f,
            0f,
            1f,
            0f,
            Damage: 5,
            KnockBack: 1f,
            OriginalDamage: 5);
        Assert.True(spawner.TrySpawn(in request, out ProjectileSnapshot projectile));
        Assert.True(store.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle));
        var context = new ProjectileSimulationStepContext(projectile, lifecycle, 0, 1);

        var composite = new RuntimeProjectileBehaviorStateStepper(
            new NoProjectileBehavior(),
            new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>(),
            archetypeBehaviors: namedBehaviors,
            archetypes: archetypes,
            identities: identities);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult next));
        Assert.Equal(9f, next.State.VelocityX);
        Assert.Equal(projectile.Type, next.State.Type);
        Assert.Equal(projectile.Spawner, next.State.Spawner);
    }

    private sealed class NpcVelocityBehavior(float velocityX) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }

    private sealed class NoNpcBehavior : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class ProjectileVelocityBehavior(float velocityX) : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    current.PositionX,
                    current.PositionY,
                    velocityX,
                    current.VelocityY,
                    current.Ai,
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                projectile.Lifecycle.TimeLeft,
                projectile.Lifecycle.Liquid);
            return true;
        }
    }

    private sealed class NoProjectileBehavior : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            next = default;
            return false;
        }
    }
}
