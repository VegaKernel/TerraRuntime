namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct GemStaffPoint1458(float X, float Y);
public readonly record struct GemStaffTrail1458(GemStaffPoint1458 P0, GemStaffPoint1458 P1,
    GemStaffPoint1458 P2, GemStaffPoint1458 P3, GemStaffPoint1458 P4, GemStaffPoint1458 P5)
{
    public GemStaffPoint1458 this[int index] => index switch
    {
        0 => P0, 1 => P1, 2 => P2, 3 => P3, 4 => P4, 5 => P5,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    // Original Update performs this shift AFTER Damage, once per physical subupdate.
    public GemStaffTrail1458 AdvanceAfterDamage(float x, float y) => new(new(x, y), P0, P1, P2, P3, P4);
}
public readonly record struct GemStaffRuntimeState1458(float Scale, float Rotation, GemStaffTrail1458 Trail)
{
    public static GemStaffRuntimeState1458 Constructor => new(1f, 0f, default);
}
public readonly record struct GemStaffHitbox1458(int X, int Y, int Width, int Height);
public readonly record struct GemStaffLosQuery1458(float FromX, float FromY, int FromWidth, int FromHeight,
    float ToX, float ToY, int ToWidth, int ToHeight);

public static partial class VanillaGemStaffProjectile1458
{
    public static bool TryBuildDamageHitbox(float x, float y, int width, int height,
        out GemStaffHitbox1458 hitbox)
    {
        hitbox = default;
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < int.MinValue + 4d || x > int.MaxValue - 4d ||
            y < int.MinValue + 4d || y > int.MaxValue - 4d || width <= 0 || height <= 0 ||
            width > int.MaxValue - 8 || height > int.MaxValue - 8) return false;
        hitbox = new((int)x - 4, (int)y - 4, width + 8, height + 8);
        return true;
    }

    public static bool TryCollide(GemStaffFeatures1458 features, in GemStaffHitbox1458 bodyDamageHitbox,
        float physicalCenterX, float physicalCenterY, in GemStaffTrail1458 trail,
        in GemStaffHitbox1458 target, Func<GemStaffLosQuery1458, bool> canHit, out bool hit)
    {
        hit = false;
        if (canHit is null || !TryDecodeFeatures(EncodeFeatures(features), out _, true) ||
            !ValidBox(in bodyDamageHitbox) || !ValidBox(in target) ||
            !float.IsFinite(physicalCenterX) || !float.IsFinite(physicalCenterY)) return false;
        bool bigger = (features & GemStaffFeatures1458.BiggerHitbox) != 0;
        if ((features & GemStaffFeatures1458.FastThenSlow) != 0)
        {
            for (int i = 1; i <= 5; i++)
            {
                var point = trail[i];
                if (point == default) break;
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < int.MinValue + 30d ||
                    point.X > int.MaxValue - 30d || point.Y < int.MinValue + 30d || point.Y > int.MaxValue - 30d)
                    return false;
                // Original replaces X/Y with oldPos directly; it does not retain the body's -4 offset.
                var rectangle = new GemStaffHitbox1458((int)point.X, (int)point.Y,
                    bodyDamageHitbox.Width, bodyDamageHitbox.Height);
                if (bigger) rectangle = Inflate(rectangle, 30);
                if (Intersects(rectangle, target) && canHit(new(rectangle.X, rectangle.Y, rectangle.Width,
                        rectangle.Height, target.X, target.Y, target.Width, target.Height)))
                { hit = true; return true; }
            }
        }
        var current = bigger ? Inflate(bodyDamageHitbox, 30) : bodyDamageHitbox;
        if (Intersects(current, target))
            hit = canHit(new(physicalCenterX, physicalCenterY, 0, 0,
                target.X + target.Width / 2, target.Y + target.Height / 2, 0, 0));
        return true;
    }

    private static bool ValidBox(in GemStaffHitbox1458 box) => box.Width > 0 && box.Height > 0 &&
        box.Width < int.MaxValue - 60 && box.Height < int.MaxValue - 60 &&
        box.X > int.MinValue + 30 && box.Y > int.MinValue + 30 &&
        (long)box.X + box.Width < int.MaxValue - 30 && (long)box.Y + box.Height < int.MaxValue - 30;
    private static GemStaffHitbox1458 Inflate(GemStaffHitbox1458 box, int amount) =>
        new(box.X - amount, box.Y - amount, box.Width + amount * 2, box.Height + amount * 2);
    private static bool Intersects(GemStaffHitbox1458 a, GemStaffHitbox1458 b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
