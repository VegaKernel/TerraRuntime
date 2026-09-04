using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileExplosionQueueTests
{
    [Fact]
    public void Trusted_launcher_termination_preserves_generation_owner_and_prepare_bomb_damage_shape()
    {
        var queue = new RuntimeProjectileExplosionQueue(4);
        ProjectileSnapshot projectile = CreateProjectile(VanillaProjectileIds.GrenadeI, 100f, 200f, 3f);
        var owner = new PlayerHandle(new PlayerSlotId(3), new PlayerSessionGeneration(8));
        var termination = new ProjectileTerminationCommit(
            projectile,
            projectile,
            ProjectileSimulationTerminationReason.LifetimeExpired,
            CombatTrusted: true,
            TrustedOwner: owner);

        queue.ProjectileTerminated(in termination);

        Assert.Equal(1, queue.Events.Length);
        RuntimeProjectileExplosionEvent explosion = queue.Events[0];
        Assert.Equal(projectile.Handle, explosion.Projectile.Handle);
        Assert.Equal(owner, explosion.TrustedOwner);
        Assert.Equal(43f, explosion.Left, 5);
        Assert.Equal(143f, explosion.Top, 5);
        Assert.Equal(128, explosion.Width);
        Assert.Equal(128, explosion.Height);
        Assert.Equal(8f, explosion.Projectile.KnockBack, 5);
    }

    [Fact]
    public void World_bounds_or_untrusted_termination_does_not_create_explosion_damage()
    {
        var queue = new RuntimeProjectileExplosionQueue(4);
        ProjectileSnapshot projectile = CreateProjectile(VanillaProjectileIds.RocketIV, 100f, 200f, 3f);
        var owner = new PlayerHandle(new PlayerSlotId(3), new PlayerSessionGeneration(8));
        var worldBounds = new ProjectileTerminationCommit(
            projectile,
            projectile,
            ProjectileSimulationTerminationReason.WorldBounds,
            CombatTrusted: true,
            TrustedOwner: owner);
        queue.ProjectileTerminated(in worldBounds);

        var untrusted = worldBounds with
        {
            Reason = ProjectileSimulationTerminationReason.LifetimeExpired,
            CombatTrusted = false
        };
        queue.ProjectileTerminated(in untrusted);

        Assert.Equal(0, queue.Events.Length);
    }

    private static ProjectileSnapshot CreateProjectile(
        ProjectileTypeId type,
        float positionX,
        float positionY,
        float knockBack) =>
        new(
            new ProjectileHandle(7, new ProjectileGeneration(5)),
            new ProjectileRevision(9),
            type,
            Spawner: 3,
            positionX,
            positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: new ProjectileAiState(60f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 100,
            KnockBack: knockBack,
            OriginalDamage: 100);
}
