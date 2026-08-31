namespace TerraRuntime.Core;

internal readonly record struct VanillaNpcKnockbackResult(float VelocityX, float VelocityY);

/// <summary>
/// Clean-room implementation of the ordinary knockback branches in TerrariaServer 1.4.5.8
/// <c>NPC.StrikeNPC_Inner</c>. Source-specific knockback changes such as On Fire! 2 and the
/// type-185 vertical multiplier remain upstream until their state is represented authoritatively.
/// </summary>
internal static class VanillaNpcKnockbackResolver
{
    public static VanillaNpcKnockbackResult Resolve(
        float velocityX,
        float velocityY,
        bool noGravity,
        int lifeMax,
        float knockBackResist,
        float knockBack,
        int hitDirection,
        int resolvedDamage,
        bool critical,
        bool expertMode)
    {
        if (knockBack <= 0f || knockBackResist <= 0f)
            return new VanillaNpcKnockbackResult(velocityX, velocityY);

        float strength = ApplyVanillaSoftCaps(knockBack * knockBackResist);
        if (critical)
            strength *= 1.4f;

        long staggerThreshold = (long)resolvedDamage * (expertMode ? 15 : 10);
        if (staggerThreshold > lifeMax)
        {
            velocityX = ApplyStrongHorizontalKnockback(velocityX, strength, hitDirection);
            float verticalImpulse = strength * (noGravity ? -0.5f : -0.75f);
            if (velocityY > verticalImpulse)
            {
                velocityY += verticalImpulse;
                if (velocityY < verticalImpulse)
                    velocityY = verticalImpulse;
            }
        }
        else
        {
            float verticalFactor = noGravity ? -0.5f : -0.75f;
            velocityY = strength * verticalFactor * knockBackResist;
            velocityX = strength * hitDirection * knockBackResist;
        }

        return new VanillaNpcKnockbackResult(velocityX, velocityY);
    }

    private static float ApplyVanillaSoftCaps(float strength)
    {
        strength = SoftenAbove(strength, 8f, 0.9f);
        strength = SoftenAbove(strength, 10f, 0.8f);
        strength = SoftenAbove(strength, 12f, 0.7f);
        strength = SoftenAbove(strength, 14f, 0.6f);
        return Math.Min(strength, 16f);
    }

    private static float SoftenAbove(float value, float threshold, float factor) =>
        value > threshold
            ? threshold + (value - threshold) * factor
            : value;

    private static float ApplyStrongHorizontalKnockback(
        float velocityX,
        float strength,
        int hitDirection)
    {
        if (hitDirection < 0 && velocityX > -strength)
        {
            if (velocityX > 0f)
                velocityX -= strength;

            velocityX -= strength;
            if (velocityX < -strength)
                velocityX = -strength;
        }
        else if (hitDirection > 0 && velocityX < strength)
        {
            if (velocityX < 0f)
                velocityX += strength;

            velocityX += strength;
            if (velocityX > strength)
                velocityX = strength;
        }

        return velocityX;
    }
}
