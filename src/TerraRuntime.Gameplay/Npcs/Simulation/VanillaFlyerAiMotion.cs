using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaFlyerAiMotionInput(
    float PositionY,
    float NpcCenterX,
    float NpcCenterY,
    float VelocityX,
    float VelocityY,
    float TargetCenterX,
    float TargetCenterY,
    float TargetTopY,
    float OldVelocityX,
    float OldVelocityY,
    int DirectionX,
    NpcAiState Ai,
    float Scale,
    bool CollideX,
    bool CollideY,
    bool Wet,
    bool DayTime,
    bool ExpertMode,
    double WorldSurfacePixels,
    int TimeLeft);

public readonly record struct VanillaFlyerAiMotionResult(
    float VelocityX,
    float VelocityY,
    NpcAiState Ai,
    int TimeLeft);

/// <summary>
/// Clean-room deterministic TerrariaServer 1.4.5.8 AI_005_EaterOfSouls movement/state slice.
/// This owns source-ordered family jitter, close homing, Bee acceleration ramp, surface Hornet
/// damping, collision bounce minima, wet rise and daylight/despawn motion. Projectile/NPC spawn
/// side effects and presentation-only rotation/dust remain outside this pure state primitive.
/// </summary>
public static class VanillaFlyerAiMotion
{
    private const float CoordinateQuantum = 8f;
    private const float JitterAcceleration = 0.023f;
    private const float CloseHomingScale = 0.007f;

    public static bool TryStep(
        NpcTypeId type,
        in VanillaFlyerAiMotionInput input,
        in VanillaFlyerMotionProfile baseProfile,
        out VanillaFlyerAiMotionResult result)
    {
        if (!float.IsFinite(input.PositionY) ||
            !float.IsFinite(input.NpcCenterX) ||
            !float.IsFinite(input.NpcCenterY) ||
            !float.IsFinite(input.VelocityX) ||
            !float.IsFinite(input.VelocityY) ||
            !float.IsFinite(input.TargetCenterX) ||
            !float.IsFinite(input.TargetCenterY) ||
            !float.IsFinite(input.TargetTopY) ||
            !float.IsFinite(input.OldVelocityX) ||
            !float.IsFinite(input.OldVelocityY) ||
            !float.IsFinite(input.Scale) ||
            input.Scale <= 0f ||
            input.DirectionX is < -1 or > 1 ||
            !input.Ai.IsFinite ||
            input.TimeLeft < -1 ||
            double.IsNaN(input.WorldSurfacePixels) ||
            input.WorldSurfacePixels <= 0d ||
            (double.IsInfinity(input.WorldSurfacePixels) && !double.IsPositiveInfinity(input.WorldSurfacePixels)) ||
            !baseProfile.IsValid)
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float maximumSpeed = baseProfile.MaximumSpeed;
        float acceleration = baseProfile.Acceleration;
        int timeLeft = input.TimeLeft;

        if (type == VanillaNpcIds.EaterOfSouls)
            acceleration = input.ExpertMode ? 0.035f : 0.02f;

        if (type == VanillaNpcIds.Bee || type == VanillaNpcIds.SmallBee)
        {
            ai1++;
            float ramp = (ai1 - 60f) / 60f;
            if (ramp > 1f)
            {
                ramp = 1f;
            }
            else
            {
                velocityX = Math.Clamp(velocityX, -6f, 6f);
                velocityY = Math.Clamp(velocityY, -6f, 6f);
            }

            maximumSpeed = 5f;
            acceleration = 0.1f * ramp;
        }

        if (IsSurfaceHornet(type) && input.PositionY < input.WorldSurfacePixels)
        {
            float targetVerticalOffset = input.TargetTopY - input.PositionY;
            if (targetVerticalOffset > 300f && velocityY < 0f)
                velocityY *= 0.97f;
            if (targetVerticalOffset < 80f && velocityY > 0f)
                velocityY *= 0.97f;
        }

        if (type == VanillaNpcIds.BloodSquid && input.DayTime)
        {
            velocityY -= 0.3f;
            timeLeft = EncourageDespawn(timeLeft, 60);
        }

        float sourceX = Quantize(input.NpcCenterX);
        float sourceY = Quantize(input.NpcCenterY);
        float targetX = Quantize(input.TargetCenterX);
        float targetY = Quantize(input.TargetCenterY);
        float deltaX = targetX - sourceX;
        float deltaY = targetY - sourceY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);

        float desiredX;
        float desiredY;
        if (distance == 0f)
        {
            desiredX = velocityX;
            desiredY = velocityY;
        }
        else
        {
            float seekScale = maximumSpeed / distance;
            desiredX = deltaX * seekScale;
            desiredY = deltaY * seekScale;
        }

        bool secondaryJitterFamily = IsSecondaryJitterFamily(type);
        if ((IsPrimaryJitterFamily(type) || secondaryJitterFamily) &&
            (distance > 100f || secondaryJitterFamily))
        {
            ai0++;
            velocityY += ai0 > 0f ? JitterAcceleration : -JitterAcceleration;
            velocityX += ai0 < -100f || ai0 > 100f
                ? JitterAcceleration
                : -JitterAcceleration;
            if (ai0 > 200f)
                ai0 = -200f;
        }

        if (distance < 150f && UsesCloseHoming(type))
        {
            velocityX += desiredX * CloseHomingScale;
            velocityY += desiredY * CloseHomingScale;
        }

        if (type == VanillaNpcIds.BloodSquid &&
            input.NpcCenterY > input.TargetCenterY - 200f)
        {
            velocityY -= 0.3f;
        }

        ApproachAxis(ref velocityX, desiredX, acceleration, baseProfile.TurnsHard);
        ApproachAxis(ref velocityY, desiredY, acceleration, baseProfile.TurnsHard);

        if (baseProfile.HasBounce)
        {
            if (input.CollideX)
            {
                velocityX = input.OldVelocityX * -baseProfile.BounceFactor;
                if (input.DirectionX == -1 && velocityX > 0f && velocityX < 2f)
                    velocityX = 2f;
                if (input.DirectionX == 1 && velocityX < 0f && velocityX > -2f)
                    velocityX = -2f;
            }

            if (input.CollideY)
            {
                velocityY = input.OldVelocityY * -baseProfile.BounceFactor;
                if (velocityY > 0f && velocityY < 1.5f)
                    velocityY = 2f;
                if (velocityY < 0f && velocityY > -1.5f)
                    velocityY = -2f;
            }
        }

        if (input.Wet && baseProfile.RisesInWater)
        {
            if (velocityY > 0f)
                velocityY *= 0.95f;
            velocityY -= baseProfile.WaterRiseAcceleration;
            if (velocityY < -baseProfile.WaterRiseSpeedCap)
                velocityY = -baseProfile.WaterRiseSpeedCap;
        }

        if (input.DayTime && !IsDaylightExempt(type))
        {
            velocityY -= acceleration * 2f;
            timeLeft = EncourageDespawn(timeLeft, 10);
        }

        result = new VanillaFlyerAiMotionResult(
            velocityX,
            velocityY,
            new NpcAiState(ai0, ai1, input.Ai.Ai2, input.Ai.Ai3),
            timeLeft);
        return true;
    }

    private static float Quantize(float value) =>
        (int)(value / CoordinateQuantum) * CoordinateQuantum;

    private static int EncourageDespawn(int timeLeft, int maximum) =>
        timeLeft >= 0 && timeLeft > maximum ? maximum : timeLeft;

    private static void ApproachAxis(
        ref float velocity,
        float desired,
        float acceleration,
        bool turnsHard)
    {
        if (velocity < desired)
        {
            velocity += acceleration;
            if (turnsHard && velocity < 0f && desired > 0f)
                velocity += acceleration;
        }
        else if (velocity > desired)
        {
            velocity -= acceleration;
            if (turnsHard && velocity > 0f && desired < 0f)
                velocity -= acceleration;
        }
    }

    private static bool IsPrimaryJitterFamily(NpcTypeId type) =>
        type == VanillaNpcIds.EaterOfSouls ||
        type == VanillaNpcIds.Probe ||
        type == VanillaNpcIds.Crimera ||
        type == VanillaNpcIds.Moth;

    private static bool IsSecondaryJitterFamily(NpcTypeId type) =>
        type == VanillaNpcIds.Hornet ||
        type == VanillaNpcIds.Corruptor ||
        type == VanillaNpcIds.BloodSquid ||
        type == VanillaNpcIds.MossHornet ||
        type == VanillaNpcIds.Bee ||
        type == VanillaNpcIds.SmallBee ||
        IsHornetVariant(type);

    private static bool UsesCloseHoming(NpcTypeId type) =>
        type == VanillaNpcIds.EaterOfSouls ||
        type == VanillaNpcIds.Corruptor ||
        type == VanillaNpcIds.Crimera ||
        type == VanillaNpcIds.BloodSquid;

    private static bool IsSurfaceHornet(NpcTypeId type) =>
        type == VanillaNpcIds.Hornet || IsHornetVariant(type);

    private static bool IsHornetVariant(NpcTypeId type) =>
        type == VanillaNpcIds.FattyHornet ||
        type == VanillaNpcIds.HoneyHornet ||
        type == VanillaNpcIds.LeafyHornet ||
        type == VanillaNpcIds.SpikeyHornet ||
        type == VanillaNpcIds.StingyHornet;

    private static bool IsDaylightExempt(NpcTypeId type) =>
        type == VanillaNpcIds.Crimera ||
        type == VanillaNpcIds.BloodSquid ||
        type == VanillaNpcIds.EaterOfSouls ||
        type == VanillaNpcIds.MeteorHead ||
        type == VanillaNpcIds.Hornet ||
        type == VanillaNpcIds.Corruptor ||
        type == VanillaNpcIds.MossHornet ||
        type == VanillaNpcIds.Moth ||
        type == VanillaNpcIds.Bee ||
        type == VanillaNpcIds.SmallBee ||
        type == VanillaNpcIds.Parrot ||
        IsHornetVariant(type);
}
