using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaFlyingEyeLifecycleInput(
    float PositionY,
    float VelocityY,
    NpcAiState Ai,
    int TimeLeft,
    bool NoTileCollide,
    bool DayTime,
    double WorldSurfacePixels,
    bool TargetInGraveyard,
    bool HasLineOfSight,
    bool SolidCollision);

public readonly record struct VanillaFlyingEyeLifecycleResult(
    NpcAiState Ai,
    int TimeLeft,
    bool NoTileCollide,
    bool Discouraged);

/// <summary>
/// Source-backed non-cosmetic lifecycle/state slice from TerrariaServer 1.4.5.8 AI_002_FloatingEye.
/// It owns daylight discouragement and Pigron ai[0]/ai[1] phasing. Collision response remains ordered
/// before this transition by the motion primitive, matching the source method.
/// </summary>
public static class VanillaFlyingEyeLifecycle
{
    public const float PigronPhaseDelayTicks = 300f;
    public const int DiscouragedTimeLeft = 10;

    public static bool TryStep(
        NpcTypeId type,
        in VanillaFlyingEyeLifecycleInput input,
        out VanillaFlyingEyeLifecycleResult result)
    {
        if (!float.IsFinite(input.PositionY) ||
            !float.IsFinite(input.VelocityY) ||
            !input.Ai.IsFinite ||
            input.TimeLeft < -1 ||
            double.IsNaN(input.WorldSurfacePixels) ||
            input.WorldSurfacePixels <= 0d ||
            (double.IsInfinity(input.WorldSurfacePixels) && !double.IsPositiveInfinity(input.WorldSurfacePixels)))
        {
            result = default;
            return false;
        }

        bool discouraged =
            VanillaFlyingEyeNpcCatalog.FleesDaylight(type) &&
            input.DayTime &&
            input.PositionY <= input.WorldSurfacePixels &&
            !input.TargetInGraveyard;

        float ai0 = input.Ai.Ai0;
        float ai1 = input.Ai.Ai1;
        bool noTileCollide = input.NoTileCollide;

        if (VanillaFlyingEyeNpcCatalog.IsPigron(type))
        {
            if (input.HasLineOfSight)
            {
                if (ai1 > 0f && !input.SolidCollision)
                {
                    ai1 = 0f;
                    ai0 = 0f;
                }
            }
            else if (ai1 == 0f)
            {
                ai0++;
            }

            if (ai0 >= PigronPhaseDelayTicks)
            {
                ai1 = 1f;
                ai0 = 0f;
            }

            noTileCollide = ai1 != 0f;
        }

        int timeLeft = input.TimeLeft;
        if (discouraged && timeLeft >= 0 && timeLeft > DiscouragedTimeLeft)
            timeLeft = DiscouragedTimeLeft;

        result = new VanillaFlyingEyeLifecycleResult(
            new NpcAiState(ai0, ai1, input.Ai.Ai2, input.Ai.Ai3),
            timeLeft,
            noTileCollide,
            discouraged);
        return true;
    }
}
