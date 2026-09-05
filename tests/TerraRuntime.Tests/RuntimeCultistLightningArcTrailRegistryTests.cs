using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class RuntimeCultistLightningArcTrailRegistryTests
{
    [Fact]
    public void Committed_segment_positions_form_generation_scoped_collision_trail()
    {
        var trails = new RuntimeCultistLightningArcTrailRegistry(4);
        ProjectileSnapshot initial = Create(new ProjectileHandle(1, new ProjectileGeneration(2)), 100f, 200f);
        ProjectileSnapshot final = initial with { PositionX = 120f, PositionY = 210f };
        ProjectileSimulationStepResult first = Step(initial with { PositionX = 110f, PositionY = 205f }, frameCounter: 0f);
        ProjectileSimulationStepResult second = Step(final, frameCounter: 1f);
        ProjectileLifecycleState lifecycle = new(600, false) { LocalAi = new ProjectileLocalAiState(0f, 0f, 0f) };

        trails.ProjectileSimulationCommitted(in initial, in lifecycle, [first, second], in final, expired: false);

        Assert.True(trails.Intersects(initial.Handle, 14, 14, 101f, 201f, 2f, 2f));
        Assert.True(trails.Intersects(initial.Handle, 14, 14, 111f, 206f, 2f, 2f));
        Assert.False(trails.Intersects(initial.Handle, 14, 14, 130f, 220f, 2f, 2f));
        Assert.False(trails.Intersects(
            new ProjectileHandle(1, new ProjectileGeneration(3)), 14, 14, 101f, 201f, 2f, 2f));
    }

    private static ProjectileSimulationStepResult Step(ProjectileSnapshot projectile, float frameCounter) =>
        new(
            new ProjectileStateUpdate(
                projectile.Type, projectile.Spawner, projectile.PositionX, projectile.PositionY,
                projectile.VelocityX, projectile.VelocityY, projectile.Ai,
                projectile.BannerIdToRespondTo, projectile.Damage, projectile.KnockBack, projectile.OriginalDamage),
            599,
            LocalAi: new ProjectileLocalAiState(0f, 0f, frameCounter));

    private static ProjectileSnapshot Create(ProjectileHandle handle, float positionX, float positionY) =>
        new(
            handle,
            new ProjectileRevision(1),
            VanillaProjectileIds.CultistBossLightningOrbArc,
            VanillaProjectileOwnership.ServerOwner,
            positionX,
            positionY,
            7f,
            0f,
            new ProjectileAiState(0f, 1f, 0f),
            0,
            60,
            0f,
            60);
}
