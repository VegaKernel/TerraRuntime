using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// One authoritative ordinary-world physics step for the verified TerrariaServer 1.4.5.8 player path.
/// This slice owns source-backed baseline horizontal/jump input, the base hitbox, gravity/fall-speed profiles,
/// walk-down-slope, ordinary StepDown/StepUp, tile collision, liquid-aware position advance, contact transitions,
/// post-move slope collision, the admitted Fishron-wing vertical slice and the verified Terraspark/Magiluminescence
/// horizontal accessory slice. Swimming accessories, mounts, grapples, dashes and extra jumps remain outside it.
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
        VanillaLiquidContactState previousContacts = default;
        return TryStep(
            in player,
            horizontalIntent,
            jumpIntent,
            flightEnabled: false,
            in jumpState,
            in previousContacts,
            out next,
            out nextJumpState);
    }

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        ServerPlayerJumpIntent jumpIntent,
        in VanillaServerPlayerJumpState jumpState,
        in VanillaLiquidContactState previousContacts,
        out ServerPlayerDryPhysicsStepResult next,
        out VanillaServerPlayerJumpState nextJumpState) =>
        TryStep(
            in player,
            horizontalIntent,
            jumpIntent,
            flightEnabled: false,
            in jumpState,
            in previousContacts,
            out next,
            out nextJumpState);

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        ServerPlayerJumpIntent jumpIntent,
        bool flightEnabled,
        in VanillaServerPlayerJumpState jumpState,
        in VanillaLiquidContactState previousContacts,
        out ServerPlayerDryPhysicsStepResult next,
        out VanillaServerPlayerJumpState nextJumpState)
    {
        VanillaServerPlayerHorizontalProfile1458 horizontalProfile = VanillaServerPlayerHorizontalProfile1458.Baseline;
        return TryStep(
            in player,
            horizontalIntent,
            jumpIntent,
            flightEnabled,
            in horizontalProfile,
            in jumpState,
            in previousContacts,
            out next,
            out nextJumpState);
    }

    public bool TryStep(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent,
        ServerPlayerJumpIntent jumpIntent,
        bool flightEnabled,
        in VanillaServerPlayerHorizontalProfile1458 horizontalProfile,
        in VanillaServerPlayerJumpState jumpState,
        in VanillaLiquidContactState previousContacts,
        out ServerPlayerDryPhysicsStepResult next,
        out VanillaServerPlayerJumpState nextJumpState)
    {
        if (!IsValidHorizontalIntent(horizontalIntent))
        {
            next = default;
            nextJumpState = default;
            return false;
        }

        VanillaServerPlayerPhysicsParameters profile =
            VanillaServerPlayerPhysicsProfile.Resolve(in previousContacts);
        if (horizontalProfile.SoaringInsignia)
            profile = profile with { JumpSpeed = profile.JumpSpeed + 1.8f };
        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent,
            in horizontalProfile);
        if (!VanillaServerPlayerJumpControl.TryApply(
                player.VelocityY,
                jumpIntent,
                in jumpState,
                profile.JumpSpeed,
                profile.JumpHeight,
                out float velocityY,
                out nextJumpState))
        {
            next = default;
            return false;
        }

        bool poweredFlight = false;
        if (flightEnabled &&
            jumpIntent == ServerPlayerJumpIntent.Held &&
            nextJumpState.WingTime > 0 &&
            nextJumpState.RemainingTicks == 0 &&
            velocityY != 0f)
        {
            velocityY = ApplyFishronWingFlight(velocityY, profile.JumpSpeed);
            int remainingFlight = nextJumpState.WingTime - 1;
            nextJumpState = nextJumpState with
            {
                WingTime = horizontalProfile.SoaringInsignia && remainingFlight != 0 ? 180 : remainingFlight
            };
            // Player.Update's !flag19 branch owns gravity. Active WingMovement never executes it.
            poweredFlight = true;
        }
        else if (flightEnabled && jumpIntent == ServerPlayerJumpIntent.Held && velocityY > 0f)
        {
            // Exhausted wings can glide while jump is held; release restores ordinary falling.
            profile = profile with { Gravity = profile.Gravity / 3f, MaximumFallSpeed = profile.MaximumFallSpeed / 3f };
        }

        return TryStepCore(
            in player,
            velocityX,
            velocityY,
            in previousContacts,
            in profile,
            ref nextJumpState,
            out next,
            poweredFlight);
    }

    public bool ShouldAutoJumpObstacle(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent)
    {
        if (horizontalIntent == ServerPlayerHorizontalIntent.Stop ||
            player.IsDead || player.MountType != 0 ||
            !float.IsFinite(player.PositionX) || !float.IsFinite(player.PositionY))
        {
            return false;
        }

        VanillaTileCollisionResult ground = VanillaWorldCollision.TileCollision(
            tiles,
            player.PositionX,
            player.PositionY,
            0f,
            Gravity,
            PlayerWidth,
            PlayerHeight,
            fallThrough: false,
            fall2: false);
        if (ground.VelocityY != 0f)
            return false;

        return IsForwardBlocked(in player, horizontalIntent);
    }

    public bool ShouldAscendPastObstacle(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent)
    {
        if (horizontalIntent == ServerPlayerHorizontalIntent.Stop ||
            player.IsDead || player.MountType != 0 ||
            !float.IsFinite(player.PositionX) || !float.IsFinite(player.PositionY))
        {
            return false;
        }

        return IsForwardBlocked(in player, horizontalIntent);
    }

    private bool IsForwardBlocked(
        in PlayerStateSnapshot player,
        ServerPlayerHorizontalIntent horizontalIntent)
    {
        float velocityX = VanillaServerPlayerHorizontalControl.Apply(
            player.VelocityX,
            player.VelocityY,
            horizontalIntent);
        if (velocityX == 0f)
            return false;

        VanillaTileCollisionResult forward = VanillaWorldCollision.TileCollision(
            tiles,
            player.PositionX,
            player.PositionY,
            velocityX,
            0f,
            PlayerWidth,
            PlayerHeight,
            fallThrough: false,
            fall2: false);
        return forward.VelocityX != velocityX;
    }

    private static float ApplyFishronWingFlight(float velocityY, float jumpSpeed)
    {
        // TerrariaServer 1.4.5.8 Player.WingMovement branch for Fishron Wings (item 2609, wingSlot 26).
        velocityY -= 0.125f;
        if (velocityY > 0f)
            velocityY -= 0.75f;
        else if (velocityY > -jumpSpeed)
            velocityY -= 0.15f;
        return Math.Max(velocityY, -jumpSpeed * 2.5f);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        out ServerPlayerDryPhysicsStepResult next)
    {
        VanillaServerPlayerJumpState jumpState = VanillaServerPlayerJumpState.Initial;
        VanillaLiquidContactState previousContacts = default;
        VanillaServerPlayerPhysicsParameters profile =
            VanillaServerPlayerPhysicsProfile.Resolve(in previousContacts);
        return TryStepCore(
            in player,
            velocityX,
            player.VelocityY,
            in previousContacts,
            in profile,
            ref jumpState,
            out next);
    }

    private bool TryStepCore(
        in PlayerStateSnapshot player,
        float velocityX,
        float controlledVelocityY,
        in VanillaLiquidContactState previousContacts,
        in VanillaServerPlayerPhysicsParameters profile,
        ref VanillaServerPlayerJumpState jumpState,
        out ServerPlayerDryPhysicsStepResult next,
        bool poweredFlight = false)
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

        VanillaLiquidContactState liquidContacts = VanillaWorldCollision.GetLiquidContacts(
            tiles,
            player.PositionX,
            player.PositionY,
            PlayerWidth,
            PlayerHeight);
        float liquidMovementScale = VanillaServerPlayerLiquidMovement.ResolveMovementScale(in liquidContacts);

        float positionX = player.PositionX;
        float positionY = player.PositionY;
        float velocityY = Math.Min(controlledVelocityY + (poweredFlight ? 0f : profile.Gravity), profile.MaximumFallSpeed);

        velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX,
            positionY,
            velocityX,
            velocityY,
            PlayerWidth,
            PlayerHeight,
            profile.Gravity);

        // TerrariaServer 1.4.5.8 Player.Update performs these after SlopeDownMovement and before
        // the ordinary tile-collision/position update for an unmounted, normal-gravity player.
        if (velocityY == profile.Gravity)
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

        if (velocityY >= profile.Gravity)
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
            jumpState = jumpState with { RemainingTicks = 0 };

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

        if (previousContacts.Wet &&
            !liquidContacts.Wet &&
            jumpState.RemainingTicks > profile.JumpHeight / 5)
        {
            jumpState = jumpState with { RemainingTicks = profile.JumpHeight / 5 };
        }

        next = new ServerPlayerDryPhysicsStepResult(
            slope.PositionX,
            slope.PositionY,
            slope.VelocityX,
            slope.VelocityY,
            CollideX: preCollisionVelocityX != collision.VelocityX,
            CollideY: preCollisionVelocityY != collision.VelocityY,
            collision.HitFloor,
            collision.HitCeiling,
            liquidContacts);
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
    bool HitCeiling,
    VanillaLiquidContactState LiquidContacts);
