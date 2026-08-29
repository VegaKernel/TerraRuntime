using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Source-backed ordinary-player horizontal movement slice from TerrariaServer 1.4.5.8 Player.HorizontalMovement.
/// It deliberately models only the unmounted baseline: run-speed clamp before input, left/right acceleration and
/// grounded/airborne slowdown. Dash, mounts, sandstorm, wrong-ground and portal branches remain outside this slice.
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

        if (velocityX < -MaximumRunSpeed)
            velocityX = -MaximumRunSpeed;
        else if (velocityX > MaximumRunSpeed)
            velocityX = MaximumRunSpeed;

        switch (intent)
        {
            case ServerPlayerHorizontalIntent.Left:
                if (velocityX > -MaximumRunSpeed)
                {
                    if (velocityX > RunSlowdown)
                        velocityX -= RunSlowdown;
                    velocityX -= RunAcceleration;
                }
                break;

            case ServerPlayerHorizontalIntent.Right:
                if (velocityX < MaximumRunSpeed)
                {
                    if (velocityX < -RunSlowdown)
                        velocityX += RunSlowdown;
                    velocityX += RunAcceleration;
                }
                break;

            case ServerPlayerHorizontalIntent.Stop:
                float slowdown = velocityY == 0f ? RunSlowdown : AirborneRunSlowdown;
                if (velocityX > slowdown)
                    velocityX -= slowdown;
                else if (velocityX < -slowdown)
                    velocityX += slowdown;
                else
                    velocityX = 0f;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown server-player horizontal intent.");
        }

        return velocityX;
    }
}
