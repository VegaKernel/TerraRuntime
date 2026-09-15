namespace TerraRuntime.Gameplay.Npcs;

/// <summary>TerrariaServer 1.4.5.8 AI_011 skull aim, including the original Windows x86 arithmetic boundaries.</summary>
public static class VanillaSkeletronSkull
{
    public static void Aim(ref float x, ref float y, float speed, int jitterX, int jitterY, bool windowsArithmetic)
    {
        // The Windows CLR retains wider products for the sum passed to Math.Sqrt. The scalar
        // distance, multiplier and aimed components are rounded before the subsequent RNG calls.
        double squared = windowsArithmetic ? (double)x * x + (double)y * y : x * x + y * y;
        float distance = (float)Math.Sqrt(squared);
        if (distance <= 0f) distance = 1f;
        float multiplier = speed / distance;
        x *= multiplier;
        y *= multiplier;

        if (windowsArithmetic)
        {
            // XNA's field stores round to Single; its reciprocal remains wider until those stores.
            // Keep the original Single 0.01 constant even when evaluating the jitter in Double.
            x = (float)(x + jitterX * (double)0.01f);
            y = (float)(y + jitterY * (double)0.01f);
            double inverse = 1d / (float)Math.Sqrt((double)x * x + (double)y * y);
            x = (float)(x * inverse);
            y = (float)(y * inverse);
        }
        else
        {
            x += jitterX * 0.01f;
            y += jitterY * 0.01f;
            float inverse = 1f / (float)Math.Sqrt(x * x + y * y);
            x *= inverse;
            y *= inverse;
        }
        x *= speed;
        y *= speed;
    }
}
