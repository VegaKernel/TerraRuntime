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
/// Source-backed TerrariaServer 1.4.5.8 projectile AI-family behavior that is independent of tile/world queries.
/// World collision, liquids, post-AI wind and lifetime/kill handling are deliberately owned by the world-motion
/// layer so adding another AI family does not also make that family own physics or replication concerns.
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
        // Green Laser type 20 has an owner-gated AI_001 branch. On a dedicated server owner 255 equals
        // Main.myPlayer, so vanilla mutates knockBack/localAI and later damage/penetrate via RNG. Those lifecycle
        // fields are not yet modeled here; rejecting server-owned type 20 prevents silent authoritative divergence.
        if (current.Type == VanillaProjectileIds.GreenLaser &&
            VanillaProjectileOwnership.IsServerOwned(current.Spawner))
        {
            next = default;
            return false;
        }

        bool isThrown = definition.AiStyle == VanillaProjectileAiStyles.Thrown;
        bool isBasicArrow = definition.AiStyle == VanillaProjectileAiStyles.Arrow && IsBasicArrowFamily(current.Type);
        if (!isThrown && !isBasicArrow)
        {
            next = default;
            return false;
        }

        // AI_001 uses ai[2] as a feature selector for several special families. The currently source-backed
        // ordinary arrow/bullet path has ai[2] == 0; non-default feature state remains a separate behavior slice.
        if (isBasicArrow && current.Ai.Ai2 != 0f)
        {
            next = default;
            return false;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;

        if (isThrown)
        {
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
        }
        else
        {
            // TerrariaServer 1.4.5.8 Projectile.AI_001(), source-backed basic aiStyle-1 path.
            ai0 += 1f;
            if (ai0 >= 15f)
            {
                ai0 = 15f;
                velocityY += 0.1f;
            }

            if (velocityY > MaximumArrowFallSpeed)
                velocityY = MaximumArrowFallSpeed;
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0);
        return true;
    }

    internal static bool IsBasicArrowFamily(ProjectileTypeId type) =>
        type == VanillaProjectileIds.WoodenArrowFriendly ||
        type == VanillaProjectileIds.FireArrow ||
        type == VanillaProjectileIds.UnholyArrow ||
        type == VanillaProjectileIds.JestersArrow ||
        type == VanillaProjectileIds.Bullet ||
        type == VanillaProjectileIds.Seed ||
        type == VanillaProjectileIds.ConfettiGun ||
        type == VanillaProjectileIds.ConfettiMelee ||
        type == VanillaProjectileIds.BoneArrowFromMerchant ||
        type == VanillaProjectileIds.SoundGun ||
        type == VanillaProjectileIds.BoneShard ||
        type == VanillaProjectileIds.GreenLaser;
}
