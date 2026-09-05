using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Source-backed ordinary-player horizontal movement slice from TerrariaServer 1.4.5.8 Player.HorizontalMovement.
/// It deliberately models the unmounted, no-track-boost baseline: left/right acceleration below max run speed and
/// the ordinary grounded/airborne slowdown fallback. Dash, mounts, wind, sandstorm, wrong-ground and portal branches
/// remain outside this slice.
/// </summary>
internal static class VanillaServerPlayerHorizontalControl
{
    internal const float MaximumRunSpeed = 3f;
    internal const float RunAcceleration = 0.08f;
    internal const float RunSlowdown = 0.2f;
    internal const float AirborneRunSlowdown = RunSlowdown * 0.5f;

    public static float Apply(
        float velocityX,
        float velocityY,
        ServerPlayerHorizontalIntent intent)
    {
        if (!float.IsFinite(velocityX) || !float.IsFinite(velocityY))
            return velocityX;

        if (intent == ServerPlayerHorizontalIntent.Left && velocityX > -MaximumRunSpeed)
        {
            if (velocityX > RunSlowdown)
                velocityX -= RunSlowdown;
            return velocityX - RunAcceleration;
        }

        if (intent == ServerPlayerHorizontalIntent.Right && velocityX < MaximumRunSpeed)
        {
            if (velocityX < -RunSlowdown)
                velocityX += RunSlowdown;
            return velocityX + RunAcceleration;
        }

        if (intent is not ServerPlayerHorizontalIntent.Left and
            not ServerPlayerHorizontalIntent.Stop and
            not ServerPlayerHorizontalIntent.Right)
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown server-player horizontal intent.");
        }

        float slowdown = velocityY == 0f ? RunSlowdown : AirborneRunSlowdown;
        if (velocityX > slowdown)
            return velocityX - slowdown;
        if (velocityX < -slowdown)
            return velocityX + slowdown;
        return 0f;
    }
}
