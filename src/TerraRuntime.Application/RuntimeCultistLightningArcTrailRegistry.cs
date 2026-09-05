using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Retains the source 20-position TrailCacheLength for Cultist lightning arcs. The registry is world-local,
/// generation-scoped and updated only after a complete projectile simulation commit, so speculative subupdates
/// and reused physical slots cannot leak collision geometry into authoritative player combat.
/// </summary>
internal sealed class RuntimeCultistLightningArcTrailRegistry : IProjectileSimulationCommitSink
{
    public const int TrailLength = 20;

    private readonly ProjectileGeneration[] generations;
    private readonly byte[] counts;
    private readonly float[] positionXs;
    private readonly float[] positionYs;

    public RuntimeCultistLightningArcTrailRegistry(int projectileCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectileCapacity);
        generations = new ProjectileGeneration[projectileCapacity];
        counts = new byte[projectileCapacity];
        positionXs = new float[checked(projectileCapacity * TrailLength)];
        positionYs = new float[positionXs.Length];
    }

    public void ProjectileSimulationCommitted(
        in ProjectileSnapshot initialProjectile,
        in ProjectileLifecycleState initialLifecycle,
        ReadOnlySpan<ProjectileSimulationStepResult> subupdates,
        in ProjectileSnapshot finalProjectile,
        bool expired)
    {
        if (initialProjectile.Type != VanillaProjectileIds.CultistBossLightningOrbArc ||
            !initialProjectile.Handle.IsAssigned ||
            initialProjectile.Handle.Slot >= generations.Length)
        {
            return;
        }

        int slot = initialProjectile.Handle.Slot;
        if (generations[slot] != initialProjectile.Handle.Generation)
        {
            generations[slot] = initialProjectile.Handle.Generation;
            counts[slot] = 0;
        }

        float positionX = initialProjectile.PositionX;
        float positionY = initialProjectile.PositionY;
        ProjectileLocalAiState localAi = initialLifecycle.LocalAi;
        for (int i = 0; i < subupdates.Length; i++)
        {
            // Projectile.Update shifts TrailingMode 1 before AI whenever frameCounter is zero. The initial
            // empty trail is also primed immediately, matching oldPos[0] == Vector2.Zero in the source.
            if (localAi.Ai2 == 0f || counts[slot] == 0)
                Push(slot, positionX, positionY);

            ProjectileSimulationStepResult step = subupdates[i];
            positionX = step.State.PositionX;
            positionY = step.State.PositionY;
            localAi = step.LocalAi ?? localAi;
        }

        if (expired)
            counts[slot] = 0;
    }

    public bool Intersects(
        ProjectileHandle projectile,
        int width,
        int height,
        float targetLeft,
        float targetTop,
        float targetWidth,
        float targetHeight)
    {
        if (!projectile.IsAssigned || projectile.Slot >= generations.Length ||
            generations[projectile.Slot] != projectile.Generation)
        {
            return false;
        }

        int slot = projectile.Slot;
        int offset = checked(slot * TrailLength);
        for (int i = 0; i < counts[slot]; i++)
        {
            // Projectile.Colliding truncates oldPos coordinates into the integer projectile rectangle.
            float left = (int)positionXs[offset + i];
            float top = (int)positionYs[offset + i];
            if (left < targetLeft + targetWidth && left + width > targetLeft &&
                top < targetTop + targetHeight && top + height > targetTop)
            {
                return true;
            }
        }

        return false;
    }

    private void Push(int slot, float positionX, float positionY)
    {
        if ((positionX == 0f && positionY == 0f) || !float.IsFinite(positionX) || !float.IsFinite(positionY))
            return;

        int offset = checked(slot * TrailLength);
        int count = counts[slot];
        int copied = Math.Min(count, TrailLength - 1);
        if (copied > 0)
        {
            Array.Copy(positionXs, offset, positionXs, offset + 1, copied);
            Array.Copy(positionYs, offset, positionYs, offset + 1, copied);
        }
        positionXs[offset] = positionX;
        positionYs[offset] = positionY;
        if (count < TrailLength)
            counts[slot] = checked((byte)(count + 1));
    }
}

/// <summary>Fixed world-owned commit fanout for live-child staging and exceptional projectile trail state.</summary>
internal sealed class RuntimeProjectileSimulationCommitSink(
    RuntimeProjectileLiveChildSpawnQueue liveChildren,
    RuntimeCultistLightningArcTrailRegistry lightningTrails) : IProjectileSimulationCommitSink
{
    public void ProjectileSimulationCommitted(
        in ProjectileSnapshot initialProjectile,
        in ProjectileLifecycleState initialLifecycle,
        ReadOnlySpan<ProjectileSimulationStepResult> subupdates,
        in ProjectileSnapshot finalProjectile,
        bool expired)
    {
        lightningTrails.ProjectileSimulationCommitted(
            in initialProjectile, in initialLifecycle, subupdates, in finalProjectile, expired);
        liveChildren.ProjectileSimulationCommitted(
            in initialProjectile, in initialLifecycle, subupdates, in finalProjectile, expired);
    }
}
