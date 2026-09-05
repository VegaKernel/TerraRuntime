using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileLiveChildSpawnQueueTests
{
    [Fact]
    public void Sharknado_segment_commit_queues_child_and_sharkron_with_source_ai64_geometry()
    {
        var queue = new RuntimeProjectileLiveChildSpawnQueue(4);
        ProjectileSnapshot initial = CreateProjectile(
            VanillaProjectileIds.Sharknado, positionX: 145f, positionY: 213f,
            velocityX: -0.01f, velocityY: 0f, ai0: 2f, ai1: 12f);
        ProjectileSnapshot final = initial with
        {
            Revision = new ProjectileRevision(6),
            Ai = initial.Ai with { Ai0 = 1f }
        };
        ProjectileLifecycleState lifecycle = new(540, false)
        {
            LocalAi = new ProjectileLocalAiState(1f, 78f, 21f)
        };
        ProjectileSimulationStepResult step = CreateStep(final, lifecycle.TimeLeft - 1);

        queue.ProjectileSimulationCommitted(in initial, in lifecycle, [step], in final, expired: false);

        Assert.Single(queue.Events.ToArray());
        RuntimeProjectileLiveChildSpawnEvent queued = queue.Events[0];
        Assert.True(RuntimeTornadoLiveChildSpawn1458.TryCreateIntents(
            in queued, out NpcAiProjectileIntent projectileIntent, out bool hasNpcIntent, out NpcAiSpawnIntent npcIntent));
        Assert.Equal(VanillaProjectileIds.Sharknado, projectileIntent.Type);
        Assert.Equal(new ProjectileAiState(10f, 11f, 0f), projectileIntent.InitialAi);
        Assert.Equal(-0.01f, projectileIntent.VelocityX, 5);
        Assert.True(hasNpcIntent);
        Assert.Equal(VanillaNpcIds.Sharkron, npcIntent.Type);
        Assert.Equal(-0.01f, npcIntent.VelocityX, 5);
        Assert.Equal(default, npcIntent.InitialAi);

        const float scale = 13f / 25f;
        const float nextScale = 14f / 25f;
        int currentWidth = (int)(150f * scale);
        int currentHeight = (int)(42f * scale);
        float centerX = initial.PositionX + currentWidth * 0.5f;
        float centerY = initial.PositionY + currentHeight * 0.5f;
        float childCenterY = centerY - 42f * scale * 0.5f - 42f * nextScale * 0.5f + 2f;
        Assert.Equal(centerX - 75f, projectileIntent.PositionX, 4);
        Assert.Equal(childCenterY - 21f, projectileIntent.PositionY, 4);
        Assert.Equal((int)centerX, npcIntent.BottomX);
        Assert.Equal((int)childCenterY, npcIntent.BottomY);
    }

    [Fact]
    public void Cthulunado_even_segment_spawns_sharkron2_with_parent_integer_width_ai()
    {
        var queue = new RuntimeProjectileLiveChildSpawnQueue(4);
        ProjectileSnapshot initial = CreateProjectile(
            VanillaProjectileIds.Cthulunado, positionX: 147f, positionY: 214f,
            velocityX: 0f, velocityY: 0f, ai0: 2f, ai1: 24f);
        ProjectileSnapshot final = initial with { Ai = initial.Ai with { Ai0 = 1f } };
        ProjectileLifecycleState lifecycle = new(840, false)
        {
            LocalAi = new ProjectileLocalAiState(1f, 56f, 15f)
        };
        ProjectileSimulationStepResult step = CreateStep(final, 839);

        queue.ProjectileSimulationCommitted(in initial, in lifecycle, [step], in final, expired: false);
        RuntimeProjectileLiveChildSpawnEvent queued = Assert.Single(queue.Events.ToArray());
        Assert.True(RuntimeTornadoLiveChildSpawn1458.TryCreateIntents(
            in queued, out NpcAiProjectileIntent projectileIntent, out bool hasNpcIntent, out NpcAiSpawnIntent npcIntent));

        Assert.Equal(VanillaProjectileIds.Cthulunado, projectileIntent.Type);
        Assert.Equal(new ProjectileAiState(10f, 23f, 0f), projectileIntent.InitialAi);
        Assert.True(hasNpcIntent);
        Assert.Equal(VanillaNpcIds.Sharkron2, npcIntent.Type);
        Assert.Equal(56f, npcIntent.InitialAi.Ai2, 5);
        Assert.Equal(-1.5f, npcIntent.InitialAi.Ai3, 5);
    }

    [Fact]
    public void Cultist_ice_mist_emits_at_30_boundary_using_pre_rotation_angle()
    {
        var queue = new RuntimeProjectileLiveChildSpawnQueue(4);
        ProjectileSnapshot initial = CreateProjectile(
            VanillaProjectileIds.CultistBossIceMist, positionX: 100f, positionY: 200f,
            velocityX: 0f, velocityY: 0f, ai0: 29f, ai1: 1f);
        ProjectileSnapshot final = initial with { Ai = initial.Ai with { Ai0 = 30f } };
        ProjectileLifecycleState lifecycle = new(300, false)
        {
            LocalAi = new ProjectileLocalAiState(0f, 1f, MathF.PI * 0.5f)
        };
        ProjectileSimulationStepResult step = CreateStep(final, 299);

        queue.ProjectileSimulationCommitted(in initial, in lifecycle, [step], in final, expired: false);
        RuntimeProjectileLiveChildSpawnEvent queued = Assert.Single(queue.Events.ToArray());
        Assert.True(RuntimeCultistIceMistLiveChildSpawn1458.TryCreateIntent(in queued, out NpcAiProjectileIntent intent));
        Assert.Equal(VanillaProjectileIds.CultistBossIceMist, intent.Type);
        Assert.Equal(100f, intent.PositionX, 5);
        Assert.Equal(200f, intent.PositionY, 5);
        Assert.InRange(MathF.Abs(intent.VelocityX), 0f, 0.00001f);
        Assert.Equal(1f, intent.VelocityY, 5);
        Assert.Equal(default, intent.InitialAi);
    }

    [Fact]
    public void Expired_or_non_boundary_transition_never_queues_live_child_work()
    {
        var queue = new RuntimeProjectileLiveChildSpawnQueue(4);
        ProjectileSnapshot initial = CreateProjectile(
            VanillaProjectileIds.CultistBossIceMist, positionX: 0f, positionY: 0f,
            velocityX: 0f, velocityY: 0f, ai0: 30f, ai1: 1f);
        ProjectileSnapshot final = initial with { Ai = initial.Ai with { Ai0 = 31f } };
        ProjectileLifecycleState lifecycle = new(300, false);
        ProjectileSimulationStepResult step = CreateStep(final, 299);

        queue.ProjectileSimulationCommitted(in initial, in lifecycle, [step], in final, expired: false);
        queue.ProjectileSimulationCommitted(in initial, in lifecycle, [step], in final, expired: true);
        Assert.Empty(queue.Events.ToArray());
    }

    private static ProjectileSimulationStepResult CreateStep(ProjectileSnapshot projectile, int timeLeft) =>
        new(
            new ProjectileStateUpdate(
                projectile.Type,
                projectile.Spawner,
                projectile.PositionX,
                projectile.PositionY,
                projectile.VelocityX,
                projectile.VelocityY,
                projectile.Ai,
                projectile.BannerIdToRespondTo,
                projectile.Damage,
                projectile.KnockBack,
                projectile.OriginalDamage),
            timeLeft);

    private static ProjectileSnapshot CreateProjectile(
        ProjectileTypeId type,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        float ai0,
        float ai1) =>
        new(
            new ProjectileHandle(8, new ProjectileGeneration(3)),
            new ProjectileRevision(5),
            type,
            VanillaProjectileOwnership.ServerOwner,
            positionX,
            positionY,
            velocityX,
            velocityY,
            new ProjectileAiState(ai0, ai1, 0f),
            BannerIdToRespondTo: 0,
            Damage: 60,
            KnockBack: 4f,
            OriginalDamage: 60);
}
