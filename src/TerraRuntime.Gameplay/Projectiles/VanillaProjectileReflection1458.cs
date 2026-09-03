using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Projectiles;

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
/// The admitted projectile catalog proves the currently runtime-owned aiStyle 1/2 identities plus the
/// source-special Super Star / Star Cannon Star identities. Presentation reflection effects remain client-owned.
/// </summary>
public static class VanillaProjectileReflection1458
{
    public static bool CanBeReflected(
        in ProjectileSnapshot projectile,
        bool alreadyReflected,
        in VanillaProjectileDefinition definition) =>
        projectile.IsActive &&
        VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) &&
        projectile.Damage > 0 &&
        !alreadyReflected &&
        (projectile.Type == VanillaProjectileIds.SuperStar ||
         projectile.Type == VanillaProjectileIds.StarCannonStar ||
         definition.AiStyle == VanillaProjectileAiStyles.Arrow ||
         definition.AiStyle == VanillaProjectileAiStyles.Thrown);

    /// <summary>
    /// TerrariaServer 1.4.5.8 NPCID.Sets.ReflectStarShotsInForTheWorthy. Keeping the complete pinned set here
    /// prevents future boss admission from silently changing Good World reflection semantics.
    /// </summary>
    public static bool ReflectsStarShotsInGoodWorld(NpcTypeId npcType) =>
        npcType.Value is
            4 or 5 or 13 or 14 or 15 or 266 or 267 or 35 or 36 or
            113 or 114 or 115 or 116 or 117 or 118 or 119 or
            125 or 126 or 134 or 135 or 136 or 139 or 127 or 128 or 131 or 129 or 130 or
            262 or 263 or 264 or 245 or 247 or 248 or 246 or 249 or
            398 or 400 or 397 or 396 or 401;

    public static bool IsGoodWorldStarShot(ProjectileTypeId projectileType) =>
        projectileType == VanillaProjectileIds.SuperStar ||
        projectileType == VanillaProjectileIds.StarCannonStar;

    public static bool TryResolve(
        in ProjectileSnapshot projectile,
        float oldVelocityX,
        float oldVelocityY,
        float ownerCenterX,
        float ownerCenterY,
        IVanillaProjectileReflectionRandom random,
        out VanillaProjectileReflectionResult result)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!float.IsFinite(ownerCenterX) ||
            !float.IsFinite(ownerCenterY) ||
            !float.IsFinite(oldVelocityX) ||
            !float.IsFinite(oldVelocityY) ||
            projectile.Damage <= 0)
        {
            result = default;
            return false;
        }

        float oldSpeed = Length(oldVelocityX, oldVelocityY);
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
