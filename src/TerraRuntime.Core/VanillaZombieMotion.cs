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
    VanillaZombieTargetRefresh ClosestTarget)
{
    public bool PursuitAllowed { get; init; } = true;
    public bool EncourageDespawn { get; init; }
    public bool JustHit { get; init; }
    public bool ApplyCanHitRule { get; init; }
    public bool CanHitCurrentTarget { get; init; } = true;
    public float NpcCenterY { get; init; }
    public float CurrentTargetCenterY { get; init; }
    public int TimeLeft { get; init; }
    public int SpriteDirection { get; init; } = -1;
}

public readonly record struct VanillaZombieMotionResult(
    float VelocityX,
    float VelocityY,
    int DirectionX,
    int DirectionY,
    ushort Target,
    NpcAiState Ai,
    int TargetRefreshes)
{
    public int TimeLeft { get; init; }
    public int SpriteDirection { get; init; }
}

/// <summary>
/// Deterministic ordinary type-3 state slice from TerrariaServer 1.4.5.8 NPC.AI_003_Fighters.
/// Covers stuck accounting including justHit and world CanHit resets, pursuit/TargetClosest cadence,
/// discouraged idle turning including spriteDirection, EncourageDespawn(10) and default horizontal motion.
/// World obstacle/door probing remains in the authoritative world-motion layer.
/// </summary>
public static class VanillaZombieMotion
{
    private const float StuckThreshold = 60f;
    private const float MaximumStuckCounter = StuckThreshold * 10f;
    private const float CanHitVerticalResetDistance = 128f;
    private const float BaseMaximumHorizontalSpeed = 1f;
    private const float HorizontalAcceleration = 0.07f;
    private const int EncouragedDespawnTime = 10;

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
            input.SpriteDirection is < -1 or > 1 ||
            input.Target > byte.MaxValue ||
            input.TimeLeft < 0 ||
            (input.ApplyCanHitRule &&
             (!float.IsFinite(input.NpcCenterY) || !float.IsFinite(input.CurrentTargetCenterY))) ||
            !input.Ai.IsFinite ||
            !input.ClosestTarget.IsValid)
        {
            result = default;
            return false;
        }

        VanillaZombieTargetRefresh closestTarget = input.ClosestTarget;
        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX;
        int directionY = input.DirectionY;
        int spriteDirection = input.SpriteDirection;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;
        int targetRefreshes = 0;
        int timeLeft = input.TimeLeft;

        bool reversingWhileGrounded =
            velocityY == 0f &&
            ((velocityX > 0f && directionX < 0) ||
             (velocityX < 0f && directionX > 0));

        if (input.PositionX == input.OldPositionX || ai3 >= StuckThreshold || reversingWhileGrounded)
            ai3++;
        else if (MathF.Abs(velocityX) > 0.9f && ai3 > 0f)
            ai3--;

        if (ai3 > MaximumStuckCounter)
            ai3 = 0f;
        if (input.JustHit)
            ai3 = 0f;
        if (input.TargetOverlaps)
            ai3 = 0f;

        if (input.ApplyCanHitRule)
        {
            if (!input.CanHitCurrentTarget)
            {
                ai3 = StuckThreshold;
                directionY = -1;
            }
            else if (input.CurrentTargetCenterY > input.NpcCenterY - CanHitVerticalResetDistance)
            {
                ai3 = 0f;
            }
        }

        bool pursue = ai3 < StuckThreshold && input.PursuitAllowed;
        if (pursue)
        {
            RefreshTarget();
            if (directionY > 0 && closestTarget.HasTarget && closestTarget.DirectionY < 0)
                directionY = -1;
        }
        else
        {
            if (input.EncourageDespawn && timeLeft > EncouragedDespawnTime)
                timeLeft = EncouragedDespawnTime;

            if (velocityX == 0f)
            {
                if (velocityY == 0f)
                {
                    ai0++;
                    if (ai0 >= 2f)
                    {
                        directionX *= -1;
                        spriteDirection = directionX;
                        ai0 = 0f;
                    }
                }
            }
            else
            {
                ai0 = 0f;
            }

            if (directionX == 0)
                directionX = 1;
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
            targetRefreshes)
        {
            TimeLeft = timeLeft,
            SpriteDirection = spriteDirection
        };
        return true;

        void RefreshTarget()
        {
            targetRefreshes++;
            if (!closestTarget.HasTarget)
                return;

            target = closestTarget.Target;
            directionX = closestTarget.DirectionX;
            directionY = closestTarget.DirectionY;
        }
    }
}
