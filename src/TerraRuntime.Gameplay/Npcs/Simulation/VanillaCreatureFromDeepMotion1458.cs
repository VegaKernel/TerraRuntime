namespace TerraRuntime.Gameplay.Npcs;

/// <summary>AI_003 water steering, exit impulse and dry knockback curve for type 461, TerrariaServer 1.4.5.8.</summary>
public static class VanillaCreatureFromDeepMotion1458
{
    public const float SwimmingClock = -.10101f;

    public static (float X, float Y) Steer(float velocityX, float velocityY, float targetDx, float targetDy, int direction, bool visible)
    {
        float speed = visible ? 5f : velocityY > 0f ? 3f : velocityY < 0f ? 8f : 5f;
        var desired = Normalize(visible ? targetDx : direction, visible ? targetDy : -1f, speed);
        float weight = visible ? 19f : speed < 5f ? 24f : 9f;
        return ((velocityX * weight + desired.X) / (weight + 1f),
                (velocityY * weight + desired.Y) / (weight + 1f));
    }

    public static (float X, float Y) ExitVelocity(float velocityX, float velocityY)
    {
        float speed = (float)Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
        return Normalize(velocityX, velocityY, MathF.Min(speed * 2f, 10f));
    }

    public static float DryKnockback(float difficulty)
    {
        // GameDifficultyData.KnockbackToEnemiesMultiplier is a clamped Classic-to-Master linear curve.
        float multiplier = (Math.Clamp(difficulty, 1f, 3f) - 1f) * (.8f - 1f) / 2f + 1f;
        return .4f * multiplier;
    }

    private static (float X, float Y) Normalize(float x, float y, float speed)
    {
        // Original FNA Vector2.Normalize multiplies by the reciprocal of its single-precision length.
        float reciprocal = 1f / (float)Math.Sqrt(x * x + y * y);
        return (x * reciprocal * speed, y * reciprocal * speed);
    }
}
