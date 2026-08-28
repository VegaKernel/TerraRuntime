using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Post-AI authoritative movement for the verified ordinary Blue Slime and Demon Eye paths. Vanilla
/// computes gravity parameters before AI, applies AI, then gravity, epsilon velocity clamp, wet/tile
/// collision and position movement. Collision/liquid state becomes input for the next AI tick.
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
        : this(inner, tiles, Math.Max(1d, tiles?.Dimensions.HeightTiles / 3d ?? 1d))
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

        // Grounded style-1 AI is intentionally disabled when ServerRuntimeState has no world tiles.
        // Enabling it here guarantees every proposed Blue Slime step has gravity/collision available.
        if (inner is VanillaNpcTargetingAiStepper targeting)
            targeting.EnableBlueSlimeMotion();
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!inner.TryStepState(in npc, out NpcStateUpdate aiState))
        {
            next = default;
            return false;
        }

        if (!VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition) ||
            !IsSupportedMotionPath(npc.Type, definition.AiStyle))
        {
            next = aiState;
            return true;
        }

        NpcSimulationState simulation = aiState.Simulation;
        float velocityX = aiState.VelocityX;
        float velocityY = aiState.VelocityY;

        if (!simulation.NoGravity)
        {
            if (!VanillaNpcGravity.TryApply(
                    npc.Type,
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

            velocityY = gravity.VelocityY;
        }

        if (velocityX < HorizontalVelocityEpsilon && velocityX > -HorizontalVelocityEpsilon)
            velocityX = 0f;

        if (simulation.NoTileCollide)
        {
            // Vanilla bypasses UpdateCollision entirely in this branch. Persisted collision/wet/overlap flags
            // therefore remain available to the next AI tick; verified style-2 ignores them while noTileCollide.
            next = aiState with
            {
                PositionX = aiState.PositionX + velocityX,
                PositionY = aiState.PositionY + velocityY,
                VelocityX = velocityX,
                VelocityY = velocityY
            };
            return true;
        }

        bool wet = VanillaWorldCollision.TryGetWetContact(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            definition.Width,
            definition.Height,
            out WorldLiquidKind liquidKind);
        NpcLiquidContactKind liquidContact = wet ? MapLiquid(liquidKind) : NpcLiquidContactKind.None;

        // Collision_WaterCollision halves horizontal velocity exactly when leaving liquid, before oldVelocity
        // is captured for the new collision pass.
        if (simulation.Wet && !wet)
            velocityX *= 0.5f;

        float oldVelocityX = velocityX;
        float oldVelocityY = velocityY;
        bool fallThroughPlatforms = npc.Type == 2;
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

        float nextPositionX = aiState.PositionX + movementX;
        float nextPositionY = aiState.PositionY + movementY;
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
            VelocityX = collidedVelocityX,
            VelocityY = collidedVelocityY,
            Simulation = simulation with
            {
                OldVelocityX = oldVelocityX,
                OldVelocityY = oldVelocityY,
                CollideX = collideX,
                CollideY = collideY,
                Wet = wet,
                LiquidContact = liquidContact,
                SolidCollision = solidCollision
            }
        };
        return true;
    }

    private static bool IsSupportedMotionPath(int type, int aiStyle) =>
        (type == 1 && aiStyle == 1) ||
        (type == 2 && aiStyle == 2);

    private static NpcLiquidContactKind MapLiquid(WorldLiquidKind kind) => kind switch
    {
        WorldLiquidKind.Lava => NpcLiquidContactKind.Lava,
        WorldLiquidKind.Honey => NpcLiquidContactKind.Honey,
        WorldLiquidKind.Shimmer => NpcLiquidContactKind.Shimmer,
        _ => NpcLiquidContactKind.Water
    };
}
