using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct HeldToolProjectileState1458(float X, float Y, float VelocityX,
    float VelocityY, float Scale, float Rotation, int Direction, int SpriteDirection,
    int TimeLeft, int SoundDelay, int Frame, int FrameCounter, float LocalAi0, float LocalAi1,
    bool Active, bool NetUpdate);

public readonly record struct HeldToolOwnerFacts1458(float MountedX, float MountedY,
    bool OwnsAim, bool Channel, bool NoItems, bool CrowdControlled, int SelectedPick,
    float ShootSpeed, float AimX, float AimY, bool TargetActive, int TargetX, int TargetY,
    int TileRangeX, int TileRangeY, int TileBoost, bool WindowsArithmetic);

public readonly record struct HeldToolProjectileTransition1458(HeldToolProjectileState1458 State,
    int OwnerDirection, float OwnerItemRotation, int DummyItemTime, bool SourceKill,
    int RandomSamples);

/// <summary>AI20 and the retained drill445 branch of AI75, before common projectile movement/lifetime.</summary>
public static class VanillaHeldToolProjectile1458
{
    public const int Count = 23;

    public static bool IsSupported(ProjectileTypeId type) => type.Value is
        57 or 58 or 59 or 60 or 61 or 62 or 107 or 213 or 214 or 216 or 217 or 219 or 220 or
        223 or 224 or 252 or 369 or 428 or 430 or 432 or 445 or 509 or 609;

    public static bool TryFinalize(ProjectileTypeId type, float x, float y, float velocityX,
        float velocityY, int retainedDirection, out HeldToolProjectileState1458 state)
    {
        state = default;
        if (!IsSupported(type) || !float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(velocityX) || !float.IsFinite(velocityY) ||
            retainedDirection is not (-1 or 1)) return false;
        float scale = type.Value switch
        {
            58 => 1.08f,
            59 or 60 or 62 => 0.9f,
            61 => 1.16f,
            107 or 224 or 252 => 1.1f,
            213 => 0.92f,
            216 => 0.93f,
            217 => 1.12f,
            219 => 0.95f,
            220 or 509 => 1.2f,
            _ => 1f
        };
        // SetDefaults does NOT reset Entity.direction in a pooled slot. It does
        // reset spriteDirection to1. The caller supplies retained owner state,
        // never a direction inferred from an untrusted packet27 velocity.
        state = new(x, y, velocityX, velocityY, scale, 0f, retainedDirection, 1,
            3600, 0, 0, 0, 0f, 0f, true, false);
        return true;
    }

    // Dedicated Dust.NewDust returns its sink without consuming Main.rand. The
    // explicit surrounding AI draws still run. The owner is an already-captured
    // source role/context, not permission to accept a client supplied mouse pose.
    public static bool TryStepAi(ProjectileTypeId type, int width, int height,
        in HeldToolProjectileState1458 previous, in HeldToolOwnerFacts1458 owner,
        Func<int, int, int> nextInteger, out HeldToolProjectileTransition1458 transition)
    {
        transition = default;
        if (!IsSupported(type) || width is < 1 or > 128 || height is < 1 or > 128 ||
            !previous.Active || previous.Direction is not (-1 or 1) ||
            !float.IsFinite(previous.X) || !float.IsFinite(previous.Y) ||
            !float.IsFinite(previous.VelocityX) || !float.IsFinite(previous.VelocityY) ||
            !float.IsFinite(previous.Scale) || previous.Scale <= 0f ||
            !float.IsFinite(previous.LocalAi0) || !float.IsFinite(previous.LocalAi1) ||
            !float.IsFinite(owner.MountedX) || !float.IsFinite(owner.MountedY) ||
            !float.IsFinite(owner.ShootSpeed) || owner.ShootSpeed < 0f ||
            nextInteger is null || owner.OwnsAim &&
                (!float.IsFinite(owner.AimX) || !float.IsFinite(owner.AimY))) return false;
        if (type.Value == 445)
            return TryStepLaser(width, height, previous, owner, out transition);

        var state = previous with { TimeLeft = 60 };
        if (type.Value is 252 or 509)
        {
            int counter = state.FrameCounter + 1;
            int frame = state.Frame;
            if (counter >= (type.Value == 252 ? 4 : 2)) { counter = 0; frame++; }
            if (frame > (type.Value == 252 ? 3 : 1)) frame = 0;
            state = state with { FrameCounter = counter, Frame = frame };
        }
        if (state.SoundDelay <= 0)
            state = state with { SoundDelay = owner.SelectedPick > 0 ? 15 : 20 };
        bool kill = false;
        float vx = state.VelocityX, vy = state.VelocityY;
        if (owner.OwnsAim)
        {
            if (owner.Channel)
            {
                float dx = owner.AimX - owner.MountedX, dy = owner.AimY - owner.MountedY;
                float speed = owner.ShootSpeed * state.Scale;
                float ratio = speed / Length(dx, dy, owner.WindowsArithmetic);
                float nx = dx * ratio, ny = dy * ratio;
                if (nx != vx || ny != vy) state = state with { NetUpdate = true };
                vx = nx;
                vy = ny;
            }
            else
            {
                kill = true;
                state = state with { Active = false, TimeLeft = 0 };
            }
        }
        int direction = state.Direction;
        float rotation = (float)(Math.Atan2(vy, vx) + 1.5700000524520874);
        float itemRotation = (float)Math.Atan2(vy * direction, vx * direction);
        int jitter = nextInteger(-3, 4);
        if (jitter is < -3 or > 3) return false;
        vx = owner.WindowsArithmetic
            ? (float)(vx * (1d + jitter * (double)0.01f))
            : vx * (1f + jitter * 0.01f);
        int emit = nextInteger(0, 6);
        if ((uint)emit >= 6) return false;
        int samples = 2;
        if (emit == 0)
        {
            int displacement = nextInteger(6, 10);
            int rise = nextInteger(7, 13);
            if (displacement is < 6 or > 9 || rise is < 7 or > 12) return false;
            samples += 2;
        }
        state = state with
        {
            X = owner.MountedX - width / 2,
            Y = owner.MountedY - height / 2,
            VelocityX = vx,
            VelocityY = vy,
            Rotation = rotation,
            SpriteDirection = direction
        };
        transition = new(state, direction, itemRotation, 2, kill, samples);
        return true;
    }

    private static bool TryStepLaser(int width, int height,
        in HeldToolProjectileState1458 previous, in HeldToolOwnerFacts1458 owner,
        out HeldToolProjectileTransition1458 transition)
    {
        transition = default;
        if (owner.TileRangeX is < 0 or > 100 || owner.TileRangeY is < 0 or > 100 ||
            owner.TileBoost is < -20 or > 100) return false;
        var state = previous;
        if (state.SoundDelay <= 0) state = state with { SoundDelay = 23 };
        float local0 = state.LocalAi0 + 1f;
        if (local0 >= 60f) local0 = 0f;
        float local1 = state.LocalAi1;
        float vx = state.VelocityX, vy = state.VelocityY;
        bool kill = false;
        if (owner.OwnsAim)
        {
            if (local1 > 0f) local1--;
            if (!owner.Channel || owner.NoItems || owner.CrowdControlled)
            {
                kill = true;
                state = state with { Active = false };
            }
            else if (local1 == 0f)
            {
                float dx = owner.AimX - owner.MountedX, dy = owner.AimY - owner.MountedY;
                if (owner.TargetActive)
                {
                    dx = owner.TargetX * 16f + 8f - owner.MountedX;
                    dy = owner.TargetY * 16f + 8f - owner.MountedY;
                    local1 = 2f;
                }
                dx = Lerp(dx, vx, 0.7f, owner.WindowsArithmetic);
                dy = Lerp(dy, vy, 0.7f, owner.WindowsArithmetic);
                if (float.IsNaN(dx) || float.IsNaN(dy)) { dx = 0f; dy = -1f; }
                float length = Length(dx, dy, owner.WindowsArithmetic);
                if (length < 30f)
                {
                    double inverse = owner.WindowsArithmetic ? 1d / length : 1f / length;
                    dx = (float)(dx * inverse) * 30f;
                    dy = (float)(dy * inverse) * 30f;
                }
                int left = (-owner.TileRangeX - owner.TileBoost + 1) * 16 - 12;
                int right = (owner.TileRangeX + owner.TileBoost - 1) * 16 + 12;
                int top = (-owner.TileRangeY - owner.TileBoost) * 16 - 12;
                int bottom = (owner.TileRangeY + owner.TileBoost - 1) * 16 + 12;
                if (dx < left || dx > right || dy < top || dy > bottom)
                {
                    float sourceLength = Length(dx, dy, owner.WindowsArithmetic);
                    double inverse = owner.WindowsArithmetic ? 1d / sourceLength : 1f / sourceLength;
                    float nx = (float)(dx * inverse), ny = (float)(dy * inverse);
                    float distance = -1f;
                    if (nx < 0f) distance = left / nx;
                    if (nx > 0f) distance = right / nx;
                    if (ny < 0f && (top / ny < distance || distance == -1f)) distance = top / ny;
                    if (ny > 0f && (bottom / ny < distance || distance == -1f)) distance = bottom / ny;
                    dx = nx * distance;
                    dy = ny * distance;
                }
                if (dx != vx || dy != vy) state = state with { NetUpdate = true };
                vx = dx;
                vy = dy;
            }
        }
        float rotation = (float)Math.Atan2(vy, vx) + (float)Math.PI / 2f;
        float itemRotation = WrapAngle((float)Math.Atan2(vy * state.Direction, vx * state.Direction));
        state = state with
        {
            X = owner.MountedX - width / 2f,
            Y = owner.MountedY - height / 2f,
            VelocityX = vx,
            VelocityY = vy,
            Rotation = rotation,
            LocalAi0 = local0,
            LocalAi1 = local1,
            SpriteDirection = state.Direction,
            TimeLeft = 2
        };
        transition = new(state, state.Direction, itemRotation, 2, kill, 0);
        return true;
    }

    private static float Length(float x, float y, bool windows) =>
        (float)Math.Sqrt(windows ? x * (double)x + y * (double)y : x * x + y * y);

    private static float Lerp(float a, float b, float amount, bool windows) =>
        windows ? (float)(a + (b - (double)a) * amount) : a + (b - a) * amount;

    private static float WrapAngle(float angle)
    {
        angle = (float)Math.IEEERemainder(angle, 6.2831854820251465);
        if (angle <= -3.14159274f) angle += 6.28318548f;
        else if (angle > 3.14159274f) angle -= 6.28318548f;
        return angle;
    }
}
