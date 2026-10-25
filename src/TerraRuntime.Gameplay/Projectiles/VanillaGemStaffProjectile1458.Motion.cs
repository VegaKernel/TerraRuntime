using System.Numerics;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct GemStaffMotionState1458(float PositionX, float PositionY,
    float VelocityX, float VelocityY, float Scale, float Rotation, float LocalAi0, float Ai1, float Ai2);

/// <summary>Captured NPC eligibility and LOS. The caller must certify the complete source candidate census.</summary>
public readonly record struct GemStaffTarget1458(int Index, float CenterX, float CenterY,
    bool CanBeChased, bool HasLineOfSight, bool LocallyImmune);

public readonly record struct GemStaffMotionResult1458(GemStaffMotionState1458 State,
    bool MustKill, bool TargetChanged);

public static partial class VanillaGemStaffProjectile1458
{
    public const int MaximumNpcCount = 200;
    public static bool TrySelectTarget(ReadOnlySpan<GemStaffTarget1458> targets, float centerX,
        float centerY, float range, out int selected) =>
        TrySelectTarget(targets, centerX, centerY, range, out selected, false);

    public static bool TrySelectTarget(ReadOnlySpan<GemStaffTarget1458> targets, float centerX,
        float centerY, float range, out int selected, bool windowsArithmetic)
    {
        selected = -1;
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(range) || range <= 0f ||
            targets.Length > MaximumNpcCount) return false;
        float nearest = range;
        int previous = -1;
        foreach (var target in targets)
        {
            if (target.Index <= previous || target.Index >= MaximumNpcCount || !float.IsFinite(target.CenterX) ||
                !float.IsFinite(target.CenterY)) return false;
            previous = target.Index;
            if (!target.CanBeChased || target.LocallyImmune || !target.HasLineOfSight) continue;
            // Original Entity.Distance uses the edition's Vector2 distance arithmetic.
            // The final Single can cross the strict range or nearest-target boundary.
            float distance = MotionLength(new(target.CenterX - centerX, target.CenterY - centerY), windowsArithmetic);
            if (distance < nearest) { nearest = distance; selected = target.Index; }
        }
        return true;
    }

    public static bool TryStepMotion(ProjectileTypeId type, GemStaffFeatures1458 features,
        in GemStaffMotionState1458 before, bool ownsTargetSelection, ReadOnlySpan<GemStaffTarget1458> targets,
        Func<int, int> nextInteger, out GemStaffMotionResult1458 result, bool windowsArithmetic = false)
    {
        result = default;
        if (nextInteger is null || !IsGemStaffProjectile(type) ||
            !TryDecodeFeatures(EncodeFeatures(features), out _, true) || !ValidMotion(in before)) return false;
        bool homing = (features & GemStaffFeatures1458.Homing) != 0;
        bool fast = (features & GemStaffFeatures1458.FastThenSlow) != 0;
        float range = fast || (features & GemStaffFeatures1458.CanBounce) != 0 ? 1000f : 500f;
        int targetIndex = (int)before.Ai2 - 1;
        Vector2 center = new(before.PositionX + 5f, before.PositionY + 5f);
        if (!TrySelectTarget(targets, center.X, center.Y, range, out int selected, windowsArithmetic)) return false;
        if (homing && ownsTargetSelection) targetIndex = selected;
        GemStaffTarget1458? chosen = null;
        if (homing && targetIndex >= 0)
        {
            foreach (var target in targets) if (target.Index == targetIndex) { chosen = target; break; }
            if (chosen is null) return false;
        }
        // These two explicit calls precede Dust.NewDust's dedicated-server early return.
        for (int i = 0; i < 2; i++)
        {
            int offer = nextInteger(5);
            if (offer is < 0 or >= 5) return false;
        }
        Vector2 velocity = new(before.VelocityX, before.VelocityY);
        Vector2 position = new(before.PositionX, before.PositionY);
        if (chosen is { } targetFacts)
        {
            Vector2 target = new(targetFacts.CenterX, targetFacts.CenterY);
            float wantedSpeed = fast ? 16f : 8f;
            float speed = MotionLength(velocity, windowsArithmetic);
            if (speed < wantedSpeed) velocity = (velocity + Normalize(velocity) * .1f) * 1.1f;
            float distance = MotionLength(target - center, windowsArithmetic);
            float turn = Remap(distance, range, 0f, 0f, 1f) * .1f + .01f;
            if (speed < wantedSpeed) turn = (turn + .05f) * Remap(speed, 1f, 3f, 10f, 1f);
            if (fast) turn *= Remap(speed, 30f, 40f, 1f, 0f);
            float angle = TowardsAngle((float)Math.Atan2(velocity.Y, velocity.X),
                (float)Math.Atan2(target.Y - center.Y, target.X - center.X), turn);
            velocity = Direction(angle) * MotionLength(velocity, windowsArithmetic);
            Vector2 delta = target - center;
            Vector2 desired = Length(delta) < wantedSpeed ? delta : Normalize(delta) * wantedSpeed;
            float blend = Remap(distance, 150f, 0f, 0f, .2f);
            velocity = new(velocity.X + (desired.X - velocity.X) * blend,
                velocity.Y + (desired.Y - velocity.Y) * blend);
        }
        float local = before.LocalAi0;
        if ((features & GemStaffFeatures1458.SwirlTwins) != 0)
        {
            float phase = (int)before.Ai1 * (float)Math.PI;
            float angularStep = (float)Math.PI * 2f;
            float waveBefore = (float)Math.Sin(local * angularStep * .05f + phase);
            float waveAfter = (float)Math.Sin((local + 1f) * angularStep * .05f + phase);
            local++;
            float angle = (float)Math.Atan2(velocity.Y, velocity.X) + (float)Math.PI / 2f;
            float difference = windowsArithmetic
                ? (float)(Math.Sin(((double)local) * angularStep * .05f + phase) - Math.Sin(((double)local - 1d) * angularStep * .05f + phase))
                : waveAfter - waveBefore;
            position += Direction(angle) * difference * 6f;
        }
        float scale = before.Scale;
        bool kill = false;
        if (fast)
        {
            if (Length(velocity) > .1f) velocity *= .97f;
            else { scale -= .005f; kill = scale < .1f; }
        }
        float ai2 = homing && ownsTargetSelection ? targetIndex + 1 : before.Ai2;
        var state = new GemStaffMotionState1458(position.X, position.Y, velocity.X, velocity.Y, scale,
            (float)Math.Atan2(velocity.Y, velocity.X), local, before.Ai1, ai2);
        if (!ValidMotion(in state)) return false;
        result = new(state, kill, ai2 != before.Ai2);
        return true;
    }

    private static bool ValidMotion(in GemStaffMotionState1458 state) =>
        float.IsFinite(state.PositionX) && float.IsFinite(state.PositionY) && float.IsFinite(state.VelocityX) &&
        float.IsFinite(state.VelocityY) && float.IsFinite(state.Scale) && state.Scale > 0f &&
        float.IsFinite(state.Rotation) && float.IsFinite(state.LocalAi0) && state.LocalAi0 >= 0f &&
        float.IsFinite(state.Ai1) && state.Ai1 is >= 0f and <= MaximumNpcCount &&
        float.IsFinite(state.Ai2) && state.Ai2 is >= 0f and <= MaximumNpcCount && state.Ai2 == (int)state.Ai2;
    private static float MotionLength(Vector2 value, bool windows) => windows
        ? (float)Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y) : Length(value);
    private static float Length(Vector2 value) => (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
    private static Vector2 Normalize(Vector2 value) => value == Vector2.Zero ? Vector2.Zero : value * (1f / Length(value));
    private static Vector2 Direction(float angle) => new((float)Math.Cos(angle), (float)Math.Sin(angle));
    private static float Remap(float value, float from, float to, float min, float max) =>
        min + (max - min) * Math.Clamp((value - from) / (to - from), 0f, 1f);
    private static float Wrap(float angle)
    {
        float twoPi = (float)Math.PI * 2f;
        float result = (float)Math.IEEERemainder(angle, twoPi);
        if (result <= -(float)Math.PI) result += twoPi;
        else if (result > (float)Math.PI) result -= twoPi;
        return result;
    }
    private static float TowardsAngle(float angle, float target, float amount)
    {
        angle = Wrap(angle); target = Wrap(target);
        if (angle < target && target - angle > (float)Math.PI) angle += (float)Math.PI * 2f;
        else if (angle > target && angle - target > (float)Math.PI) angle -= (float)Math.PI * 2f;
        return Wrap(angle + Math.Clamp(target - angle, -amount, amount));
    }
}
