using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// One authoritative dry-world physics step for the verified ordinary TerrariaServer 1.4.5.8 player path.
/// This slice owns source-backed baseline horizontal input, the base hitbox, gravity/fall-speed clamp,
/// walk-down-slope, tile collision, position advance and post-move slope collision. Mounts, liquids,
/// StepUp/StepDown and jump-control state remain outside this slice.
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
        if (!IsValidHorizontalIntent(horizontalIntent))
        {
            next = default;
            return false;
        }

        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent);
        return TryStepCore(in player, velocityX, out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        out ServerPlayerDryPhysicsStepResult next)
    {
        if (!player.Player.IsAssigned ||
            player.IsDead ||
            player.MountType != 0 ||
            !float.IsFinite(player.PositionX) ||
            !float.IsFinite(player.PositionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(player.VelocityY))
        {
            next = default;
            return false;
        }

        float velocityY = Math.Min(player.VelocityY + Gravity, MaximumFallSpeed);

        velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            player.PositionX,
            player.PositionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            Gravity);

        float preCollisionVelocityX = velocityX;
        float preCollisionVelocityY = velocityY;
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            player.PositionX,
            player.PositionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            fallThrough: false,
            fall2: false);

        float positionX = player.PositionX + collision.VelocityX;
        float positionY = player.PositionY + collision.VelocityY;
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
