using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Source-ordered child-projectile spawns for the admitted TerrariaServer 1.4.5.8 combat slice. Server-created
/// children are immediately generation-bound to their authoritative owner so packet 27/29 cannot rewrite them.
/// </summary>
internal static class RuntimeProjectileChildSpawn1458
{
    public static bool TrySpawnConfettiMelee(
        RuntimeProjectileStore projectiles,
        PlayerHandle owner,
        float targetCenterX,
        float targetCenterY,
        float targetVelocityX,
        float targetVelocityY,
        out ProjectileSnapshot spawned)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if (!owner.IsAssigned ||
            !float.IsFinite(targetCenterX) || !float.IsFinite(targetCenterY) ||
            !float.IsFinite(targetVelocityX) || !float.IsFinite(targetVelocityY))
        {
            spawned = default;
            return false;
        }

        // Projectile.NewProjectile receives a center point. SetDefaults(289) gives a 10x10 projectile, while
        // RuntimeProjectileStore stores top-left coordinates. Damage/knockback are exactly zero in vanilla.
        const float HalfSize = 5f;
        var state = new ProjectileStateUpdate(
            VanillaProjectileIds.ConfettiMelee,
            owner.Slot.Value,
            targetCenterX - HalfSize,
            targetCenterY - HalfSize,
            targetVelocityX,
            targetVelocityY,
            default,
            BannerIdToRespondTo: 0,
            Damage: 0,
            KnockBack: 0f,
            OriginalDamage: 0);

        if (!projectiles.TrySpawnVanilla(in state, out spawned))
            return false;

        if (projectiles.TryMarkCombatTrusted(spawned.Handle, owner))
            return true;

        projectiles.TryDespawn(spawned.Handle, out _);
        spawned = default;
        return false;
    }
    public static bool TrySpawnSuperStarSlash(
        RuntimeProjectileStore projectiles,
        PlayerHandle owner,
        float targetCenterX,
        float targetCenterY,
        short parentDamage,
        Random random,
        out ProjectileSnapshot spawned)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        ArgumentNullException.ThrowIfNull(random);
        if (!owner.IsAssigned || parentDamage <= 0 ||
            !float.IsFinite(targetCenterX) || !float.IsFinite(targetCenterY))
        {
            spawned = default;
            return false;
        }

        // Utils.NextVector2CircularEdge(200,200): a unit vector on the ellipse edge. SummonSuperStarSlash
        // folds negative Y downward, adds 100 Y, normalizes to speed 6, then spawns 20 velocity-lengths
        // behind the target. SetDefaults(729) is 20x20, so convert its center to store top-left coordinates.
        float angle = (float)random.NextDouble() * MathF.Tau;
        float edgeX = MathF.Cos(angle) * 200f;
        float edgeY = MathF.Sin(angle) * 200f;
        if (edgeY < 0f)
            edgeY = -edgeY;
        edgeY += 100f;
        float length = MathF.Sqrt(edgeX * edgeX + edgeY * edgeY);
        float velocityX = edgeX / length * 6f;
        float velocityY = edgeY / length * 6f;
        float spawnCenterX = targetCenterX - velocityX * 20f;
        float spawnCenterY = targetCenterY - velocityY * 20f;
        short childDamage = (short)(parentDamage * 0.75);

        const float HalfSize = 10f;
        var state = new ProjectileStateUpdate(
            VanillaProjectileIds.SuperStarSlash,
            owner.Slot.Value,
            spawnCenterX - HalfSize,
            spawnCenterY - HalfSize,
            velocityX,
            velocityY,
            new ProjectileAiState(0f, targetCenterY, 0f),
            BannerIdToRespondTo: 0,
            Damage: childDamage,
            KnockBack: 0f,
            OriginalDamage: childDamage);

        if (!projectiles.TrySpawnVanilla(in state, out spawned))
            return false;
        if (projectiles.TryMarkCombatTrusted(spawned.Handle, owner))
            return true;

        projectiles.TryDespawn(spawned.Handle, out _);
        spawned = default;
        return false;
    }

}
