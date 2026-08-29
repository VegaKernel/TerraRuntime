using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Converts MoveTo/FollowPlayer into button-like horizontal and jump intent. It never advances position or chooses
/// velocity; those remain owned by the source-backed player physics stepper.
/// </summary>
internal static class RuntimeServerPlayerMovementIntentController
{
    private const float PlayerCenterX = VanillaServerPlayerDryPhysicsStepper.PlayerWidth * 0.5f;
    private const float PlayerCenterY = VanillaServerPlayerDryPhysicsStepper.PlayerHeight * 0.5f;

    public static bool TryResolve(
        in PlayerStateSnapshot player,
        in ServerPlayerMovementIntent intent,
        IRuntimePlayerSnapshotLookup players,
        out ServerPlayerHorizontalIntent horizontal,
        out ServerPlayerJumpIntent jump)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (!player.Player.IsAssigned || !intent.IsValid)
        {
            horizontal = ServerPlayerHorizontalIntent.Stop;
            jump = ServerPlayerJumpIntent.Released;
            return false;
        }

        switch (intent.Kind)
        {
            case ServerPlayerMovementIntentKind.Stop:
                horizontal = ServerPlayerHorizontalIntent.Stop;
                jump = ServerPlayerJumpIntent.Released;
                return true;

            case ServerPlayerMovementIntentKind.MoveTo:
                return ResolveTarget(
                    in player,
                    intent.TargetX,
                    intent.TargetY,
                    intent.Options,
                    out horizontal,
                    out jump);

            case ServerPlayerMovementIntentKind.FollowPlayer:
                if (!players.TryGetPlayer(intent.TargetPlayer, out PlayerStateSnapshot target) ||
                    (target.HasHealth && target.IsDead))
                {
                    horizontal = ServerPlayerHorizontalIntent.Stop;
                    jump = ServerPlayerJumpIntent.Released;
                    return true;
                }

                return ResolveTarget(
                    in player,
                    target.PositionX + PlayerCenterX,
                    target.PositionY + PlayerCenterY,
                    intent.Options,
                    out horizontal,
                    out jump);

            default:
                horizontal = ServerPlayerHorizontalIntent.Stop;
                jump = ServerPlayerJumpIntent.Released;
                return false;
        }
    }

    private static bool ResolveTarget(
        in PlayerStateSnapshot player,
        float targetX,
        float targetY,
        ServerPlayerMovementOptions options,
        out ServerPlayerHorizontalIntent horizontal,
        out ServerPlayerJumpIntent jump)
    {
        float deltaX = targetX - (player.PositionX + PlayerCenterX);
        float deltaY = targetY - (player.PositionY + PlayerCenterY);
        if (options.MaximumDistance > 0f)
        {
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;
            float maximumSquared = options.MaximumDistance * options.MaximumDistance;
            if (!float.IsFinite(distanceSquared) || distanceSquared > maximumSquared)
            {
                horizontal = ServerPlayerHorizontalIntent.Stop;
                jump = ServerPlayerJumpIntent.Released;
                return true;
            }
        }

        horizontal = Math.Abs(deltaX) <= options.StopDistance
            ? ServerPlayerHorizontalIntent.Stop
            : deltaX > 0f
                ? ServerPlayerHorizontalIntent.Right
                : ServerPlayerHorizontalIntent.Left;
        jump = deltaY < -options.JumpVerticalThreshold
            ? ServerPlayerJumpIntent.Held
            : ServerPlayerJumpIntent.Released;
        return true;
    }
}
