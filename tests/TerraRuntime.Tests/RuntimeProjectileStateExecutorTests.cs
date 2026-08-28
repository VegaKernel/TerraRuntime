using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileStateExecutorTests
{
    [Fact]
    public void Tick_applies_supported_state_transitions_without_allocating_per_projectile_state()
    {
        var store = new RuntimeProjectileStore(capacity: 8);
        ProjectileStateUpdate first = CreateUpdate(type: 1, positionX: 10f, velocityX: 2f);
        ProjectileStateUpdate second = CreateUpdate(type: 2, positionX: 20f, velocityX: -3f);
        Assert.True(store.TrySpawn(1, in first, out ProjectileSnapshot firstSnapshot));
        Assert.True(store.TrySpawn(6, in second, out ProjectileSnapshot secondSnapshot));
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new IntegrateVelocityStepper());

        Assert.Equal(new ProjectileStateTickSummary(2, 2, 2, 0), summary);
        Assert.True(store.TryGet(firstSnapshot.Handle, out ProjectileSnapshot movedFirst));
        Assert.True(store.TryGet(secondSnapshot.Handle, out ProjectileSnapshot movedSecond));
        Assert.Equal(12f, movedFirst.PositionX);
        Assert.Equal(17f, movedSecond.PositionX);
        Assert.Equal((ulong)2, movedFirst.Revision.Value);
        Assert.Equal((ulong)2, movedSecond.Revision.Value);
    }

    [Fact]
    public void Reentrant_slot_reuse_rejects_stale_prepass_proposal()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate original = CreateUpdate(type: 1, positionX: 10f, velocityX: 5f);
        Assert.True(store.TrySpawn(2, in original, out ProjectileSnapshot first));
        var executor = new RuntimeProjectileStateExecutor(store);
        var stepper = new ReuseSlotDuringStep(store);

        ProjectileStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 0, 1), summary);
        Assert.True(store.TryGetActive(2, out ProjectileSnapshot replacement));
        Assert.NotEqual(first.Handle, replacement.Handle);
        Assert.Equal((ulong)2, replacement.Handle.Generation.Value);
        Assert.Equal(100f, replacement.PositionX);
        Assert.Equal((ulong)1, replacement.Revision.Value);
    }

    [Fact]
    public void Unsupported_projectiles_are_examined_without_creating_updates()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 10f, velocityX: 2f);
        Assert.True(store.TrySpawn(1, in state, out ProjectileSnapshot snapshot));
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new NoOpStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), summary);
        Assert.True(store.TryGet(snapshot.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(snapshot, unchanged);
    }

    private static ProjectileStateUpdate CreateUpdate(int type, float positionX, float velocityX) =>
        new(
            Type: new ProjectileTypeId(type),
            Spawner: 3,
            PositionX: positionX,
            PositionY: 50f,
            VelocityX: velocityX,
            VelocityY: 1f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1.5f,
            OriginalDamage: 20);

    private sealed class IntegrateVelocityStepper : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSnapshot projectile, out ProjectileStateUpdate next)
        {
            next = new ProjectileStateUpdate(
                projectile.Type,
                projectile.Spawner,
                projectile.PositionX + projectile.VelocityX,
                projectile.PositionY + projectile.VelocityY,
                projectile.VelocityX,
                projectile.VelocityY,
                projectile.Ai,
                projectile.BannerIdToRespondTo,
                projectile.Damage,
                projectile.KnockBack,
                projectile.OriginalDamage);
            return true;
        }
    }

    private sealed class ReuseSlotDuringStep(RuntimeProjectileStore store) : IProjectileStateStepper
    {
        private readonly RuntimeProjectileStore store = store;

        public bool TryStepState(in ProjectileSnapshot projectile, out ProjectileStateUpdate next)
        {
            Assert.True(store.TryDespawn(projectile.Handle, out _));
            ProjectileStateUpdate replacement = CreateUpdate(type: 2, positionX: 100f, velocityX: 0f);
            Assert.True(store.TrySpawn(projectile.Handle.Slot, in replacement, out _));

            next = new ProjectileStateUpdate(
                projectile.Type,
                projectile.Spawner,
                projectile.PositionX + projectile.VelocityX,
                projectile.PositionY,
                projectile.VelocityX,
                projectile.VelocityY,
                projectile.Ai,
                projectile.BannerIdToRespondTo,
                projectile.Damage,
                projectile.KnockBack,
                projectile.OriginalDamage);
            return true;
        }
    }

    private sealed class NoOpStepper : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSnapshot projectile, out ProjectileStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
