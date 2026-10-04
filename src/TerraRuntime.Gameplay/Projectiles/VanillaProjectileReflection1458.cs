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
    // Original SetDefaults sweep of all1135 positive identities; this is the source default flag/style set,
    // not an inference from owner or positive damage. Mutable friendly/hostile overrides remain lifecycle-owned.
    private static readonly ushort[] ReflectableDefaults =
    [
        1, 2, 3, 4, 5, 14, 15, 20, 21, 27, 36, 41, 48, 51, 54, 69,
        70, 76, 77, 78, 88, 89, 90, 91, 93, 94, 95, 103, 104, 117, 118, 119,
        120, 121, 122, 123, 124, 125, 126, 158, 159, 160, 161, 162, 166, 172, 195, 207,
        225, 242, 246, 248, 249, 253, 265, 267, 278, 279, 282, 283, 284, 285, 286, 287,
        304, 306, 309, 311, 312, 318, 323, 330, 336, 337, 344, 355, 357, 359, 370, 371,
        374, 376, 389, 408, 424, 425, 426, 440, 442, 459, 469, 474, 477, 478, 479, 484,
        485, 495, 497, 502, 504, 507, 510, 520, 521, 522, 532, 583, 585, 587, 589, 597,
        599, 601, 606, 616, 619, 620, 621, 634, 635, 638, 639, 640, 645, 660, 661, 664,
        666, 668, 680, 684, 706, 709, 710, 711, 712, 728, 731, 772, 819, 837, 861, 868,
        869, 876, 936, 937, 954, 955, 967, 968, 979, 981, 996, 1006, 1012, 1023, 1026, 1085,
        1097, 1099, 1111, 1114, 1120, 1124, 1134
    ];

    public static bool CanBeReflectedAtDefaults(ProjectileTypeId type) =>
        type.Value > 0 && type.Value <= ushort.MaxValue &&
        Array.BinarySearch(ReflectableDefaults, (ushort)type.Value) >= 0;

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

    /// <summary>Exact source arithmetic, including nonfinite original outcomes; retained callers reject them before live commit.</summary>
    public static VanillaProjectileReflectionResult ResolveSource(in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition body, float oldVelocityX, float oldVelocityY,
        float ownerCenterX, float ownerCenterY, IVanillaProjectileReflectionRandom random)
    {
        float towardX = ownerCenterX - (projectile.PositionX + body.Width * .5f);
        float towardY = ownerCenterY - (projectile.PositionY + body.Height * .5f);
        float inverse = 1f / Length(towardX, towardY);
        towardX *= inverse;
        towardY *= inverse;
        float oldSpeed = Length(oldVelocityX, oldVelocityY);
        towardX *= oldSpeed;
        towardY *= oldSpeed;
        float velocityX = random.NextInt32(-100, 101);
        float velocityY = random.NextInt32(-100, 101);
        inverse = 1f / Length(velocityX, velocityY);
        velocityX *= inverse;
        velocityY *= inverse;
        float speed = Length(towardX, towardY);
        velocityX *= speed;
        velocityY *= speed;
        velocityX += towardX * 20f;
        velocityY += towardY * 20f;
        inverse = 1f / Length(velocityX, velocityY);
        velocityX *= inverse;
        velocityY *= inverse;
        velocityX *= speed;
        velocityY *= speed;
        short damage = checked((short)(projectile.Damage / 2 / 2));
        return new(velocityX, velocityY, damage);
    }

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

        if (!VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition))
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
