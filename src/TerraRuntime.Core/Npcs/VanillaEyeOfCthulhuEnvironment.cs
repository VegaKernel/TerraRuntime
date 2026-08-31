namespace TerraRuntime.Core;

/// <summary>
/// World-facing geometry required by TerrariaServer 1.4.5.8 Eye of Cthulhu AI_004. Core AI owns state
/// transitions while the runtime host supplies the exact tile-backed Collision.CanHit query used by the
/// Good World post-rapid-dash transformation re-entry.
/// </summary>
public interface IVanillaEyeOfCthulhuEnvironment
{
    bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight);
}
