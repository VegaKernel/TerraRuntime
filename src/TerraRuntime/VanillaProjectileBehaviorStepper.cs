using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Immutable world-independent inputs consumed by vanilla projectile behavior. Weather is supplied explicitly
/// by the runtime so AI code does not reach into world/global state.
/// </summary>
internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength);

/// <summary>State produced by one supported vanilla projectile AI-family step before world motion/collision.</summary>
internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0);

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

            default:
                next = default;
                return false;
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0);
        return true;
    }
}
