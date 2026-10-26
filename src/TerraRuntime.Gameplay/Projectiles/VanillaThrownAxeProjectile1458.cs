namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct ThrownAxeState1458(float X, float Y, float VelocityX,
    float VelocityY, float Rotation, int Direction, int TimeLeft, int SoundDelay,
    float Ai0, float Ai1, bool TileCollide, bool Active, bool NetUpdate);

public readonly record struct ThrownAxeOwnerFacts1458(float X, float Y, int Width, int Height,
    bool UsesOwnerMeleeSpeed, bool OwnsCatch, float MeleeSpeed, bool WindowsArithmetic);

public readonly record struct ThrownAxeTransition1458(ThrownAxeState1458 State, bool SourceKill);

/// <summary>The retained1052 axe's AI3 branch; common movement, cutting and lifetime follow separately.</summary>
public static class VanillaThrownAxeProjectile1458
{
    public const int ProjectileType = 1052;
    public const int Width = 22;
    public const int Height = 22;

    public static bool TryFinalize(float x, float y, float velocityX, float velocityY,
        int retainedDirection, out ThrownAxeState1458 state)
    {
        state = default;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) || retainedDirection is not (-1 or 1)) return false;
        state = new(x, y, velocityX, velocityY, 0f, retainedDirection, 3600, 0,
            0f, 0f, true, true, false);
        return true;
    }

    public static bool TryStepAi(in ThrownAxeState1458 previous,
        in ThrownAxeOwnerFacts1458 owner, out ThrownAxeTransition1458 transition)
    {
        transition = default;
        if (!previous.Active || !float.IsFinite(previous.X) || !float.IsFinite(previous.Y) ||
            !float.IsFinite(previous.VelocityX) || !float.IsFinite(previous.VelocityY) ||
            !float.IsFinite(previous.Rotation) || !float.IsFinite(previous.Ai0) ||
            !float.IsFinite(previous.Ai1) || previous.Direction is not (-1 or 1) ||
            !float.IsFinite(owner.X) || !float.IsFinite(owner.Y) || owner.Width is < 1 or > 4096 ||
            owner.Height is < 1 or > 4096 || owner.UsesOwnerMeleeSpeed &&
                (!float.IsFinite(owner.MeleeSpeed) || owner.MeleeSpeed <= 0f)) return false;
        var state = previous;
        if (state.SoundDelay == 0) state = state with { SoundDelay = 8 };
        bool kill = false;
        if (state.Ai0 == 0f)
        {
            float age = state.Ai1 + 1f;
            state = state with { Ai1 = age };
            if (age >= 30f) state = state with { Ai0 = 1f, Ai1 = 0f, NetUpdate = true };
        }
        else
        {
            state = state with { TileCollide = false };
            float speed = 9f, acceleration = 0.4f;
            if (owner.UsesOwnerMeleeSpeed)
            {
                float inverse = 1f / owner.MeleeSpeed;
                float factor = 1f + (inverse - 1f) * 1.5f;
                factor = Math.Clamp(factor, 1f, 10f);
                speed *= factor;
                acceleration *= factor;
            }
            float dx = owner.X + owner.Width / 2 - (state.X + Width * 0.5f);
            float dy = owner.Y + owner.Height / 2 - (state.Y + Height * 0.5f);
            float length = Length(dx, dy, owner.WindowsArithmetic);
            if (length > 3000f)
            {
                kill = true;
                state = state with { Active = false, TimeLeft = 0 };
            }
            float ratio = speed / length;
            float targetX = dx * ratio, targetY = dy * ratio;
            float vx = Approach(state.VelocityX, targetX, acceleration);
            float vy = Approach(state.VelocityY, targetY, acceleration);
            state = state with { VelocityX = vx, VelocityY = vy };
            if (owner.OwnsCatch && Intersects((int)state.X, (int)state.Y, Width, Height,
                (int)owner.X, (int)owner.Y, owner.Width, owner.Height))
            {
                kill = true;
                state = state with { Active = false, TimeLeft = 0 };
            }
        }
        state = state with { Rotation = state.Rotation + 0.4f * state.Direction };
        transition = new(state, kill);
        return true;
    }

    public static bool TryResolveTileImpact(in ThrownAxeState1458 previous,
        float incomingX, float incomingY, out ThrownAxeState1458 after)
    {
        after = previous;
        if (!float.IsFinite(incomingX) || !float.IsFinite(incomingY)) return false;
        after = previous with
        {
            Ai0 = 1f,
            VelocityX = -incomingX,
            VelocityY = -incomingY,
            NetUpdate = true
        };
        return true;
    }

    private static float Approach(float value, float target, float amount)
    {
        if (value < target)
        {
            value += amount;
            if (value < 0f && target > 0f) value += amount;
        }
        else if (value > target)
        {
            value -= amount;
            if (value > 0f && target < 0f) value -= amount;
        }
        return value;
    }

    private static float Length(float x, float y, bool windows) =>
        (float)Math.Sqrt(windows ? x * (double)x + y * (double)y : x * x + y * y);

    private static bool Intersects(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
        bx < ax + aw && ax < bx + bw && by < ay + ah && ay < by + bh;
}
