using System.Numerics;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct GemStaffTileImpact1458(GemStaffMotionState1458 State, int Penetration,
    bool MustKill, bool ClearTrail, bool Bounced);

public static partial class VanillaGemStaffProjectile1458
{
    /// <summary>The input pose already includes source slope correction. The caller owns the later common UpdatePosition.</summary>
    public static bool TryResolveTileImpact(ProjectileTypeId type, GemStaffFeatures1458 features,
        in GemStaffMotionState1458 before, float clampedVelocityX, float clampedVelocityY, int penetration,
        bool ownsTargetSelection, ReadOnlySpan<GemStaffTarget1458> targets, out GemStaffTileImpact1458 impact,
        bool windowsArithmetic = false, int physicalWidth = 10, int physicalHeight = 10)
    {
        impact = default;
        if (!IsGemStaffProjectile(type) || !TryDecodeFeatures(EncodeFeatures(features), out _, true) ||
            !ValidMotion(in before) || !float.IsFinite(clampedVelocityX) || !float.IsFinite(clampedVelocityY) || penetration < -1 || physicalWidth != physicalHeight || physicalWidth is not (10 or 22))
            return false;
        bool bounced = (features & GemStaffFeatures1458.CanBounce) != 0;
        float x = before.PositionX, y = before.PositionY, ai2 = before.Ai2;
        Vector2 incoming = new(before.VelocityX, before.VelocityY);
        Vector2 velocity = new(clampedVelocityX, clampedVelocityY);
        bool kill;
        if (bounced)
        {
            if (clampedVelocityX != before.VelocityX) velocity.X = -before.VelocityX * .8f;
            if (clampedVelocityY != before.VelocityY) velocity.Y = -before.VelocityY * .8f;
            penetration--;
            kill = penetration <= 0;
            if ((features & GemStaffFeatures1458.Homing) != 0)
            {
                int selected = -1;
                if (ownsTargetSelection && !TrySelectTarget(targets, x + physicalWidth * .5f, y + physicalHeight * .5f, 2400f, out selected, windowsArithmetic)) return false;
                if (selected >= 0)
                {
                    GemStaffTarget1458 selectedFacts = default;
                    foreach (var target in targets) if (target.Index == selected) { selectedFacts = target; break; }
                    Vector2 delta = new(selectedFacts.CenterX - (x + physicalWidth * .5f), selectedFacts.CenterY - (y + physicalHeight * .5f));
                    if (Length(delta) > 0f) velocity = NormalizeImpact(delta, windowsArithmetic) * MotionLength(incoming, windowsArithmetic) * .8f;
                }
                ai2 = selected + 1;
            }
        }
        else if ((features & GemStaffFeatures1458.FastThenSlow) != 0)
        {
            x += clampedVelocityX * .99f;
            y += clampedVelocityY * .99f;
            velocity = NormalizeImpact(incoming, windowsArithmetic) * .0001f;
            kill = false;
        }
        else kill = true;
        var after = before with { PositionX = x, PositionY = y, VelocityX = velocity.X, VelocityY = velocity.Y, Ai2 = ai2 };
        if (!ValidMotion(in after)) return false;
        impact = new(after, penetration, kill, bounced, bounced);
        return true;
    }
    private static Vector2 NormalizeImpact(Vector2 value, bool windows)
    {
        if (!windows) return Normalize(value);
        if (value == Vector2.Zero) return Vector2.Zero;
        double reciprocal = 1.0 / Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y);
        return new((float)(value.X * reciprocal), (float)(value.Y * reciprocal));
    }
}
