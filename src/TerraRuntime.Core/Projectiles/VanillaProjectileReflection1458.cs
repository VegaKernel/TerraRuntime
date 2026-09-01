using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public interface IVanillaProjectileReflectionRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}

public readonly record struct VanillaProjectileReflectionResult(
    float VelocityX,
    float VelocityY,
    short Damage);

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.ReflectProjectile gameplay mutation without sound/dust presentation effects.
/// The currently admitted projectile catalog can prove reflectability for aiStyle 1/2; unsupported source
/// styles and special type 728/955 remain fail-closed until their definitions are admitted.
/// </summary>
public static class VanillaProjectileReflection1458
{
    public static bool CanBeReflected(
        in ProjectileSnapshot projectile,
        in ProjectileLifecycleState lifecycle,
        in VanillaProjectileDefinition definition) =>
        projectile.IsActive &&
        VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) &&
        projectile.Damage > 0 &&
        !lifecycle.Reflected &&
        (definition.AiStyle == VanillaProjectileAiStyles.Arrow ||
         definition.AiStyle == VanillaProjectileAiStyles.Thrown);

    public static bool TryResolve(
        in ProjectileSnapshot projectile,
        in ProjectileLifecycleState lifecycle,
        float ownerCenterX,
        float ownerCenterY,
        IVanillaProjectileReflectionRandom random,
        out VanillaProjectileReflectionResult result)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!float.IsFinite(ownerCenterX) ||
            !float.IsFinite(ownerCenterY) ||
            !float.IsFinite(lifecycle.OldVelocityX) ||
            !float.IsFinite(lifecycle.OldVelocityY) ||
            projectile.Damage <= 0)
        {
            result = default;
            return false;
        }

        float oldSpeed = Length(lifecycle.OldVelocityX, lifecycle.OldVelocityY);
        if (!float.IsFinite(oldSpeed) || oldSpeed <= float.Epsilon)
        {
            result = default;
            return false;
        }

        if (!VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition))
        {
            result = default;
            return false;
        }

        float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + definition.Height * 0.5f;
        float towardOwnerX = ownerCenterX - projectileCenterX;
        float towardOwnerY = ownerCenterY - projectileCenterY;
        if (!Normalize(ref towardOwnerX, ref towardOwnerY))
        {
            result = default;
            return false;
        }
        towardOwnerX *= oldSpeed;
        towardOwnerY *= oldSpeed;

        float velocityX = random.NextInt32(-100, 101);
        float velocityY = random.NextInt32(-100, 101);
        if (!Normalize(ref velocityX, ref velocityY))
        {
            result = default;
            return false;
        }
        velocityX *= oldSpeed;
        velocityY *= oldSpeed;
        velocityX += towardOwnerX * 20f;
        velocityY += towardOwnerY * 20f;
        if (!Normalize(ref velocityX, ref velocityY))
        {
            result = default;
            return false;
        }
        velocityX *= oldSpeed;
        velocityY *= oldSpeed;

        int damage = projectile.Damage;
        damage /= 2;
        damage /= 2;
        result = new VanillaProjectileReflectionResult(
            velocityX,
            velocityY,
            checked((short)damage));
        return true;
    }

    private static float Length(float x, float y) => MathF.Sqrt(x * x + y * y);

    private static bool Normalize(ref float x, ref float y)
    {
        float length = Length(x, y);
        if (!float.IsFinite(length) || length <= float.Epsilon)
            return false;
        float inverse = 1f / length;
        x *= inverse;
        y *= inverse;
        return float.IsFinite(x) && float.IsFinite(y);
    }
}
