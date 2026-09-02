using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaWormHeadMotionInput(
    float CenterX,
    float CenterY,
    float VelocityX,
    float VelocityY,
    float TargetCenterX,
    float TargetCenterY,
    bool Digging);

public readonly record struct VanillaWormHeadMotionResult(float VelocityX, float VelocityY);

public readonly record struct VanillaWormSegmentFollowInput(
    float PositionX,
    float PositionY,
    int Width,
    int Height,
    float LeaderCenterX,
    float LeaderCenterY,
    float Gap);

public readonly record struct VanillaWormSegmentFollowResult(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int DirectionX);

/// <summary>State-only movement core of TerrariaServer 1.4.5.8 AI_006_Worms.</summary>
public static class VanillaWormMotion
{
    private const float CoordinateQuantum = 16f;

    public static bool TryStepHead(
        in VanillaWormHeadMotionInput input,
        in VanillaWormMotionProfile profile,
        out VanillaWormHeadMotionResult result)
    {
        if (!profile.IsValid ||
            !float.IsFinite(input.CenterX) ||
            !float.IsFinite(input.CenterY) ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            !float.IsFinite(input.TargetCenterX) ||
            !float.IsFinite(input.TargetCenterY))
        {
            result = default;
            return false;
        }

        float offsetX = Snap(input.TargetCenterX) - Snap(input.CenterX);
        float offsetY = Snap(input.TargetCenterY) - Snap(input.CenterY);
        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;

        if (input.Digging)
        {
            SwimThroughGround(
                ref velocityX,
                ref velocityY,
                offsetX,
                offsetY,
                profile.MaximumSpeed,
                profile.TurnRate);
        }
        else
        {
            ArcThroughAir(
                ref velocityX,
                ref velocityY,
                offsetX,
                profile.MaximumSpeed,
                profile.TurnRate,
                velocityY < 0f ? profile.RisingAirGravity : profile.AirGravity);
        }

        result = new VanillaWormHeadMotionResult(velocityX, velocityY);
        return true;
    }

    public static bool TryFollowSegment(
        in VanillaWormSegmentFollowInput input,
        out VanillaWormSegmentFollowResult result)
    {
        if (!float.IsFinite(input.PositionX) ||
            !float.IsFinite(input.PositionY) ||
            input.Width <= 0 ||
            input.Height <= 0 ||
            !float.IsFinite(input.LeaderCenterX) ||
            !float.IsFinite(input.LeaderCenterY) ||
            !float.IsFinite(input.Gap) ||
            input.Gap <= 0f)
        {
            result = default;
            return false;
        }

        float centerX = input.PositionX + input.Width * 0.5f;
        float centerY = input.PositionY + input.Height * 0.5f;
        float deltaX = input.LeaderCenterX - centerX;
        float deltaY = input.LeaderCenterY - centerY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        int directionX = deltaX > 0f ? 1 : -1;
        if (distance == 0f)
        {
            result = new(input.PositionX, input.PositionY, 0f, 0f, directionX);
            return true;
        }

        float reach = (distance - input.Gap) / distance;
        result = new VanillaWormSegmentFollowResult(
            input.PositionX + deltaX * reach,
            input.PositionY + deltaY * reach,
            0f,
            0f,
            directionX);
        return true;
    }

    private static float Snap(float value) => (int)(value / CoordinateQuantum) * CoordinateQuantum;

    private static void ArcThroughAir(
        ref float velocityX,
        ref float velocityY,
        float offsetX,
        float speed,
        float turn,
        float gravity)
    {
        velocityY += gravity;
        if (velocityY > speed)
            velocityY = speed;

        float drift = MathF.Abs(velocityX) + MathF.Abs(velocityY);
        if (drift < speed * 0.4f)
        {
            velocityX += velocityX < 0f ? -turn * 1.1f : turn * 1.1f;
        }
        else if (velocityY == speed)
        {
            if (velocityX < offsetX)
                velocityX += turn;
            else if (velocityX > offsetX)
                velocityX -= turn;
        }
        else if (velocityY > 4f)
        {
            velocityX += velocityX < 0f ? turn * 0.9f : -turn * 0.9f;
        }
    }

    private static void SwimThroughGround(
        ref float velocityX,
        ref float velocityY,
        float offsetX,
        float offsetY,
        float speed,
        float turn)
    {
        float distance = MathF.Sqrt(offsetX * offsetX + offsetY * offsetY);
        if (distance == 0f)
            return;

        float run = MathF.Abs(offsetX);
        float rise = MathF.Abs(offsetY);
        float scale = speed / distance;
        float desiredX = offsetX * scale;
        float desiredY = offsetY * scale;
        float initialVelocityX = velocityX;
        float initialVelocityY = velocityY;
        bool onCourse =
            (initialVelocityX > 0f && desiredX > 0f) ||
            (initialVelocityX < 0f && desiredX < 0f) ||
            (initialVelocityY > 0f && desiredY > 0f) ||
            (initialVelocityY < 0f && desiredY < 0f);

        if (onCourse)
        {
            Approach(ref velocityX, desiredX, turn);
            Approach(ref velocityY, desiredY, turn);
            if (MathF.Abs(desiredY) < speed * 0.2f &&
                ((initialVelocityX > 0f && desiredX < 0f) ||
                 (initialVelocityX < 0f && desiredX > 0f)))
            {
                velocityY += velocityY > 0f ? turn * 2f : -turn * 2f;
            }

            if (MathF.Abs(desiredX) < speed * 0.2f &&
                ((initialVelocityY > 0f && desiredY < 0f) ||
                 (initialVelocityY < 0f && desiredY > 0f)))
            {
                velocityX += velocityX > 0f ? turn * 2f : -turn * 2f;
            }
        }
        else if (run > rise)
        {
            Approach(ref velocityX, desiredX, turn * 1.1f);
            if (MathF.Abs(velocityX) + MathF.Abs(velocityY) < speed * 0.5f)
                velocityY += velocityY > 0f ? turn : -turn;
        }
        else
        {
            Approach(ref velocityY, desiredY, turn * 1.1f);
            if (MathF.Abs(velocityX) + MathF.Abs(velocityY) < speed * 0.5f)
                velocityX += velocityX > 0f ? turn : -turn;
        }
    }

    private static void Approach(ref float value, float desired, float amount)
    {
        if (value < desired)
            value += amount;
        else if (value > desired)
            value -= amount;
    }
}
