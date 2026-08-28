using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Post-AI authoritative movement slice for the verified ordinary Demon Eye path. Terraria runs AI first,
/// then resolves gravity/collision and commits position; collision/liquid state becomes input for the next AI tick.
/// This wrapper keeps targeting, AI and world motion inside one RuntimeNpcStore revision.
/// </summary>
internal sealed class VanillaNpcWorldMotionAiStepper : INpcAiStateStepper
{
    private const float WaterMovementSpeed = 0.5f;
    private const float LavaMovementSpeed = 0.5f;
    private const float HoneyMovementSpeed = 0.25f;
    private const float ShimmerMovementSpeed = 0.375f;

    private readonly INpcAiStateStepper inner;
    private readonly WorldTileStore tiles;

    public VanillaNpcWorldMotionAiStepper(INpcAiStateStepper inner, WorldTileStore tiles)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!inner.TryStepState(in npc, out NpcStateUpdate aiState))
        {
            next = default;
            return false;
        }

        if (npc.Type != 2 ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition) ||
            definition.AiStyle != 2)
        {
            next = aiState;
            return true;
        }

        NpcSimulationState simulation = aiState.Simulation;
        float velocityX = aiState.VelocityX;
        float velocityY = aiState.VelocityY;

        if (simulation.NoTileCollide)
        {
            next = aiState with
            {
                PositionX = aiState.PositionX + velocityX,
                PositionY = aiState.PositionY + velocityY,
                Simulation = simulation with
                {
                    OldVelocityX = velocityX,
                    OldVelocityY = velocityY,
                    CollideX = false,
                    CollideY = false,
                    Wet = false,
                    LiquidContact = NpcLiquidContactKind.None
                }
            };
            return true;
        }

        // This first world-motion slice is intentionally limited to the no-gravity flying path.
        // Ground/falling families use VanillaNpcGravity before they are enabled here.
        if (!simulation.NoGravity)
        {
            next = aiState;
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

        // Vanilla halves horizontal momentum exactly when an NPC leaves liquid.
        if (simulation.Wet && !wet)
            velocityX *= 0.5f;

        float oldVelocityX = velocityX;
        float oldVelocityY = velocityY;
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            aiState.PositionX,
            aiState.PositionY,
            velocityX,
            velocityY,
            definition.Width,
            definition.Height,
            fallThrough: true,
            fall2: true);

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

        next = aiState with
        {
            PositionX = aiState.PositionX + movementX,
            PositionY = aiState.PositionY + movementY,
            VelocityX = collidedVelocityX,
            VelocityY = collidedVelocityY,
            Simulation = simulation with
            {
                OldVelocityX = oldVelocityX,
                OldVelocityY = oldVelocityY,
                CollideX = collideX,
                CollideY = collideY,
                Wet = wet,
                LiquidContact = liquidContact
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
