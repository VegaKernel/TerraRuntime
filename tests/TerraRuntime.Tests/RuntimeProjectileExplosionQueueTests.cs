using TerraRuntime.Gameplay.Projectiles;
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

    [Theory]
    [InlineData(452, 144, 0f)]
    [InlineData(454, 208, 1f)]
    public void Moon_lord_projectile_kill_damage_preserves_source_knockback_and_expands_hitbox(
        int rawType,
        int expectedSize,
        float knockBack)
    {
        var queue = new RuntimeProjectileExplosionQueue(4);
        ProjectileSnapshot projectile = CreateProjectile(new ProjectileTypeId(rawType), 100f, 200f, knockBack) with
        {
            Spawner = byte.MaxValue
        };
        var sourceNpc = new NpcHandle(6, new NpcGeneration(4));
        var termination = new ProjectileTerminationCommit(
            projectile,
            projectile,
            ProjectileSimulationTerminationReason.BehaviorKill,
            CombatTrusted: false,
            TrustedOwner: default,
            SourceNpc: sourceNpc);

        queue.ProjectileTerminated(in termination);

        Assert.Single(queue.Events.ToArray());
        RuntimeProjectileExplosionEvent explosion = queue.Events[0];
        Assert.Equal(expectedSize, explosion.Width);
        Assert.Equal(expectedSize, explosion.Height);
        Assert.Equal(knockBack, explosion.Projectile.KnockBack, 5);
        Assert.Equal(sourceNpc, explosion.SourceNpc);
    }

    [Theory]
    [InlineData(467)]
    [InlineData(468)]
    public void Cultist_fireball_kill_damage_preserves_npc_provenance_and_expands_to_176_square(int rawType)
    {
        var queue = new RuntimeProjectileExplosionQueue(4);
        ProjectileSnapshot projectile = CreateProjectile(new ProjectileTypeId(rawType), 100f, 200f, 3.25f) with
        {
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        var sourceNpc = new NpcHandle(14, new NpcGeneration(9));
        var termination = new ProjectileTerminationCommit(
            projectile,
            projectile,
            ProjectileSimulationTerminationReason.BehaviorKill,
            CombatTrusted: false,
            TrustedOwner: default,
            SourceNpc: sourceNpc);

        queue.ProjectileTerminated(in termination);

        Assert.Single(queue.Events.ToArray());
        RuntimeProjectileExplosionEvent explosion = queue.Events[0];
        Assert.Equal(176, explosion.Width);
        Assert.Equal(176, explosion.Height);
        Assert.Equal(3.25f, explosion.Projectile.KnockBack, 5);
        Assert.Equal(sourceNpc, explosion.SourceNpc);
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
