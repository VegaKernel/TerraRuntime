namespace TerraRuntime.Core;

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
