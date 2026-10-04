namespace TerraRuntime.Core.Npcs;

/// <summary>
/// World-facing facts consumed by the source-backed TerrariaServer 1.4.5.8 AI_002 lifecycle layer.
/// Core AI owns state transitions only; tile LOS, solid overlap and Graveyard scene metrics remain runtime/world facts.
/// </summary>
public interface IVanillaFlyingEyeEnvironment
{
    bool IsGraveyardAt(float centerX, float centerY);

    bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight);

    bool SolidCollision(float positionX, float positionY, int width, int height);
}


/// <summary>Retains the exact live tile regions read by a complete AI_002 plan.</summary>
internal interface IVanillaFlyingEyeWorldFence1458
{
    bool IsCurrent { get; }
}

internal interface IVanillaFlyingEyeRetainedEnvironment1458 : IVanillaFlyingEyeEnvironment
{
    bool TryCapture(in TerraRuntime.Contracts.Runtime.NpcSnapshot source,
        in TerraRuntime.Gameplay.Npcs.VanillaNpcTargetCandidate current,
        in TerraRuntime.Gameplay.Npcs.VanillaNpcTargetCandidate closest,
        out IVanillaFlyingEyeWorldFence1458 fence);
}
