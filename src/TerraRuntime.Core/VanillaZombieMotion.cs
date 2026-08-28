using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaZombieTargetRefresh(
    bool HasTarget,
    ushort Target,
    int DirectionX,
    int DirectionY)
{
    public bool IsValid =>
        (!HasTarget ||
         (Target < byte.MaxValue && DirectionX is -1 or 1 && DirectionY is -1 or 1)) &&
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1;
}

public readonly record struct VanillaZombieMotionInput(
    float PositionX,
    float OldPositionX,
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    float Scale,
    bool TargetOverlaps,
    VanillaZombieTargetRefresh ClosestTarget);

public readonly record struct VanillaZombieMotionResult(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    int TargetRefreshes);

/// <summary>
/// Deterministic ordinary type-3 state slice from TerrariaServer 1.4.5.8 NPC.AI_003_Fighters.
/// This covers the night/underground pursuit path shared by a plain Zombie: TargetClosest cadence,
/// ai[3] stuck accounting and the default horizontal acceleration band. Obstacle/door probing,
/// justHit resets, daytime surface despawn behavior and special fighter subtypes are separate layers.
/// </summary>
public static class VanillaZombieMotion
{
    private const float StuckThreshold = 60f;
    private const float MaximumStuckCounter = StuckThreshold * 10f;
    private const float BaseMaximumHorizontalSpeed = 1f;
    private const float HorizontalAcceleration = 0.07f;

    public static bool TryStep(
        in VanillaZombieMotionInput input,
        out VanillaZombieMotionResult result)
    {
        if (!float.IsFinite(input.PositionX) ||
            !float.IsFinite(input.OldPositionX) ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            !float.IsFinite(input.Scale) ||
            input.Scale <= 0f ||
            input.DirectionX is < -1 or > 1 ||
            input.DirectionY is < -1 or > 1 ||
            input.Target > byte.MaxValue ||
            !input.Ai.IsFinite ||
            !input.ClosestTarget.IsValid)
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;
        int targetRefreshes = 0;

        bool reversingWhileGrounded =
            velocityY == 0f &&
            ((velocityX > 0f && directionX < 0) ||
             (velocityX < 0f && directionX > 0));

        if (input.PositionX == input.OldPositionX || ai3 >= StuckThreshold || reversingWhileGrounded)
        {
            ai3++;
        }
        else if (MathF.Abs(velocityX) > 0.9f && ai3 > 0f)
        {
            ai3--;
        }

        if (ai3 > MaximumStuckCounter)
            ai3 = 0f;
        if (input.TargetOverlaps)
            ai3 = 0f;

        // Plain type 3 is not discouraged at night or below worldSurface, so the verified branch calls
        // TargetClosest every tick while ai[3] remains below the stuck threshold.
        if (ai3 < StuckThreshold)
        {
            RefreshTarget();
            if (directionY > 0 && input.ClosestTarget.HasTarget && input.ClosestTarget.DirectionY < 0)
                directionY = -1;
        }

        float maximumSpeed = BaseMaximumHorizontalSpeed * (1f + (1f - input.Scale));
        if (velocityX < -maximumSpeed || velocityX > maximumSpeed)
        {
            if (velocityY == 0f)
                velocityX *= 0.8f;
        }
        else if (velocityX < maximumSpeed && directionX == 1)
        {
            velocityX += HorizontalAcceleration;
            if (velocityX > maximumSpeed)
                velocityX = maximumSpeed;
        }
        else if (velocityX > -maximumSpeed && directionX == -1)
        {
            velocityX -= HorizontalAcceleration;
            if (velocityX < -maximumSpeed)
                velocityX = -maximumSpeed;
        }

        result = new VanillaZombieMotionResult(
            velocityX,
            velocityY,
            directionX,
            directionY,
            target,
            new NpcAiState(ai0, ai1, ai2, ai3),
            targetRefreshes);
        return true;

        void RefreshTarget()
        {
            targetRefreshes++;
            if (!input.ClosestTarget.HasTarget)
                return;

            target = input.ClosestTarget.Target;
            directionX = input.ClosestTarget.DirectionX;
            directionY = input.ClosestTarget.DirectionY;
        }
    }
}
