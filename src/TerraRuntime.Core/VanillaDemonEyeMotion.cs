namespace TerraRuntime.Core;

/// <summary>
/// Inputs owned by the simulation/collision/targeting layers for one ordinary Demon Eye motion step.
/// Target selection remains outside this primitive; DirectionX/DirectionY are the result of vanilla-style
/// targeting for the current tick.
/// </summary>
public readonly record struct VanillaDemonEyeMotionInput(
    float VelocityX,
    float VelocityY,
    float OldVelocityX,
    float OldVelocityY,
    int DirectionX,
    int DirectionY,
    float Scale,
    bool NoTileCollide,
    bool CollideX,
    bool CollideY,
    bool Wet);

/// <summary>
/// State-only result of the verified ordinary type-2 floating-eye motion core.
/// Cosmetic dust and target acquisition are deliberate side/context boundaries.
/// </summary>
public readonly record struct VanillaDemonEyeMotionResult(
    float VelocityX,
    float VelocityY,
    bool NoGravity);

/// <summary>
/// Clean-room implementation of the generic movement branch used by Demon Eye (NPC type 2) in
/// TerrariaServer 1.4.5.8 AI_002_FloatingEye. This is not yet the full style-2 system: target selection,
/// collision production and cosmetic effects are supplied/handled by their owning runtime systems.
/// </summary>
public static class VanillaDemonEyeMotion
{
    public static bool TryStep(
        in VanillaDemonEyeMotionInput input,
        out VanillaDemonEyeMotionResult result)
    {
        if (!IsValid(in input))
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;

        if (!input.NoTileCollide)
        {
            if (input.CollideX)
            {
                velocityX = input.OldVelocityX * -0.5f;
                if (input.DirectionX == -1 && velocityX > 0f && velocityX < 2f)
                    velocityX = 2f;
                if (input.DirectionX == 1 && velocityX < 0f && velocityX > -2f)
                    velocityX = -2f;
            }

            if (input.CollideY)
            {
                velocityY = input.OldVelocityY * -0.5f;
                if (velocityY > 0f && velocityY < 1f)
                    velocityY = 1f;
                if (velocityY < 0f && velocityY > -1f)
                    velocityY = -1f;
            }
        }

        float maxVelocityX = 4f * (1f + (1f - input.Scale));
        float maxVelocityY = 1.5f * (1f + (1f - input.Scale));

        if (input.DirectionX == -1 && velocityX > -maxVelocityX)
        {
            velocityX -= 0.1f;
            if (velocityX > maxVelocityX)
                velocityX -= 0.1f;
            else if (velocityX > 0f)
                velocityX += 0.05f;
            if (velocityX < -maxVelocityX)
                velocityX = -maxVelocityX;
        }
        else if (input.DirectionX == 1 && velocityX < maxVelocityX)
        {
            velocityX += 0.1f;
            if (velocityX < -maxVelocityX)
                velocityX += 0.1f;
            else if (velocityX < 0f)
                velocityX -= 0.05f;
            if (velocityX > maxVelocityX)
                velocityX = maxVelocityX;
        }

        if (input.DirectionY == -1 && velocityY > -maxVelocityY)
        {
            velocityY -= 0.04f;
            if (velocityY > maxVelocityY)
                velocityY -= 0.05f;
            else if (velocityY > 0f)
                velocityY += 0.03f;
            if (velocityY < -maxVelocityY)
                velocityY = -maxVelocityY;
        }
        else if (input.DirectionY == 1 && velocityY < maxVelocityY)
        {
            velocityY += 0.04f;
            if (velocityY < -maxVelocityY)
                velocityY += 0.05f;
            else if (velocityY < 0f)
                velocityY -= 0.03f;
            if (velocityY > maxVelocityY)
                velocityY = maxVelocityY;
        }

        if (input.Wet)
        {
            if (velocityY > 0f)
                velocityY *= 0.95f;
            velocityY -= 0.5f;
            if (velocityY < -4f)
                velocityY = -4f;
        }

        result = new VanillaDemonEyeMotionResult(velocityX, velocityY, NoGravity: true);
        return true;
    }

    private static bool IsValid(in VanillaDemonEyeMotionInput input) =>
        float.IsFinite(input.VelocityX) &&
        float.IsFinite(input.VelocityY) &&
        float.IsFinite(input.OldVelocityX) &&
        float.IsFinite(input.OldVelocityY) &&
        float.IsFinite(input.Scale) &&
        input.Scale > 0f &&
        input.DirectionX is >= -1 and <= 1 &&
        input.DirectionY is >= -1 and <= 1;
}
