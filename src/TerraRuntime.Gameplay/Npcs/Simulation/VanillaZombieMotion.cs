using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

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
    public bool ScaleAdjustsMaximumHorizontalSpeed { get; init; } = true;
    public float ReversingVelocityDamping { get; init; } = 1f;
    public VanillaGroundFighterMotionProfile MotionProfile { get; init; } = VanillaGroundFighterMotionProfile.Standard;
    public int Life { get; init; } = 1;
    public int LifeMax { get; init; } = 1;
    public float HalfHealthSpeedMultiplier { get; init; } = 1f;
    public float OverspeedGroundDamping { get; init; } = .8f;
    public float MissingHealthSpeedBonus { get; init; }
    public float MissingHealthAccelerationBonus { get; init; }
    public bool ArmedAttackCanStart { get; init; }
    public bool ArmedAttackMustEnd { get; init; }
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

/// <summary>Type-specific horizontal movement branches inside TerrariaServer 1.4.5.8 AI_003_Fighters.</summary>
public enum VanillaGroundFighterMotionProfile : byte
{
    Standard = 0,
    MoonEventLeaper = 1,
    HalfHealthBerserker = 2,
    MissingHealthBerserker = 3,
    ArmedZombie = 4,
    Crawdad = 5,
    Salamander = 6,
    TacticalSkeleton = 7,
    SkeletonSniper = 8,
    SkeletonCommando = 9
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
            !Enum.IsDefined(input.MotionProfile) ||
            input.Life < 0 || input.LifeMax <= 0 || input.Life > input.LifeMax ||
            !float.IsFinite(input.HalfHealthSpeedMultiplier) || input.HalfHealthSpeedMultiplier <= 0f ||
            !float.IsFinite(input.OverspeedGroundDamping) || input.OverspeedGroundDamping is <= 0f or > 1f ||
            !float.IsFinite(input.MissingHealthSpeedBonus) || input.MissingHealthSpeedBonus < 0f ||
            !float.IsFinite(input.MissingHealthAccelerationBonus) || input.MissingHealthAccelerationBonus < 0f ||
            !float.IsFinite(input.ReversingVelocityDamping) ||
            input.ReversingVelocityDamping <= 0f || input.ReversingVelocityDamping > 1f ||
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

        bool armedAttackTick = input.MotionProfile is VanillaGroundFighterMotionProfile.ArmedZombie or VanillaGroundFighterMotionProfile.Crawdad && ai2 > 0f;
        if (armedAttackTick)
        {
            // AI_003_Fighters: armed zombies retain the common target/stuck prepass, then brake for twenty
            // ticks while their melee damage is raised by the owning strategy.
            ai3 = 1f;
            velocityX *= .9f;
            if (MathF.Abs(velocityX) < .1f)
                velocityX = 0f;
            ai2++;
            if (ai2 >= 20f || velocityY != 0f || input.ArmedAttackMustEnd)
                ai2 = 0f;
        }

        float maximumSpeed = input.BaseMaximumHorizontalSpeed;
        if (input.ScaleAdjustsMaximumHorizontalSpeed)
            maximumSpeed *= 1f + (1f - input.Scale);
        if ((velocityX > 0f && directionX < 0) || (velocityX < 0f && directionX > 0))
            velocityX *= input.ReversingVelocityDamping;
        if (armedAttackTick)
        {
            // The source's armed branch owns horizontal motion for this tick.
        }
        else if (input.MotionProfile == VanillaGroundFighterMotionProfile.Salamander ||
                 (input.MotionProfile is VanillaGroundFighterMotionProfile.TacticalSkeleton or VanillaGroundFighterMotionProfile.SkeletonSniper or VanillaGroundFighterMotionProfile.SkeletonCommando && ai2 > 0f))
        {
            // AI_003's stationary ranged branches take over after the shared target/stuck prepass.
        }
        else if (input.MotionProfile == VanillaGroundFighterMotionProfile.MoonEventLeaper)
        {
            if (velocityY == 0f)
            {
                velocityX *= 0.85f;
                if (velocityX is > -0.3f and < 0.3f)
                {
                    velocityY = -7f;
                    velocityX = maximumSpeed * directionX;
                }
            }
            else if (spriteDirection == directionX)
            {
                velocityX = (velocityX * 10f + maximumSpeed * directionX) / 11f;
            }
        }
        else if (input.MotionProfile == VanillaGroundFighterMotionProfile.HalfHealthBerserker)
        {
            float halfHealthFactor = input.Life < input.LifeMax / 2 ? 2f : 1f;
            maximumSpeed *= halfHealthFactor * input.HalfHealthSpeedMultiplier;
            float acceleration = input.HorizontalAcceleration * halfHealthFactor;
            ApplyStandardMotion(maximumSpeed, acceleration, input.OverspeedGroundDamping);
        }
        else if (input.MotionProfile == VanillaGroundFighterMotionProfile.MissingHealthBerserker)
        {
            float missingHealth = 1f - input.Life / (float)input.LifeMax;
            maximumSpeed += missingHealth * input.MissingHealthSpeedBonus;
            float acceleration = input.HorizontalAcceleration + missingHealth * input.MissingHealthAccelerationBonus;
            ApplyStandardMotion(maximumSpeed, acceleration, input.OverspeedGroundDamping);
        }
        else
        {
            if (velocityX < -maximumSpeed || velocityX > maximumSpeed)
            {
                if (velocityY == 0f)
                    velocityX *= input.OverspeedGroundDamping;
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
        }

        if (input.MotionProfile is VanillaGroundFighterMotionProfile.ArmedZombie or VanillaGroundFighterMotionProfile.Crawdad && !armedAttackTick && input.ArmedAttackCanStart)
        {
            velocityX *= .7f;
            ai2 = 1f;
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

        void ApplyStandardMotion(float maximumSpeed, float acceleration, float groundDamping)
        {
            if (velocityX < -maximumSpeed || velocityX > maximumSpeed)
            {
                if (velocityY == 0f)
                    velocityX *= groundDamping;
            }
            else if (velocityX < maximumSpeed && directionX == 1)
            {
                velocityX = MathF.Min(velocityX + acceleration, maximumSpeed);
            }
            else if (velocityX > -maximumSpeed && directionX == -1)
            {
                velocityX = MathF.Max(velocityX - acceleration, -maximumSpeed);
            }
        }

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
    float PursuitGapSpeedMultiplier,
    bool ScaleAdjustsMaximumHorizontalSpeed = false,
    bool CloseRangeLunge = false,
    float ReversingVelocityDamping = 1f,
    VanillaGroundFighterMotionProfile MotionProfile = VanillaGroundFighterMotionProfile.Standard,
    float HalfHealthSpeedMultiplier = 1f,
    float OverspeedGroundDamping = .8f,
    float MissingHealthSpeedBonus = 0f,
    float MissingHealthAccelerationBonus = 0f,
    bool DaySurfaceEncouragesDespawn = true)
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
        float.IsFinite(PursuitGapSpeedMultiplier) && PursuitGapSpeedMultiplier > 0f &&
        float.IsFinite(ReversingVelocityDamping) && ReversingVelocityDamping is > 0f and <= 1f &&
        Enum.IsDefined(MotionProfile) &&
        float.IsFinite(HalfHealthSpeedMultiplier) && HalfHealthSpeedMultiplier > 0f &&
        float.IsFinite(OverspeedGroundDamping) && OverspeedGroundDamping is > 0f and <= 1f &&
        float.IsFinite(MissingHealthSpeedBonus) && MissingHealthSpeedBonus >= 0f &&
        float.IsFinite(MissingHealthAccelerationBonus) && MissingHealthAccelerationBonus >= 0f;

    private static bool IsJumpVelocity(float velocity) => float.IsFinite(velocity) && velocity < 0f;
}

/// <summary>Source-backed AI_003 movement/traversal profiles for explicitly admitted NPC definitions.</summary>
public static class VanillaGroundFighterBehaviorCatalog
{
    public static bool TryGet(NpcTypeId type, out VanillaGroundFighterBehaviorParameters parameters) =>
        VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out parameters) ||
        VanillaMoonEventGroundFighterCatalog1458.TryGetBehavior(type, out parameters);
}
