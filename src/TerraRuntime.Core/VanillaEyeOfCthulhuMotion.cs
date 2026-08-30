using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaEyeOfCthulhuMotionInput(
    float NpcCenterX,
    float NpcCenterY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai,
    int Life,
    int LifeMax,
    int TimeLeft,
    bool DayTime,
    bool TargetAvailable,
    bool TargetDead,
    float TargetCenterX,
    float TargetCenterY);

public readonly record struct VanillaEyeOfCthulhuMotionResult(
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai,
    int TimeLeft);

/// <summary>
/// Allocation-free classic-mode state machine for TerrariaServer 1.4.5.8 Eye of Cthulhu aiStyle 4.
/// Cosmetic rotation/dust/sounds are deliberately absent. Servant spawning is also deliberately not emitted
/// from this state-only primitive: NPC spawn side effects must be committed by the runtime side-effect layer.
/// Expert/Master/getGoodWorld branches are not silently approximated by this classic-mode implementation.
/// </summary>
public static class VanillaEyeOfCthulhuMotion
{
    private const float PhaseOneHoverSpeed = 5f;
    private const float PhaseOneHoverAcceleration = 0.04f;
    private const float PhaseOneHoverOffsetY = 200f;
    private const float PhaseOneHoverTicks = 600f;
    private const float PhaseOneDashSpeed = 6f;
    private const float PhaseOneDashSlowdownStart = 40f;
    private const float PhaseOneDashTicks = 150f;
    private const float PhaseOneDashSlowdown = 0.98f;

    private const float TransformationTicks = 100f;
    private const float TransformationSpinAcceleration = 0.005f;
    private const float TransformationMaximumSpin = 0.5f;
    private const float TransformationVelocityScale = 0.98f;

    private const float PhaseTwoHoverSpeed = 6f;
    private const float PhaseTwoHoverAcceleration = 0.07f;
    private const float PhaseTwoHoverOffsetY = 120f;
    private const float PhaseTwoHoverTicks = 200f;
    private const float PhaseTwoDashSpeed = 6.8f;
    private const float PhaseTwoDashSlowdownStart = 40f;
    private const float PhaseTwoDashTicks = 130f;
    private const float PhaseTwoDashSlowdown = 0.97f;

    private const float VelocityStopEpsilon = 0.1f;
    private const int RetreatDespawnTime = 10;

    public static bool TryStep(
        in VanillaEyeOfCthulhuMotionInput input,
        out VanillaEyeOfCthulhuMotionResult result)
    {
        if (!IsValid(in input))
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;
        int timeLeft = input.TimeLeft;

        if (input.DayTime || !input.TargetAvailable || input.TargetDead)
        {
            velocityY -= PhaseOneHoverAcceleration;
            timeLeft = timeLeft < 0
                ? RetreatDespawnTime
                : Math.Min(timeLeft, RetreatDespawnTime);
            result = Build(velocityX, velocityY, target, ai0, ai1, ai2, ai3, timeLeft);
            return true;
        }

        if (ai0 == 0f)
        {
            if (ai1 == 0f)
            {
                SteerToward(
                    input.NpcCenterX,
                    input.NpcCenterY,
                    input.TargetCenterX,
                    input.TargetCenterY - PhaseOneHoverOffsetY,
                    PhaseOneHoverSpeed,
                    PhaseOneHoverAcceleration,
                    ref velocityX,
                    ref velocityY);

                ai2++;
                if (ai2 >= PhaseOneHoverTicks)
                {
                    ai1 = 1f;
                    ai2 = 0f;
                    ai3 = 0f;
                    target = VanillaNpcDefinitionCatalog.DefaultTarget;
                }
            }
            else if (ai1 == 1f)
            {
                SetDirectVelocity(
                    input.NpcCenterX,
                    input.NpcCenterY,
                    input.TargetCenterX,
                    input.TargetCenterY,
                    PhaseOneDashSpeed,
                    ref velocityX,
                    ref velocityY);
                ai1 = 2f;
            }
            else if (ai1 == 2f)
            {
                ai2++;
                if (ai2 >= PhaseOneDashSlowdownStart)
                    SlowAndStop(ref velocityX, ref velocityY, PhaseOneDashSlowdown);

                if (ai2 >= PhaseOneDashTicks)
                {
                    ai3++;
                    ai2 = 0f;
                    target = VanillaNpcDefinitionCatalog.DefaultTarget;
                    if (ai3 >= 3f)
                    {
                        ai1 = 0f;
                        ai3 = 0f;
                    }
                    else
                    {
                        ai1 = 1f;
                    }
                }
            }
            else
            {
                result = default;
                return false;
            }

            if ((float)input.Life < input.LifeMax * 0.5f)
            {
                ai0 = 1f;
                ai1 = 0f;
                ai2 = 0f;
                ai3 = 0f;
            }

            result = Build(velocityX, velocityY, target, ai0, ai1, ai2, ai3, timeLeft);
            return true;
        }

        if (ai0 == 1f || ai0 == 2f)
        {
            if (ai0 == 1f || ai3 == 1f)
                ai2 = Math.Min(ai2 + TransformationSpinAcceleration, TransformationMaximumSpin);
            else
                ai2 = Math.Max(ai2 - TransformationSpinAcceleration, 0f);

            ai1++;
            if (ai1 >= TransformationTicks)
            {
                if (ai3 == 1f)
                {
                    ai3 = 0f;
                    ai1 = 0f;
                }
                else
                {
                    ai0++;
                    ai1 = 0f;
                    if (ai0 == 3f)
                        ai2 = 0f;
                }
            }

            SlowAndStop(ref velocityX, ref velocityY, TransformationVelocityScale);
            result = Build(velocityX, velocityY, target, ai0, ai1, ai2, ai3, timeLeft);
            return true;
        }

        if (ai0 != 3f)
        {
            result = default;
            return false;
        }

        if (ai1 == 0f)
        {
            SteerToward(
                input.NpcCenterX,
                input.NpcCenterY,
                input.TargetCenterX,
                input.TargetCenterY - PhaseTwoHoverOffsetY,
                PhaseTwoHoverSpeed,
                PhaseTwoHoverAcceleration,
                ref velocityX,
                ref velocityY);

            ai2++;
            if (ai2 >= PhaseTwoHoverTicks)
            {
                ai1 = 1f;
                ai2 = 0f;
                ai3 = 0f;
                target = VanillaNpcDefinitionCatalog.DefaultTarget;
            }
        }
        else if (ai1 == 1f)
        {
            SetDirectVelocity(
                input.NpcCenterX,
                input.NpcCenterY,
                input.TargetCenterX,
                input.TargetCenterY,
                PhaseTwoDashSpeed,
                ref velocityX,
                ref velocityY);
            ai1 = 2f;
        }
        else if (ai1 == 2f)
        {
            ai2++;
            if (ai2 >= PhaseTwoDashSlowdownStart)
                SlowAndStop(ref velocityX, ref velocityY, PhaseTwoDashSlowdown);

            if (ai2 >= PhaseTwoDashTicks)
            {
                ai3++;
                ai2 = 0f;
                target = VanillaNpcDefinitionCatalog.DefaultTarget;
                if (ai3 >= 3f)
                {
                    ai1 = 0f;
                    ai3 = 0f;
                }
                else
                {
                    ai1 = 1f;
                }
            }
        }
        else
        {
            // ai[1] 3/4/5 belong to expert-mode rapid-dash branches and must not be fabricated here.
            result = default;
            return false;
        }

        result = Build(velocityX, velocityY, target, ai0, ai1, ai2, ai3, timeLeft);
        return true;
    }

    private static bool IsValid(in VanillaEyeOfCthulhuMotionInput input) =>
        float.IsFinite(input.NpcCenterX) &&
        float.IsFinite(input.NpcCenterY) &&
        float.IsFinite(input.VelocityX) &&
        float.IsFinite(input.VelocityY) &&
        input.Ai.IsFinite &&
        input.LifeMax > 0 &&
        input.Life >= 0 &&
        input.Life <= input.LifeMax &&
        input.TimeLeft >= -1 &&
        (!input.TargetAvailable ||
         (float.IsFinite(input.TargetCenterX) && float.IsFinite(input.TargetCenterY)));

    private static VanillaEyeOfCthulhuMotionResult Build(
        float velocityX,
        float velocityY,
        ushort target,
        float ai0,
        float ai1,
        float ai2,
        float ai3,
        int timeLeft) =>
        new(
            velocityX,
            velocityY,
            target,
            new NpcAiState(ai0, ai1, ai2, ai3),
            timeLeft);

    private static void SteerToward(
        float sourceX,
        float sourceY,
        float targetX,
        float targetY,
        float speed,
        float acceleration,
        ref float velocityX,
        ref float velocityY)
    {
        GetDesiredVelocity(sourceX, sourceY, targetX, targetY, speed, out float desiredX, out float desiredY);
        ApproachAxis(ref velocityX, desiredX, acceleration);
        ApproachAxis(ref velocityY, desiredY, acceleration);
    }

    private static void SetDirectVelocity(
        float sourceX,
        float sourceY,
        float targetX,
        float targetY,
        float speed,
        ref float velocityX,
        ref float velocityY) =>
        GetDesiredVelocity(sourceX, sourceY, targetX, targetY, speed, out velocityX, out velocityY);

    private static void GetDesiredVelocity(
        float sourceX,
        float sourceY,
        float targetX,
        float targetY,
        float speed,
        out float velocityX,
        out float velocityY)
    {
        float deltaX = targetX - sourceX;
        float deltaY = targetY - sourceY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance <= float.Epsilon)
        {
            velocityX = 0f;
            velocityY = 0f;
            return;
        }

        float scale = speed / distance;
        velocityX = deltaX * scale;
        velocityY = deltaY * scale;
    }

    private static void ApproachAxis(ref float velocity, float desired, float acceleration)
    {
        if (velocity < desired)
        {
            velocity += acceleration;
            if (velocity < 0f && desired > 0f)
                velocity += acceleration;
        }
        else if (velocity > desired)
        {
            velocity -= acceleration;
            if (velocity > 0f && desired < 0f)
                velocity -= acceleration;
        }
    }

    private static void SlowAndStop(ref float velocityX, ref float velocityY, float scale)
    {
        velocityX *= scale;
        velocityY *= scale;
        if (velocityX > -VelocityStopEpsilon && velocityX < VelocityStopEpsilon)
            velocityX = 0f;
        if (velocityY > -VelocityStopEpsilon && velocityY < VelocityStopEpsilon)
            velocityY = 0f;
    }
}
