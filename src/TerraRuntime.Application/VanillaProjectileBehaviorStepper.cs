using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Immutable world-independent inputs consumed by vanilla projectile behavior. Weather is supplied explicitly
/// by the runtime so AI code does not reach into world/global state.
/// </summary>
internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength,
    IRuntimePlayerSlotSnapshotLookup? PlayerSnapshots = null);

/// <summary>State produced by one supported vanilla projectile AI-family step before world motion/collision.</summary>
internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0,
    float? Ai1Override = null);

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile behavior that is independent of tile/world queries.
/// Runtime behavior-family selection is explicit in <see cref="VanillaProjectileBehaviorProfileCatalog"/> so
/// equal aiStyle values never silently opt unrelated projectile types into the same implementation.
/// World collision, liquids, post-AI wind and lifetime/kill handling remain owned by the world-motion layer.
/// </summary>
internal static class VanillaProjectileBehaviorStepper
{
    private const float MaximumThrownFallSpeed = 32f;
    private const float MaximumArrowFallSpeed = 16f;

    public static bool TryStep(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        if (!VanillaProjectileBehaviorProfileCatalog.TryGet(current.Type, out VanillaProjectileBehaviorProfile profile))
        {
            next = default;
            return false;
        }

        return TryStep(in current, in definition, in profile, in context, out next);
    }

    public static bool TryStep(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorProfile profile,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        if (!profile.BehaviorImplemented || definition.AiStyle != profile.ExpectedAiStyle)
        {
            next = default;
            return false;
        }

        if (profile.RejectServerOwned && VanillaProjectileOwnership.IsServerOwned(current.Spawner))
        {
            next = default;
            return false;
        }

        if (profile.RequiresDefaultAi2 && current.Ai.Ai2 != 0f)
        {
            next = default;
            return false;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;
        float? ai1Override = null;

        switch (profile.Family)
        {
            case VanillaProjectileBehaviorFamily.Thrown:
                // TerrariaServer 1.4.5.8 AI(), aiStyle == 2.
                if (context.WindPhysics)
                    velocityX += context.WindSpeedCurrent * context.WindPhysicsStrength;

                ai0 += 1f;
                if (ai0 >= 20f)
                {
                    velocityY += 0.4f;
                    velocityX *= 0.97f;
                }

                if (velocityY > MaximumThrownFallSpeed)
                    velocityY = MaximumThrownFallSpeed;
                break;

            case VanillaProjectileBehaviorFamily.BasicArrow:
                // TerrariaServer 1.4.5.8 Projectile.AI_001(), source-backed basic aiStyle-1 path.
                ai0 += 1f;
                if (ai0 >= 15f)
                {
                    ai0 = 15f;
                    velocityY += 0.1f;
                }

                if (velocityY > MaximumArrowFallSpeed)
                    velocityY = MaximumArrowFallSpeed;
                break;

            case VanillaProjectileBehaviorFamily.SkeletronSkull:
                float ai1 = current.Ai.Ai1 + 1f;
                ai1Override = ai1;
                float speed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                if (ai1 > 30f && ai1 < 110f && speed > 0f &&
                    TryFindClosestPlayer(in current, in definition, context.PlayerSnapshots, out float targetX, out float targetY))
                {
                    float centerX = current.PositionX + definition.Width * 0.5f;
                    float centerY = current.PositionY + definition.Height * 0.5f;
                    float dx = targetX - centerX;
                    float dy = targetY - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > 0f)
                    {
                        float desiredX = dx / distance * speed;
                        float desiredY = dy / distance * speed;
                        velocityX = (velocityX * 24f + desiredX) / 25f;
                        velocityY = (velocityY * 24f + desiredY) / 25f;
                        float blendedSpeed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                        if (blendedSpeed > 0f)
                        {
                            velocityX = velocityX / blendedSpeed * speed;
                            velocityY = velocityY / blendedSpeed * speed;
                        }
                    }
                }

                if (MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) < 18f)
                {
                    velocityX *= 1.02f;
                    velocityY *= 1.02f;
                }
                break;

            default:
                next = default;
                return false;
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, ai1Override);
        return true;
    }

    private static bool TryFindClosestPlayer(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        IRuntimePlayerSlotSnapshotLookup? players,
        out float centerX,
        out float centerY)
    {
        centerX = 0f;
        centerY = 0f;
        if (players is null)
            return false;

        float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + definition.Height * 0.5f;
        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;
        for (int rawSlot = 0; rawSlot < byte.MaxValue; rawSlot++)
        {
            var slot = new PlayerSlotId(checked((byte)rawSlot));
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player) || player.IsDead)
                continue;

            float playerCenterX = player.PositionX + 10f;
            float playerCenterY = player.PositionY + 21f;
            float dx = playerCenterX - projectileCenterX;
            float dy = playerCenterY - projectileCenterY;
            float distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            centerX = playerCenterX;
            centerY = playerCenterY;
            found = true;
        }
        return found;
    }
}
