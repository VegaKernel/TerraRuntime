using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// One authoritative ordinary-world physics step for the verified TerrariaServer 1.4.5.8 player path.
/// This slice owns source-backed baseline horizontal/jump input, the base hitbox, medium-specific gravity/fall-speed,
/// walk-down-slope, ordinary StepDown/StepUp, tile collision, liquid-aware position advance and post-move slope
/// collision. Mounts, floating equipment, merman/trident movement, grapples and extended jump families remain outside
/// this slice.
/// </summary>
internal sealed class VanillaServerPlayerDryPhysicsStepper
{
    internal const int PlayerWidth = 20;
    internal const int PlayerHeight = 42;
    internal const float Gravity = VanillaServerPlayerLiquidPhysics.DryGravity;
    internal const float MaximumFallSpeed =
        VanillaServerPlayerLiquidPhysics.DryMaximumFallSpeedBase +
        VanillaServerPlayerLiquidPhysics.MaximumFallSpeedEpsilon;

    private const int PlayerSlotCapacity = 256;

    private readonly WorldTileStore tiles;
    private readonly PlayerHandle[] liquidStateOwners = new PlayerHandle[PlayerSlotCapacity];
    private readonly VanillaServerPlayerLiquidState[] liquidStates =
        new VanillaServerPlayerLiquidState[PlayerSlotCapacity];

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

        VanillaServerPlayerLiquidState previousLiquidState = GetPreviousLiquidState(player.Player);
        VanillaServerPlayerMotionProfile motionProfile =
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in previousLiquidState);
        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent);
        if (!VanillaServerPlayerJumpControl.TryApply(
                player.VelocityY,
                jumpIntent,
                in jumpState,
                motionProfile.JumpSpeed,
                motionProfile.JumpHeight,
                out float velocityY,
                out nextJumpState))
        {
            next = default;
            return false;
        }

        return TryStepCore(
            in player,
            velocityX,
            velocityY,
            in previousLiquidState,
            in motionProfile,
            ref nextJumpState,
            out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        out ServerPlayerDryPhysicsStepResult next)
    {
        VanillaServerPlayerLiquidState previousLiquidState = GetPreviousLiquidState(player.Player);
        VanillaServerPlayerMotionProfile motionProfile =
            VanillaServerPlayerLiquidPhysics.ResolveMotionProfile(in previousLiquidState);
        VanillaServerPlayerJumpState jumpState = VanillaServerPlayerJumpState.Initial;
        return TryStepCore(
            in player,
            velocityX,
            player.VelocityY,
            in previousLiquidState,
            in motionProfile,
            ref jumpState,
            out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        float controlledVelocityY,
        in VanillaServerPlayerLiquidState previousLiquidState,
        in VanillaServerPlayerMotionProfile motionProfile,
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

        // TerrariaServer refreshes wet/honey/shimmer state later than JumpMovement/gravity but before collision.
        // The current contact therefore affects this tick's displacement and the next tick's motion profile.
        VanillaLiquidContactState liquidContacts = VanillaWorldCollision.GetLiquidContacts(
            tiles,
            player.PositionX,
            player.PositionY,
            PlayerWidth,
            PlayerHeight);
        VanillaServerPlayerLiquidState currentLiquidState =
            VanillaServerPlayerLiquidState.FromContacts(in liquidContacts);
        float liquidMovementScale = VanillaServerPlayerLiquidMovement.ResolveMovementScale(in liquidContacts);

        float positionX = player.PositionX;
        float positionY = player.PositionY;
        float velocityY = Math.Min(
            controlledVelocityY + motionProfile.Gravity,
            motionProfile.MaximumFallSpeed);

        int remainingJumpTicks = VanillaServerPlayerLiquidPhysics.ClampRemainingJumpOnLiquidExit(
            jumpState.RemainingTicks,
            in previousLiquidState,
            in currentLiquidState,
            motionProfile.JumpHeight);
        if (remainingJumpTicks != jumpState.RemainingTicks)
            jumpState = new VanillaServerPlayerJumpState(remainingJumpTicks, jumpState.ReleaseReady);

        velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX,
            positionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            motionProfile.Gravity);

        // TerrariaServer 1.4.5.8 Player.Update performs these after SlopeDownMovement and before
        // the ordinary tile-collision/position update for an unmounted, normal-gravity player.
        if (velocityY == motionProfile.Gravity)
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

        if (velocityY >= motionProfile.Gravity)
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

        VanillaServerPlayerLiquidDisplacement displacement =
            VanillaServerPlayerLiquidMovement.ResolveDisplacement(
                preCollisionVelocityX,
                preCollisionVelocityY,
                collision.VelocityX,
                collision.VelocityY,
                liquidMovementScale);
        positionX += displacement.X;
        positionY += displacement.Y;
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX,
            positionY,
            collision.VelocityX,
            collision.VelocityY,
            PlayerWidth,
            PlayerHeight,
            fall: false);

        CommitLiquidState(player.Player, in currentLiquidState);
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

    private VanillaServerPlayerLiquidState GetPreviousLiquidState(PlayerHandle player)
    {
        if (!player.IsAssigned)
            return VanillaServerPlayerLiquidState.Dry;

        int slot = player.Slot.Value;
        return liquidStateOwners[slot] == player
            ? liquidStates[slot]
            : VanillaServerPlayerLiquidState.Dry;
    }

    private void CommitLiquidState(
        PlayerHandle player,
        in VanillaServerPlayerLiquidState liquidState)
    {
        int slot = player.Slot.Value;
        liquidStateOwners[slot] = player;
        liquidStates[slot] = liquidState;
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
