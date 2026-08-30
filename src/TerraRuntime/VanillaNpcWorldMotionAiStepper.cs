using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Post-AI authoritative movement for the verified ordinary NPC physics families. Vanilla computes gravity
/// parameters before AI, applies AI, then gravity, epsilon velocity clamp, the pre-collision walk-down-slope
/// pass, wet/tile collision, position movement and the post-move slope pass. Collision/liquid state becomes
/// input for the next AI tick. Concrete content IDs are resolved to explicit physics-family metadata before
/// this stage chooses special movement behavior. Every collision query resolves the hitbox from the live
/// post-AI scale so dynamic-size NPCs do not keep their spawn geometry after an AI scale transition.
/// </summary>
internal sealed class VanillaNpcWorldMotionAiStepper : INpcAiStateStepper, INpcAiStateStepperWrapper
{
    private const float WaterMovementSpeed = 0.5f;
    private const float LavaMovementSpeed = 0.5f;
    private const float HoneyMovementSpeed = 0.25f;
    private const float ShimmerMovementSpeed = 0.375f;
    private const float HorizontalVelocityEpsilon = 0.005f;

    private readonly INpcAiStateStepper inner;
    private readonly WorldTileStore tiles;
    private readonly double worldSurfaceTiles;

    public VanillaNpcWorldMotionAiStepper(INpcAiStateStepper inner, WorldTileStore tiles)
        : this(
            inner,
            tiles,
            tiles?.WorldSurfaceTiles ?? Math.Max(1d, tiles?.Dimensions.HeightTiles / 3d ?? 1d))
    {
    }

    public VanillaNpcWorldMotionAiStepper(
        INpcAiStateStepper inner,
        WorldTileStore tiles,
        double worldSurfaceTiles)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        if (!double.IsFinite(worldSurfaceTiles) || worldSurfaceTiles <= 0d)
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceTiles));

        this.worldSurfaceTiles = worldSurfaceTiles;

        VanillaNpcTargetingAiStepper? targeting =
            NpcAiStateStepperComposition.FindCapability<VanillaNpcTargetingAiStepper>(inner);
        if (targeting is not null)
        {
            targeting.EnableBlueSlimeMotion(worldSurfaceTiles);
            targeting.EnableZombieMotion(worldSurfaceTiles);
            targeting.SetKingSlimeEnvironment(new VanillaKingSlimeWorldEnvironment(tiles));
        }
    }

    public INpcAiStateStepper InnerStepper => inner;

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        bool fighterStuckHopEligible = npc.VelocityX == 0f && !npc.Simulation.JustHit;
        if (!inner.TryStepState(in npc, out NpcStateUpdate aiState))
        {
            next = default;
            return false;
        }

        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition) ||
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.None)
        {
            next = aiState;
            return true;
        }

        NpcSimulationState simulation = aiState.Simulation;
        if (!definition.TryResolveHitbox(simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        int hitboxWidth = hitbox.Width;
        int hitboxHeight = hitbox.Height;
        float velocityX = aiState.VelocityX;
        float velocityY = aiState.VelocityY;

        if (definition.PhysicsFamily == VanillaNpcPhysicsFamily.GroundFighter)
        {
            bool hasFighterProfile = VanillaGroundFighterBehaviorCatalog.TryGet(
                definition.Type,
                out VanillaGroundFighterBehaviorParameters fighterProfile);
            if (hasFighterProfile && !fighterProfile.IsValid)
            {
                next = default;
                return false;
            }

            VanillaZombieObstacleMotionParameters obstacleParameters = hasFighterProfile
                ? new VanillaZombieObstacleMotionParameters(
                    fighterProfile.LowStepJumpVelocity,
                    fighterProfile.OneTileJumpVelocity,
                    fighterProfile.TwoTileJumpVelocity,
                    fighterProfile.ThreeTileJumpVelocity,
                    fighterProfile.PursuitGapJumpVelocity,
                    fighterProfile.PursuitGapSpeedMultiplier)
                : VanillaZombieObstacleMotionParameters.Vanilla;
            float stuckHopVelocity = hasFighterProfile ? fighterProfile.StuckHopVelocity : -5f;

            VanillaZombieStepUpResult stepUp = VanillaWorldZombieStepUp.Resolve(
                tiles,
                aiState.PositionX,
                aiState.PositionY,
                velocityX,
                velocityY,
                hitboxWidth,
                hitboxHeight);
            if (stepUp.Stepped)
                aiState = aiState with { PositionY = stepUp.PositionY };

            VanillaZombieDoorContactResult doorContact = VanillaWorldZombieDoorContact.Resolve(
                tiles,
                aiState.PositionX,
                aiState.PositionY,
                velocityX,
                velocityY,
                hitboxWidth,
                hitboxHeight,
                simulation.DirectionX,
                aiState.Ai);
            velocityX = doorContact.VelocityX;
            aiState = aiState with
            {
                VelocityX = velocityX,
                Ai = doorContact.Ai
            };

            VanillaZombieObstacleMotionResult obstacle = VanillaWorldZombieObstacleMotion.Resolve(
                tiles,
                aiState.PositionX,
                aiState.PositionY,
                velocityX,
                velocityY,
                hitboxWidth,
                hitboxHeight,
                simulation.DirectionX,
                simulation.DirectionY,
                obstacleParameters);
            velocityX = obstacle.VelocityX;
            velocityY = obstacle.VelocityY;

            if (doorContact.GroundSupported &&
                velocityY == 0f &&
                fighterStuckHopEligible &&
                aiState.Ai.Ai3 == 1f)
            {
                velocityY = stuckHopVelocity;
            }
        }

        if (!VanillaNpcGravity.TryApply(
                in definition,
                aiState.PositionY,
                velocityY,
                simulation.Wet,
                simulation.LiquidContact,
                tiles.Dimensions.WidthTiles,
                worldSurfaceTiles,
                out VanillaNpcGravityResult gravity))
        {
            next = aiState;
            return true;
        }

        if (!simulation.NoGravity)
            velocityY = gravity.VelocityY;

        if (velocityX < HorizontalVelocityEpsilon && velocityX > -HorizontalVelocityEpsilon)
            velocityX = 0f;

        if (simulation.NoTileCollide)
        {
            next = aiState with
            {
                PositionX = aiState.PositionX + velocityX,
                PositionY = aiState.PositionY + velocityY,
                VelocityX = velocityX,
                VelocityY = velocityY,
                Simulation = simulation with
                {
                    OldPositionX = aiState.PositionX,
                    OldPositionY = aiState.PositionY
                }
            };
            return true;
        }

        velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            velocityX,
            velocityY,
            hitboxWidth,
            hitboxHeight,
            gravity.Parameters.Gravity);

        bool wet = VanillaWorldCollision.TryGetWetContact(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            hitboxWidth,
            hitboxHeight,
            out WorldLiquidKind liquidKind);
        NpcLiquidContactKind liquidContact = wet ? MapLiquid(liquidKind) : NpcLiquidContactKind.None;

        if (simulation.Wet && !wet)
            velocityX *= 0.5f;

        float oldVelocityX = velocityX;
        float oldVelocityY = velocityY;
        bool fallThroughPlatforms = definition.PhysicsFamily switch
        {
            VanillaNpcPhysicsFamily.FlyingEye => true,
            VanillaNpcPhysicsFamily.GroundFighter => simulation.DirectionY == 1,
            _ => false
        };
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            velocityX,
            velocityY,
            hitboxWidth,
            hitboxHeight,
            fallThrough: fallThroughPlatforms,
            fall2: fallThroughPlatforms);

        float collidedVelocityX = collision.VelocityX;
        float collidedVelocityY = collision.HitCeiling ? 0.01f : collision.VelocityY;
        bool collideX = oldVelocityX != collidedVelocityX;
        bool collideY = oldVelocityY != collidedVelocityY;

        float movementX = collidedVelocityX;
        float movementY = collidedVelocityY;
        if (wet)
        {
            float slowdown = liquidKind switch
            {
                WorldLiquidKind.Honey => HoneyMovementSpeed,
                WorldLiquidKind.Shimmer => ShimmerMovementSpeed,
                WorldLiquidKind.Lava => LavaMovementSpeed,
                _ => WaterMovementSpeed
            };

            movementX = collideX ? collidedVelocityX : collidedVelocityX * slowdown;
            movementY = collideY ? collidedVelocityY : collidedVelocityY * slowdown;
        }

        float oldPositionX = aiState.PositionX;
        float oldPositionY = aiState.PositionY;
        float nextPositionX = aiState.PositionX + movementX;
        float nextPositionY = aiState.PositionY + movementY;
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
            tiles,
            nextPositionX,
            nextPositionY,
            collidedVelocityX,
            collidedVelocityY,
            hitboxWidth,
            hitboxHeight,
            fallThroughPlatforms);
        nextPositionX = slope.PositionX;
        nextPositionY = slope.PositionY;
        float finalVelocityX = slope.VelocityX;
        float finalVelocityY = slope.VelocityY;

        bool solidCollision = VanillaWorldSolidCollision.Intersects(
            tiles,
            nextPositionX,
            nextPositionY,
            hitboxWidth,
            hitboxHeight);

        next = aiState with
        {
            PositionX = nextPositionX,
            PositionY = nextPositionY,
            VelocityX = finalVelocityX,
            VelocityY = finalVelocityY,
            Simulation = simulation with
            {
                OldVelocityX = oldVelocityX,
                OldVelocityY = oldVelocityY,
                OldPositionX = oldPositionX,
                OldPositionY = oldPositionY,
                CollideX = collideX,
                CollideY = collideY,
                Wet = wet,
                LiquidContact = liquidContact,
                SolidCollision = solidCollision
            }
        };
        return true;
    }

    private static NpcLiquidContactKind MapLiquid(WorldLiquidKind kind) => kind switch
    {
        WorldLiquidKind.Lava => NpcLiquidContactKind.Lava,
        WorldLiquidKind.Honey => NpcLiquidContactKind.Honey,
        WorldLiquidKind.Shimmer => NpcLiquidContactKind.Shimmer,
        _ => NpcLiquidContactKind.Water
    };
}
