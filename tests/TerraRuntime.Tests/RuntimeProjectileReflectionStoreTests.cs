using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileReflectionStoreTests
{
    [Fact]
    public void Spawn_initializes_old_velocity_and_reflection_is_generation_safe_one_shot()
    {
        var store = new RuntimeProjectileStore(capacity: 2);
        var update = new ProjectileStateUpdate(
            VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: 0,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 3f,
            VelocityY: 4f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        Assert.True(store.TrySpawn(0, in update, out ProjectileSnapshot projectile));
        Assert.True(store.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState initial));
        Assert.Equal(0f, initial.OldVelocityX);
        Assert.Equal(0f, initial.OldVelocityY);
        Assert.True(store.TryCommitSimulationStep(
            projectile.Handle,
            in update,
            initial.TimeLeft,
            out projectile,
            out bool expired));
        Assert.False(expired);
        Assert.True(store.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState stepped));
        Assert.Equal(3f, stepped.OldVelocityX);
        Assert.Equal(4f, stepped.OldVelocityY);

        Assert.True(store.TryReflect(projectile.Handle, -5f, 0f, 5, out ProjectileSnapshot reflected));
        Assert.Equal(projectile.Revision.Value + 1, reflected.Revision.Value);
        Assert.False(store.TryReflect(projectile.Handle, 5f, 0f, 1, out _));
        Assert.True(store.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle));
        Assert.True(lifecycle.Reflected);
        Assert.Equal(1, lifecycle.PenetrateOverride);
    }
}
