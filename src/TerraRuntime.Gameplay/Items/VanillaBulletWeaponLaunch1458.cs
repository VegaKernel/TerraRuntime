using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>A projectile's velocity after ItemCheck_Shoot spread and NewProjectile aiStyle-1 damping.</summary>
public readonly record struct VanillaBulletLaunchShot1458(float VelocityX, float VelocityY);

/// <summary>
/// Ordered ordinary bullet launch facts from TerrariaServer 1.4.5.8. The caller owns aim, ammunition,
/// RNG checkpoints and allocation. This helper neither publishes projectiles nor consumes live inventory.
/// </summary>
public static class VanillaBulletWeaponLaunch1458
{
    public const int MaximumShotCount = 8;
    private const int MaximumDampingSteps = 64;

    public static bool Supports(ItemTypeId weapon) => weapon.Value is 98 or 219 or 533 or 1929 or 964 or 534 or 679 or 4703;

    public static bool TryPlanFromAim(
        ItemTypeId weapon,
        float aimDeltaX,
        float aimDeltaY,
        int facingDirection,
        float shootSpeed,
        ProjectileTypeId projectile,
        Func<int, int, int> next,
        Func<double> nextDouble,
        Span<VanillaBulletLaunchShot1458> shots,
        out int count)
    {
        count = 0;
        if (!IsValid(weapon, shootSpeed, projectile, shots) || facingDirection is not (-1 or 1) ||
            !float.IsFinite(aimDeltaX) || !float.IsFinite(aimDeltaY))
            return false;
        float distance = Length(aimDeltaX, aimDeltaY);
        if (!float.IsFinite(distance)) return false;
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(nextDouble);

        float scale;
        if (distance == 0f)
        {
            aimDeltaX = facingDirection;
            aimDeltaY = 0f;
            scale = shootSpeed;
        }
        else scale = shootSpeed / distance;

        // Chain Gun adds jitter to the raw aim divided by the normalization factor, then multiplies.
        // Keeping that operation order preserves source float rounding; it is not a second normalization.
        if (weapon == VanillaItemIds.ChainGun)
        {
            aimDeltaX += next(-50, 51) * 0.03f / scale;
            aimDeltaY += next(-50, 51) * 0.03f / scale;
        }
        float velocityX = aimDeltaX * scale;
        float velocityY = aimDeltaY * scale;
        count = GetCount(weapon, next);
        Fill(weapon, velocityX, velocityY, next, nextDouble, shots[..count]);
        return true;
    }

    /// <summary>
    /// Recovers bounded aim intent from the first published child's velocity and the detached launch cursor.
    /// Raw MouseWorld distance is not recoverable from packet27. Magnitude acceptance is limited to source
    /// float representation error; this is not ownership of the client's original aim or RNG stream.
    /// </summary>
    public static bool TryResolveFromFirstVelocity(
        ItemTypeId weapon,
        float firstVelocityX,
        float firstVelocityY,
        float shootSpeed,
        ProjectileTypeId projectile,
        Func<int, int, int> next,
        Func<double> nextDouble,
        Span<VanillaBulletLaunchShot1458> shots,
        out int count)
    {
        count = 0;
        if (!IsValid(weapon, shootSpeed, projectile, shots) ||
            !float.IsFinite(firstVelocityX) || !float.IsFinite(firstVelocityY))
            return false;
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(nextDouble);

        float earlyX = 0f;
        float earlyY = 0f;
        if (weapon == VanillaItemIds.ChainGun)
        {
            earlyX = next(-50, 51) * 0.03f;
            earlyY = next(-50, 51) * 0.03f;
        }
        int plannedCount = GetCount(weapon, next);
        GetJitter(weapon, out int minimum, out int maximum, out float strength);
        float jitterX = strength == 0f ? 0f : next(minimum, maximum) * strength;
        float jitterY = strength == 0f ? 0f : next(minimum, maximum) * strength;
        if (!TryRecoverBase(firstVelocityX, firstVelocityY, shootSpeed, earlyX, earlyY,
                jitterX, jitterY, out float baseX, out float baseY))
            return false;

        // The first child already consumed its offers. Subsequent children use the same retained base aim.
        float velocityX = baseX + earlyX;
        float velocityY = baseY + earlyY;
        shots[0] = Damp(velocityX + jitterX, velocityY + jitterY);
        Fill(weapon, velocityX, velocityY, next, nextDouble, shots[1..plannedCount], firstChild: false);
        count = plannedCount;
        return true;
    }

    private static bool IsValid(ItemTypeId weapon, float speed, ProjectileTypeId projectile,
        Span<VanillaBulletLaunchShot1458> shots) =>
        Supports(weapon) && projectile.Value is 14 or 15 &&
        float.IsFinite(speed) && speed > 0f && speed <= 64f && shots.Length >= MaximumShotCount;

    private static int GetCount(ItemTypeId weapon, Func<int, int, int> next) => weapon.Value switch
    {
        534 => next(4, 6),
        964 => next(3, 5),
        679 => 6,
        4703 => 8,
        _ => 1
    };

    private static void GetJitter(ItemTypeId weapon, out int minimum, out int maximum, out float strength)
    {
        (minimum, maximum, strength) = weapon.Value switch
        {
            98 or 533 => (-40, 41, 0.01f),
            1929 => (-40, 41, 0.03f),
            534 or 679 => (-40, 41, 0.05f),
            964 => (-35, 36, 0.04f),
            _ => (0, 0, 0f)
        };
    }

    private static void Fill(ItemTypeId weapon, float baseX, float baseY,
        Func<int, int, int> next, Func<double> nextDouble,
        Span<VanillaBulletLaunchShot1458> shots, bool firstChild = true)
    {
        GetJitter(weapon, out int minimum, out int maximum, out float strength);
        for (int index = 0; index < shots.Length; index++)
        {
            float x = baseX;
            float y = baseY;
            if (weapon == VanillaItemIds.QuadBarrelShotgun)
            {
                if (!firstChild || index != 0)
                {
                    float length = Length(x, y);
                    Normalize(ref x, ref y);
                    float angle = ((float)Math.PI / 2f) * (float)nextDouble();
                    float cosine = (float)Math.Cos(angle);
                    float sine = (float)Math.Sin(angle);
                    float rotatedX = x * cosine - y * sine;
                    float rotatedY = x * sine + y * cosine;
                    float displacement = (float)nextDouble() * 2f - 1f;
                    x = baseX + rotatedX * displacement * 5f;
                    y = baseY + rotatedY * displacement * 5f;
                    Normalize(ref x, ref y);
                    x *= length;
                    y *= length;
                    x += next(-40, 41) * 0.05f;
                    y += next(-40, 41) * 0.05f;
                }
            }
            else if (strength != 0f)
            {
                x += next(minimum, maximum) * strength;
                y += next(minimum, maximum) * strength;
            }
            shots[index] = Damp(x, y);
        }
    }

    private static bool TryRecoverBase(float firstX, float firstY, float speed,
        float earlyX, float earlyY, float jitterX, float jitterY, out float x, out float y)
    {
        x = y = 0f;
        float tolerance = MathF.Max(0.0005f, speed * 0.00001f);
        float publishedX = firstX;
        float publishedY = firstY;
        for (int depth = 0; depth <= MaximumDampingSteps; depth++)
        {
            float candidateX = firstX - jitterX - earlyX;
            float candidateY = firstY - jitterY - earlyY;
            var regenerated = Damp(firstX, firstY);
            if (MathF.Abs(Length(candidateX, candidateY) - speed) <= tolerance &&
                MathF.Abs(regenerated.VelocityX - publishedX) <= tolerance &&
                MathF.Abs(regenerated.VelocityY - publishedY) <= tolerance)
            {
                x = candidateX;
                y = candidateY;
                return true;
            }
            firstX /= 0.97f;
            firstY /= 0.97f;
            if (!float.IsFinite(firstX) || !float.IsFinite(firstY)) break;
        }
        return false;
    }

    private static VanillaBulletLaunchShot1458 Damp(float x, float y)
    {
        // Projectile.NewProjectile aiStyle1 uses asymmetric strictness at Y=-16, retained verbatim as data.
        while (x >= 16f || x <= -16f || y >= 16f || y < -16f)
        {
            x *= 0.97f;
            y *= 0.97f;
        }
        return new(x, y);
    }

    private static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);

    private static void Normalize(ref float x, ref float y)
    {
        float length = Length(x, y);
        if (length == 0f) { x = y = 0f; return; }
        float inverse = 1f / length;
        x *= inverse;
        y *= inverse;
    }
}
