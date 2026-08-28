using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Post-AI authoritative movement for the verified ordinary Blue Slime, Demon Eye and Zombie paths. Vanilla
/// computes gravity parameters before AI, applies AI, then gravity, epsilon velocity clamp, the pre-collision
/// walk-down-slope pass, wet/tile collision, position movement and the post-move slope pass. Collision/liquid
/// state becomes input for the next AI tick.
/// </summary>
internal sealed class VanillaNpcWorldMotionAiStepper : INpcAiStateStepper
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

        if (inner is VanillaNpcTargetingAiStepper targeting)
        {
            targeting.EnableBlueSlimeMotion(worldSurfaceTiles);
            targeting.EnableZombieMotion(worldSurfaceTiles);
        }
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        bool zombieStuckHopEligible = npc.VelocityX == 0f && !npc.Simulation.JustHit;
        if (!inner.TryStepState(in npc, out NpcStateUpdate aiState))
        {
            next = default;
            return false;
        }

        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition) ||
            !IsSupportedMotionPath(npcType, definition.AiStyle))
        {
            next = aiState;
            return true;
        }

        NpcSimulationState simulation = aiState.Simulation;
        float velocityX = aiState.VelocityX;
        float velocityY = aiState.VelocityY;

        // The late ordinary AI_003 world-probe tail executes before UpdateNPC applies gravity. Closed-door
        // contact owns ai[1]/ai[2]/ai[3] and can recoil X; obstacle jumps follow, then the tiny stuck-hop whose
        // eligibility vanilla captured from the pre-AI X velocity and justHit state.
        if (npcType == VanillaNpcIds.Zombie)
        {
            VanillaZombieDoorContactResult doorContact = VanillaWorldZombieDoorContact.Resolve(
                tiles,
                aiState.PositionX,
                aiState.PositionY,
                velocityX,
                velocityY,
                definition.Width,
                definition.Height,
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
                definition.Width,
                definition.Height,
                simulation.DirectionX,
                simulation.DirectionY);
            velocityX = obstacle.VelocityX;
            velocityY = obstacle.VelocityY;

            if (doorContact.GroundSupported &&
                velocityY == 0f &&
                zombieStuckHopEligible &&
                aiState.Ai.Ai3 == 1f)
            {
                velocityY = -5f;
            }
        }

        // Vanilla computes NPC.gravity and maxFallSpeed before AI regardless of noGravity. The noGravity
        // flag controls only whether the already-computed gravity is added after AI. WalkDownSlope later
        // receives that same gravity field, so the parameter must remain available even when noGravity is set.
        if (!VanillaNpcGravity.TryApply(
                npcType,
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
            definition.Width,
            definition.Height,
            gravity.Parameters.Gravity);

        bool wet = VanillaWorldCollision.TryGetWetContact(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            definition.Width,
            definition.Height,
            out WorldLiquidKind liquidKind);
        NpcLiquidContactKind liquidContact = wet ? MapLiquid(liquidKind) : NpcLiquidContactKind.None;

        if (simulation.Wet && !wet)
            velocityX *= 0.5f;

        float oldVelocityX = velocityX;
        float oldVelocityY = velocityY;
        bool fallThroughPlatforms =
            npcType == VanillaNpcIds.DemonEye ||
            (npcType == VanillaNpcIds.Zombie && simulation.DirectionY == 1);
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            velocityX,
            velocityY,
            definition.Width,
            definition.Height,
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
            definition.Width,
            definition.Height,
            fallThroughPlatforms);
        nextPositionX = slope.PositionX;
        nextPositionY = slope.PositionY;
        float finalVelocityX = slope.VelocityX;
        float finalVelocityY = slope.VelocityY;

        bool solidCollision = VanillaWorldSolidCollision.Intersects(
            tiles,
            nextPositionX,
            nextPositionY,
            definition.Width,
            definition.Height);

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

    private static bool IsSupportedMotionPath(NpcTypeId type, NpcAiStyleId aiStyle) =>
        (type == VanillaNpcIds.BlueSlime && aiStyle == VanillaNpcAiStyles.Slime) ||
        (type == VanillaNpcIds.DemonEye && aiStyle == VanillaNpcAiStyles.DemonEye) ||
        (type == VanillaNpcIds.Zombie && aiStyle == VanillaNpcAiStyles.Fighter);

    private static NpcLiquidContactKind MapLiquid(WorldLiquidKind kind) => kind switch
    {
        WorldLiquidKind.Lava => NpcLiquidContactKind.Lava,
        WorldLiquidKind.Honey => NpcLiquidContactKind.Honey,
        WorldLiquidKind.Shimmer => NpcLiquidContactKind.Shimmer,
        _ => NpcLiquidContactKind.Water
    };
}
