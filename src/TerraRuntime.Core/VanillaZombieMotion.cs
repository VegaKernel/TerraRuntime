using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;

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
    public float BaseMaximumHorizontalSpeed { get; init; } = 1f;
    public float HorizontalAcceleration { get; init; } = 0.07f;
    public float StuckThreshold { get; init; } = 60f;
    public float MaximumStuckCounter { get; init; } = 600f;
    public int EncouragedDespawnTime { get; init; } = 10;
    public bool PursuitAllowed { get; init; } = true;
    public bool EncourageDespawn { get; init; }
    public bool JustHit { get; init; }
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
/// Covers stuck accounting including justHit reset, pursuit/TargetClosest cadence, discouraged idle turning
/// including spriteDirection, lifetime clamping and profile-driven horizontal motion. Type-624 Gnome CanHit
/// pathing and other subtype branches are intentionally outside this baseline.
/// </summary>
public static class VanillaZombieMotion
{
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
            !float.IsFinite(input.BaseMaximumHorizontalSpeed) ||
            input.BaseMaximumHorizontalSpeed <= 0f ||
            !float.IsFinite(input.HorizontalAcceleration) ||
            input.HorizontalAcceleration <= 0f ||
            !float.IsFinite(input.StuckThreshold) ||
            input.StuckThreshold <= 0f ||
            !float.IsFinite(input.MaximumStuckCounter) ||
            input.MaximumStuckCounter < input.StuckThreshold ||
            input.EncouragedDespawnTime <= 0 ||
            input.DirectionX is < -1 or > 1 ||
            input.DirectionY is < -1 or > 1 ||
            input.SpriteDirection is < -1 or > 1 ||
            input.Target > byte.MaxValue ||
            input.TimeLeft < 0 ||
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

        if (input.PositionX == input.OldPositionX || ai3 >= input.StuckThreshold || reversingWhileGrounded)
            ai3++;
        else if (MathF.Abs(velocityX) > 0.9f && ai3 > 0f)
            ai3--;

        if (ai3 > input.MaximumStuckCounter)
            ai3 = 0f;
        if (input.JustHit)
            ai3 = 0f;
        if (input.TargetOverlaps)
            ai3 = 0f;

        bool pursue = ai3 < input.StuckThreshold && input.PursuitAllowed;
        if (pursue)
        {
            RefreshTarget();
            if (directionY > 0 && closestTarget.HasTarget && closestTarget.DirectionY < 0)
                directionY = -1;
        }
        else
        {
            if (input.EncourageDespawn && timeLeft > input.EncouragedDespawnTime)
                timeLeft = input.EncouragedDespawnTime;

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

        float maximumSpeed = input.BaseMaximumHorizontalSpeed * (1f + (1f - input.Scale));
        if (velocityX < -maximumSpeed || velocityX > maximumSpeed)
        {
            if (velocityY == 0f)
                velocityX *= 0.8f;
        }
        else if (velocityX < maximumSpeed && directionX == 1)
        {
            velocityX += input.HorizontalAcceleration;
            if (velocityX > maximumSpeed)
                velocityX = maximumSpeed;
        }
        else if (velocityX > -maximumSpeed && directionX == -1)
        {
            velocityX -= input.HorizontalAcceleration;
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

/// <summary>
/// Version-pinned ordinary AI_003 movement/traversal profile. Values are deliberately explicit so an admitted
/// fighter cannot silently inherit generic pursuit constants from either the AI or world-collision layer.
/// </summary>
public readonly record struct VanillaGroundFighterBehaviorParameters(
    float BaseMaximumHorizontalSpeed,
    float HorizontalAcceleration,
    float StuckThreshold,
    float MaximumStuckCounter,
    int EncouragedDespawnTime,
    float StuckHopVelocity,
    float LowStepJumpVelocity,
    float OneTileJumpVelocity,
    float TwoTileJumpVelocity,
    float ThreeTileJumpVelocity,
    float PursuitGapJumpVelocity,
    float PursuitGapSpeedMultiplier)
{
    public bool IsValid =>
        float.IsFinite(BaseMaximumHorizontalSpeed) && BaseMaximumHorizontalSpeed > 0f &&
        float.IsFinite(HorizontalAcceleration) && HorizontalAcceleration > 0f &&
        float.IsFinite(StuckThreshold) && StuckThreshold > 0f &&
        float.IsFinite(MaximumStuckCounter) && MaximumStuckCounter >= StuckThreshold &&
        EncouragedDespawnTime > 0 &&
        IsJumpVelocity(StuckHopVelocity) &&
        IsJumpVelocity(LowStepJumpVelocity) &&
        IsJumpVelocity(OneTileJumpVelocity) &&
        IsJumpVelocity(TwoTileJumpVelocity) &&
        IsJumpVelocity(ThreeTileJumpVelocity) &&
        IsJumpVelocity(PursuitGapJumpVelocity) &&
        float.IsFinite(PursuitGapSpeedMultiplier) && PursuitGapSpeedMultiplier > 0f;

    private static bool IsJumpVelocity(float velocity) => float.IsFinite(velocity) && velocity < 0f;
}

/// <summary>Source-backed AI_003 movement/traversal profiles for explicitly admitted NPC definitions.</summary>
public static class VanillaGroundFighterBehaviorCatalog
{
    public static bool TryGet(NpcTypeId type, out VanillaGroundFighterBehaviorParameters parameters)
    {
        if (type == VanillaNpcIds.Zombie)
        {
            parameters = CreateOrdinaryProfile(1f);
            return true;
        }

        if (type == VanillaNpcIds.Skeleton)
        {
            parameters = CreateOrdinaryProfile(1.5f);
            return true;
        }

        parameters = default;
        return false;
    }

    private static VanillaGroundFighterBehaviorParameters CreateOrdinaryProfile(float maximumHorizontalSpeed) =>
        new(
            BaseMaximumHorizontalSpeed: maximumHorizontalSpeed,
            HorizontalAcceleration: 0.07f,
            StuckThreshold: 60f,
            MaximumStuckCounter: 600f,
            EncouragedDespawnTime: 10,
            StuckHopVelocity: -5f,
            LowStepJumpVelocity: -5f,
            OneTileJumpVelocity: -6f,
            TwoTileJumpVelocity: -7f,
            ThreeTileJumpVelocity: -8f,
            PursuitGapJumpVelocity: -8f,
            PursuitGapSpeedMultiplier: 1.5f);
}
