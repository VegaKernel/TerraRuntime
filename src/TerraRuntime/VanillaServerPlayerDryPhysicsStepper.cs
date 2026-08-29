using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// One authoritative dry-world physics step for the verified ordinary TerrariaServer 1.4.5.8 player path.
/// This slice deliberately excludes mounts, liquids, StepUp/StepDown and jump-control state. It owns only the
/// source-backed base hitbox, gravity/fall-speed clamp, walk-down-slope, tile collision, position advance and
/// post-move slope collision required before richer G6-D player control is layered on top.
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
        out ServerPlayerDryPhysicsStepResult next)
    {
        if (!player.Player.IsAssigned ||
            player.IsDead ||
            player.MountType != 0 ||
            !float.IsFinite(player.PositionX) ||
            !float.IsFinite(player.PositionY) ||
            !float.IsFinite(player.VelocityX) ||
            !float.IsFinite(player.VelocityY))
        {
            next = default;
            return false;
        }

        float velocityX = player.VelocityX;
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
