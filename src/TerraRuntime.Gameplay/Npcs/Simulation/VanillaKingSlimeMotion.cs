using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaKingSlimeLocalAi(
    float TeleportPressure,
    float TeleportBottomX,
    float TeleportBottomY,
    float Initialized)
{
    public bool IsFinite =>
        float.IsFinite(TeleportPressure) &&
        float.IsFinite(TeleportBottomX) &&
        float.IsFinite(TeleportBottomY) &&
        float.IsFinite(Initialized);
}

public readonly record struct VanillaKingSlimeMotionInput(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int DirectionX,
    ushort Target,
    NpcAiState Ai,
    VanillaKingSlimeLocalAi LocalAi,
    int Life,
    int LifeMax,
    int TimeLeft,
    float Scale,
    bool GoodWorld,
    bool CanHitTarget,
    VanillaNpcTargetCandidate TargetCandidate,
    VanillaNpcTargetCandidate ClosestCandidate,
    bool HasTeleportDestination,
    float TeleportBottomX,
    float TeleportBottomY,
    float WorldPixelWidth,
    float WorldPixelHeight);

public readonly record struct VanillaKingSlimeMotionResult(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int DirectionX,
    ushort Target,
    NpcAiState Ai,
    VanillaKingSlimeLocalAi LocalAi,
    int TimeLeft,
    float Scale,
    bool Hidden,
    bool DontTakeDamage,
    bool MinionBurstRequested);

/// <summary>
/// Allocation-free server-authoritative state primitive for TerrariaServer 1.4.5.8 King Slime aiStyle 15.
/// It owns synchronized AI slots, jump cadence, teleport shrink/grow timing, scale/position preservation and
/// the 5%-life minion-burst threshold. Teleport spot discovery and random child materialization are deliberately
/// external effects: callers must provide a resolved destination before the source transition that enters ai[1]=5.
/// </summary>
public static class VanillaKingSlimeMotion
{
    public const float TeleportTriggerTicks = 300f;
    public const float TeleportPressureLimit = 360f;
    public const float TeleportAntiCheeseDistance = 2000f;
    public const float TargetDespawnDistance = 3000f;
    public const float TeleportShrinkTicks = 60f;
    public const float TeleportGrowTicks = 30f;
    public const float BaseScaleMinimum = 0.75f;
    public const float BaseScaleLifeFactor = 0.5f;
    public const float MinionBurstLifeFraction = 0.05f;

    private const int BaseWidth = 98;
    private const int BaseHeight = 92;
    private const float GroundFriction = 0.8f;
    private const float AirFriction = 0.93f;
    private const float AirAcceleration = 0.2f;
    private const float NormalAirSpeed = 3f;
    private const float GoodWorldAirSpeed = 6f;
    private const float VelocityStopEpsilon = 0.1f;
    private const int DespawnTime = 10;

    public static bool RequiresTeleportDestination(
        in VanillaKingSlimeMotionInput input,
        out bool antiCheese)
    {
        antiCheese = false;
        if (!IsValid(in input) || !TryGetCurrentTarget(in input, out VanillaNpcTargetCandidate target))
            return false;

        float centerX = input.PositionX + ResolveDimension(BaseWidth, input.Scale) * 0.5f;
        float centerY = input.PositionY + ResolveDimension(BaseHeight, input.Scale) * 0.5f;
        float distance = Distance(centerX, centerY, target.CenterX, target.CenterY);
        bool trigger =
            !target.Dead &&
            input.TimeLeft > DespawnTime &&
            input.Ai.Ai2 >= TeleportTriggerTicks &&
            input.Ai.Ai1 < 5f &&
            input.VelocityY == 0f;
        if (!trigger)
            return false;

        antiCheese = input.LocalAi.TeleportPressure >= TeleportPressureLimit ||
                     distance > TeleportAntiCheeseDistance;
        return true;
    }

    public static bool TryStep(
        in VanillaKingSlimeMotionInput input,
        out VanillaKingSlimeMotionResult result)
    {
        if (!IsValid(in input))
        {
            result = default;
            return false;
        }

        float positionX = input.PositionX;
        float positionY = input.PositionY;
        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        int directionX = input.DirectionX == 0 ? 1 : input.DirectionX;
        ushort target = input.Target;
        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        float ai2 = input.Ai.Ai2;
        float ai3 = input.Ai.Ai3;
        float local0 = input.LocalAi.TeleportPressure;
        float local1 = input.LocalAi.TeleportBottomX;
        float local2 = input.LocalAi.TeleportBottomY;
        float local3 = input.LocalAi.Initialized;
        int timeLeft = input.TimeLeft;
        float scale = input.Scale;
        bool minionBurstRequested = false;
        bool initializedThisTick = false;
        VanillaNpcTargetCandidate closestCandidate = input.ClosestCandidate;

        if (ai3 == 0f && input.Life > 0)
            ai3 = input.LifeMax;

        VanillaNpcTargetCandidate currentTarget = input.TargetCandidate;
        if (local3 == 0f)
        {
            local3 = 1f;
            initializedThisTick = true;
            ai0 = -100f;
            RefreshTarget();
        }

        if (TryGetUsableCandidate(currentTarget, out VanillaNpcTargetCandidate usableTarget))
        {
            float currentCenterX = positionX + ResolveDimension(BaseWidth, scale) * 0.5f;
            float currentCenterY = positionY + ResolveDimension(BaseHeight, scale) * 0.5f;
            float targetDistance = Distance(currentCenterX, currentCenterY, usableTarget.CenterX, usableTarget.CenterY);
            if (usableTarget.Dead || targetDistance > TargetDespawnDistance)
            {
                RefreshTarget();
                if (TryGetUsableCandidate(currentTarget, out usableTarget))
                {
                    currentCenterX = positionX + ResolveDimension(BaseWidth, scale) * 0.5f;
                    currentCenterY = positionY + ResolveDimension(BaseHeight, scale) * 0.5f;
                    targetDistance = Distance(currentCenterX, currentCenterY, usableTarget.CenterX, usableTarget.CenterY);
                }

                if (!TryGetUsableCandidate(currentTarget, out usableTarget) ||
                    usableTarget.Dead ||
                    targetDistance > TargetDespawnDistance)
                {
                    timeLeft = timeLeft < 0 ? DespawnTime : Math.Min(timeLeft, DespawnTime);
                    if (TryGetUsableCandidate(currentTarget, out usableTarget))
                        directionX = usableTarget.CenterX < currentCenterX ? 1 : -1;
                    ai2 = 0f;
                    ai0 = 0f;
                    ai1 = 5f;
                    local1 = input.WorldPixelWidth;
                    local2 = input.WorldPixelHeight;
                }
            }
        }
        else
        {
            RefreshTarget();
        }

        if (TryGetUsableCandidate(currentTarget, out usableTarget) &&
            !usableTarget.Dead &&
            timeLeft > DespawnTime &&
            ai2 >= TeleportTriggerTicks &&
            ai1 < 5f &&
            velocityY == 0f)
        {
            if (!input.HasTeleportDestination)
            {
                result = default;
                return false;
            }

            ai2 = 0f;
            ai0 = 0f;
            ai1 = 5f;
            local1 = input.TeleportBottomX;
            local2 = input.TeleportBottomY;
        }

        if (TryGetUsableCandidate(currentTarget, out usableTarget))
        {
            float currentTopY = positionY;
            float targetBottomY = usableTarget.CenterY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
            if (!input.CanHitTarget || MathF.Abs(currentTopY - targetBottomY) > 160f)
            {
                ai2++;
                local0++;
            }
            else
            {
                local0--;
                if (local0 < 0f)
                    local0 = 0f;
            }
        }

        if (timeLeft is >= 0 and < DespawnTime && (ai0 != 0f || ai1 != 0f))
        {
            ai0 = 0f;
            ai1 = 0f;
        }

        bool teleporting = false;
        bool hidden = false;
        float teleportScaleFactor = 1f;
        if (ai1 == 5f)
        {
            teleporting = true;
            ai0++;
            teleportScaleFactor = 0.5f + Math.Clamp((TeleportShrinkTicks - ai0) / TeleportShrinkTicks, 0f, 1f) * 0.5f;
            hidden = ai0 >= TeleportShrinkTicks;
            if (ai0 >= TeleportShrinkTicks)
            {
                SetBottomCenter(ref positionX, ref positionY, scale, local1, local2);
                ai1 = 6f;
                ai0 = 0f;
            }
        }
        else if (ai1 == 6f)
        {
            teleporting = true;
            ai0++;
            teleportScaleFactor = 0.5f + Math.Clamp(ai0 / TeleportGrowTicks, 0f, 1f) * 0.5f;
            if (ai0 >= TeleportGrowTicks)
            {
                ai1 = 0f;
                ai0 = 0f;
                RefreshTarget();
            }
        }

        if (velocityY == 0f)
        {
            velocityX *= GroundFriction;
            if (velocityX > -VelocityStopEpsilon && velocityX < VelocityStopEpsilon)
                velocityX = 0f;

            if (!teleporting)
            {
                ai0 += 2f;
                if ((float)input.Life < input.LifeMax * 0.8f) ai0++;
                if ((float)input.Life < input.LifeMax * 0.6f) ai0++;
                if ((float)input.Life < input.LifeMax * 0.4f) ai0 += 2f;
                if ((float)input.Life < input.LifeMax * 0.2f) ai0 += 3f;
                if ((float)input.Life < input.LifeMax * 0.1f) ai0 += 4f;

                if (ai0 >= 0f)
                {
                    RefreshTarget();
                    if (ai1 == 3f)
                    {
                        velocityY = -13f;
                        velocityX += 3.5f * directionX;
                        ai0 = -200f;
                        ai1 = 0f;
                    }
                    else if (ai1 == 2f)
                    {
                        velocityY = -6f;
                        velocityX += 4.5f * directionX;
                        ai0 = -120f;
                        ai1++;
                    }
                    else
                    {
                        velocityY = -8f;
                        velocityX += 4f * directionX;
                        ai0 = -120f;
                        ai1++;
                    }
                }
            }
        }
        else if (target < byte.MaxValue)
        {
            float maximumSpeed = input.GoodWorld ? GoodWorldAirSpeed : NormalAirSpeed;
            if ((directionX == 1 && velocityX < maximumSpeed) ||
                (directionX == -1 && velocityX > -maximumSpeed))
            {
                if ((directionX == -1 && velocityX < 0.1f) ||
                    (directionX == 1 && velocityX > -0.1f))
                    velocityX += AirAcceleration * directionX;
                else
                    velocityX *= AirFriction;
            }
        }

        if (input.Life > 0)
        {
            float lifeRatio = (float)input.Life / input.LifeMax;
            float difficultyScale = input.GoodWorld ? 1f + lifeRatio : 1f;
            float nextScale = (lifeRatio * BaseScaleLifeFactor + BaseScaleMinimum) *
                              teleportScaleFactor *
                              difficultyScale;
            if (nextScale != scale || initializedThisTick)
            {
                PreserveBottomCenterAcrossScale(ref positionX, ref positionY, scale, nextScale);
                scale = nextScale;
            }

            int minionThreshold = (int)(input.LifeMax * MinionBurstLifeFraction);
            if ((float)(input.Life + minionThreshold) < ai3)
            {
                ai3 = input.Life;
                minionBurstRequested = true;
            }
        }

        result = new VanillaKingSlimeMotionResult(
            positionX,
            positionY,
            velocityX,
            velocityY,
            directionX,
            target,
            new NpcAiState(ai0, ai1, ai2, ai3),
            new VanillaKingSlimeLocalAi(local0, local1, local2, local3),
            timeLeft,
            scale,
            hidden,
            hidden,
            minionBurstRequested);
        return true;

        void RefreshTarget()
        {
            if (!TryGetUsableCandidate(closestCandidate, out VanillaNpcTargetCandidate closest))
                return;

            currentTarget = closest;
            target = closest.Slot;
            float centerX = positionX + ResolveDimension(BaseWidth, scale) * 0.5f;
            directionX = closest.CenterX < centerX ? -1 : 1;
        }
    }

    private static bool IsValid(in VanillaKingSlimeMotionInput input) =>
        float.IsFinite(input.PositionX) &&
        float.IsFinite(input.PositionY) &&
        float.IsFinite(input.VelocityX) &&
        float.IsFinite(input.VelocityY) &&
        input.DirectionX is >= -1 and <= 1 &&
        input.Target <= byte.MaxValue &&
        input.Ai.IsFinite &&
        input.LocalAi.IsFinite &&
        input.LifeMax > 0 &&
        input.Life >= 0 &&
        input.Life <= input.LifeMax &&
        input.TimeLeft >= -1 &&
        float.IsFinite(input.Scale) && input.Scale > 0f &&
        float.IsFinite(input.TeleportBottomX) &&
        float.IsFinite(input.TeleportBottomY) &&
        float.IsFinite(input.WorldPixelWidth) && input.WorldPixelWidth > 0f &&
        float.IsFinite(input.WorldPixelHeight) && input.WorldPixelHeight > 0f &&
        IsCandidateFinite(input.TargetCandidate) &&
        IsCandidateFinite(input.ClosestCandidate);

    private static bool TryGetCurrentTarget(
        in VanillaKingSlimeMotionInput input,
        out VanillaNpcTargetCandidate target)
    {
        if (TryGetUsableCandidate(input.TargetCandidate, out target) && target.Slot == input.Target)
            return true;
        if (TryGetUsableCandidate(input.ClosestCandidate, out target))
            return true;
        target = default;
        return false;
    }

    private static bool TryGetUsableCandidate(
        VanillaNpcTargetCandidate candidate,
        out VanillaNpcTargetCandidate usable)
    {
        if (candidate.Active && !candidate.Ghost &&
            float.IsFinite(candidate.CenterX) && float.IsFinite(candidate.CenterY))
        {
            usable = candidate;
            return true;
        }
        usable = default;
        return false;
    }

    private static bool IsCandidateFinite(VanillaNpcTargetCandidate candidate) =>
        float.IsFinite(candidate.CenterX) && float.IsFinite(candidate.CenterY);

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static int ResolveDimension(int baseDimension, float scale) =>
        Math.Max(1, (int)MathF.Floor(baseDimension * scale));

    private static void PreserveBottomCenterAcrossScale(
        ref float positionX,
        ref float positionY,
        float oldScale,
        float newScale)
    {
        int oldWidth = ResolveDimension(BaseWidth, oldScale);
        int oldHeight = ResolveDimension(BaseHeight, oldScale);
        int newWidth = ResolveDimension(BaseWidth, newScale);
        int newHeight = ResolveDimension(BaseHeight, newScale);
        positionX += oldWidth / 2;
        positionY += oldHeight;
        positionX -= newWidth / 2;
        positionY -= newHeight;
    }

    private static void SetBottomCenter(
        ref float positionX,
        ref float positionY,
        float scale,
        float bottomX,
        float bottomY)
    {
        int width = ResolveDimension(BaseWidth, scale);
        int height = ResolveDimension(BaseHeight, scale);
        positionX = bottomX - width / 2f;
        positionY = bottomY - height;
    }
}
