using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileOverflowSimulationTests
{
    [Fact]
    public void Vanilla_executor_skips_physical_overflow_slot_1000()
    {
        var store = new RuntimeProjectileStore();
        ProjectileStateUpdate state = CreateUpdate();
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot normal));
        Assert.True(store.TrySpawn(RuntimeProjectileStore.VanillaOverflowSlot, in state, out ProjectileSnapshot overflow));
        Assert.True(store.TryGetLifecycle(overflow.Handle, out ProjectileLifecycleState overflowLifecycle));
        var executor = new RuntimeProjectileStateExecutor(store);
        var stepper = new CountingStepper();

        ProjectileStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(1, stepper.Calls);
        Assert.True(store.TryGet(normal.Handle, out ProjectileSnapshot updatedNormal));
        Assert.Equal(new ProjectileRevision(2), updatedNormal.Revision);
        Assert.True(store.TryGet(overflow.Handle, out ProjectileSnapshot unchangedOverflow));
        Assert.Equal(new ProjectileRevision(1), unchangedOverflow.Revision);
        Assert.True(store.TryGetLifecycle(overflow.Handle, out ProjectileLifecycleState unchangedLifecycle));
        Assert.Equal(overflowLifecycle, unchangedLifecycle);
    }

    private static ProjectileStateUpdate CreateUpdate() =>
        new(
            Type: new ProjectileTypeId(1),
            Spawner: 3,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 1f,
            VelocityY: 2f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);

    private sealed class CountingStepper : IProjectileStateStepper
    {
        public int Calls { get; private set; }

        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            Calls++;
            ProjectileSnapshot current = projectile.Projectile;
            var state = new ProjectileStateUpdate(
                current.Type,
                current.Spawner,
                current.PositionX + current.VelocityX,
                current.PositionY + current.VelocityY,
                current.VelocityX,
                current.VelocityY,
                current.Ai,
                current.BannerIdToRespondTo,
                current.Damage,
                current.KnockBack,
                current.OriginalDamage);
            next = new ProjectileSimulationStepResult(state, projectile.Lifecycle.TimeLeft);
            return true;
        }
    }
}
