using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// One authoritative dry-world physics step for the verified ordinary TerrariaServer 1.4.5.8 player path.
/// This slice owns source-backed baseline horizontal/jump input, the base hitbox, gravity/fall-speed clamp,
/// walk-down-slope, ordinary StepDown/StepUp, tile collision, position advance and post-move slope collision.
/// Mounts, liquids and extended jump families remain outside this slice.
/// </summary>
internal sealed class VanillaServerPlayerDryPhysicsStepper
{
    internal const int PlayerWidth = 20;
    internal const int PlayerHeight = 42;
    internal const float Gravity = 0.4f;
    internal const float MaximumFallSpeed = 10f;

    private readonly WorldTileStore tiles;

    public VanillaServerPlayerDryPhysicsStepper(WorldTileStore tiles)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public bool TryStep(
        in PlayerStateSnapshot player,
        out ServerPlayerDryPhysicsStepResult next) =>
        TryStepCore(in player, player.VelocityX, out next);

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        out ServerPlayerDryPhysicsStepResult next)
    {
        VanillaServerPlayerJumpState jumpState = VanillaServerPlayerJumpState.Initial;
        return TryStep(
            in player,
            horizontalIntent,
            ServerPlayerJumpIntent.Released,
            in jumpState,
            out next,
            out _);
    }

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        ServerPlayerJumpIntent jumpIntent,
        in VanillaServerPlayerJumpState jumpState,
        out ServerPlayerDryPhysicsStepResult next,
        out VanillaServerPlayerJumpState nextJumpState)
    {
        if (!IsValidHorizontalIntent(horizontalIntent))
        {
            next = default;
            nextJumpState = default;
            return false;
        }

        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent);
        if (!VanillaServerPlayerJumpControl.TryApply(
                player.VelocityY,
                jumpIntent,
                in jumpState,
                out float velocityY,
                out nextJumpState))
        {
            next = default;
            return false;
        }

        return TryStepCore(in player, velocityX, velocityY, ref nextJumpState, out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        out ServerPlayerDryPhysicsStepResult next)
    {
        VanillaServerPlayerJumpState jumpState = VanillaServerPlayerJumpState.Initial;
        return TryStepCore(in player, velocityX, player.VelocityY, ref jumpState, out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        float controlledVelocityY,
        ref VanillaServerPlayerJumpState jumpState,
        out ServerPlayerDryPhysicsStepResult next)
    {
        if (!player.Player.IsAssigned ||
            player.IsDead ||
            player.MountType != 0 ||
            !float.IsFinite(player.PositionX) ||
            !float.IsFinite(player.PositionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(controlledVelocityY))
        {
            next = default;
            return false;
        }

        float positionX = player.PositionX;
        float positionY = player.PositionY;
        float velocityY = Math.Min(controlledVelocityY + Gravity, MaximumFallSpeed);

        velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX,
            positionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            Gravity);

        // TerrariaServer 1.4.5.8 Player.Update performs these after SlopeDownMovement and before
        // the ordinary tile-collision/position update for an unmounted, normal-gravity player.
        if (velocityY == Gravity)
        {
            positionY = VanillaWorldPlayerStepCollision.StepDown(
                tiles,
                positionX,
                positionY,
                velocityX,
                velocityY,
                PlayerWidth,
                PlayerHeight).PositionY;
        }

        if (velocityY >= Gravity)
        {
            positionY = VanillaWorldPlayerStepCollision.StepUp(
                tiles,
                positionX,
                positionY,
                velocityX,
                PlayerWidth,
                PlayerHeight).PositionY;
        }

        float preCollisionVelocityX = velocityX;
        float preCollisionVelocityY = velocityY;
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            positionX,
            positionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            fallThrough: false,
            fall2: false);

        if (collision.HitCeiling && jumpState.RemainingTicks > 0)
            jumpState = new VanillaServerPlayerJumpState(0, jumpState.ReleaseReady);

        positionX += collision.VelocityX;
        positionY += collision.VelocityY;
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX,
            positionY,
            collision.VelocityX,
            collision.VelocityY,
            PlayerWidth,
            PlayerHeight,
            fall: false);

        next = new ServerPlayerDryPhysicsStepResult(
            slope.PositionX,
            slope.PositionY,
            slope.VelocityX,
            slope.VelocityY,
            CollideX: preCollisionVelocityX != collision.VelocityX,
            CollideY: preCollisionVelocityY != collision.VelocityY,
            collision.HitFloor,
            collision.HitCeiling);
        return true;
    }

    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;
}

internal readonly record struct ServerPlayerDryPhysicsStepResult(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    bool CollideX,
    bool CollideY,
    bool HitFloor,
    bool HitCeiling);
