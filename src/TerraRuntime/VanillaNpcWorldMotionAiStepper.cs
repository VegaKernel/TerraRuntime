using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Post-AI authoritative movement for the verified ordinary NPC physics families. This layer also owns the
/// source-backed King Slime terminal transition and its committed world effects: Slime Rain termination, first-kill
/// blue town-slime unlock/Nerdy spawn, and downedSlimeKing progression.
/// </summary>
internal sealed class VanillaNpcWorldMotionAiStepper :
    INpcAiStateStepper,
    INpcAiStateStepperWrapper,
    INpcAiStatePostCommitEffect
{
    private const float WaterMovementSpeed = 0.5f;
    private const float LavaMovementSpeed = 0.5f;
    private const float HoneyMovementSpeed = 0.25f;
    private const float ShimmerMovementSpeed = 0.375f;
    private const float HorizontalVelocityEpsilon = 0.005f;

    private readonly INpcAiStateStepper inner;
    private readonly WorldTileStore tiles;
    private readonly double worldSurfaceTiles;
    private readonly VanillaNpcTargetingAiStepper? targeting;
    private readonly IVanillaNpcWorldEventState? worldEvents;
    private readonly IVanillaGroundFighterDoorRandom? doorRandom;
    private readonly IVanillaGroundFighterDoorOpeningSink? doorOpeningSink;
    private readonly RuntimeWorldProgressionMutations progressionMutations;
    private readonly IKingSlimeDeathRandom kingSlimeDeathRandom;

    public VanillaNpcWorldMotionAiStepper(INpcAiStateStepper inner, WorldTileStore tiles)
        : this(
            inner,
            tiles,
            tiles?.WorldSurfaceTiles ?? Math.Max(1d, tiles?.Dimensions.HeightTiles / 3d ?? 1d),
            worldEvents: null,
            doorRandom: null,
            doorOpeningSink: null,
            kingSlimeDeathRandom: null)
    {
    }

    public VanillaNpcWorldMotionAiStepper(
        INpcAiStateStepper inner,
        WorldTileStore tiles,
        double worldSurfaceTiles)
        : this(
            inner,
            tiles,
            worldSurfaceTiles,
            worldEvents: null,
            doorRandom: null,
            doorOpeningSink: null,
            kingSlimeDeathRandom: null)
    {
    }

    internal VanillaNpcWorldMotionAiStepper(
        INpcAiStateStepper inner,
        WorldTileStore tiles,
        double worldSurfaceTiles,
        IVanillaNpcWorldEventState? worldEvents,
        IVanillaGroundFighterDoorRandom? doorRandom = null,
        IVanillaGroundFighterDoorOpeningSink? doorOpeningSink = null,
        IKingSlimeDeathRandom? kingSlimeDeathRandom = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        if (!double.IsFinite(worldSurfaceTiles) || worldSurfaceTiles <= 0d)
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceTiles));

        this.worldSurfaceTiles = worldSurfaceTiles;
        this.worldEvents = worldEvents;
        this.doorRandom = doorRandom;
        this.doorOpeningSink = doorOpeningSink;
        this.kingSlimeDeathRandom = kingSlimeDeathRandom ?? new SystemKingSlimeDeathRandom();
        progressionMutations = RuntimeWorldProgressionRegistry.GetOrCreate(tiles);
        progressionMutations.SetSlimeBlueSpawnBaseline(worldEvents?.SlimeBlueSpawnUnlocked == true);

        targeting = NpcAiStateStepperComposition.FindCapability<VanillaNpcTargetingAiStepper>(inner);
        if (targeting is not null)
        {
            targeting.EnableBlueSlimeMotion(worldSurfaceTiles);
            targeting.EnableZombieMotion(worldSurfaceTiles);
            targeting.SetKingSlimeEnvironment(new VanillaKingSlimeWorldEnvironment(tiles));
            targeting.SetWormEnvironment(new VanillaWormWorldEnvironment(tiles));
        }
    }

    public INpcAiStateStepper InnerStepper => inner;

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (TryCreateKingSlimeTerminalTransition(in npc, out next))
            return true;

        bool fighterStuckHopEligible = npc.VelocityX == 0f && !npc.Simulation.JustHit;
        if (!inner.TryStepState(in npc, out NpcStateUpdate aiState))
        {
            next = default;
            return false;
        }

        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition) ||
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

            VanillaGroundFighterDoorEnvironment doorEnvironment = ResolveDoorEnvironment(in aiState);
            VanillaZombieDoorContactResult doorContact = VanillaWorldZombieDoorContact.Resolve(
                tiles,
                aiState.PositionX,
                aiState.PositionY,
                velocityX,
                velocityY,
                hitboxWidth,
                hitboxHeight,
                simulation.DirectionX,
                aiState.Ai,
                doorEnvironment,
                doorRandom);
            if (doorContact.OpeningIntent is { } openingIntent &&
                doorOpeningSink?.TryOpen(in openingIntent) == true)
            {
                doorContact = doorContact with
                {
                    Ai = new NpcAiState(
                        doorContact.Ai.Ai0,
                        0f,
                        doorContact.Ai.Ai2,
                        doorContact.Ai.Ai3)
                };
            }

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

    public void ApplyCommittedEffect(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        if (!IsCommittedKingSlimeDeathShape(in before, in committed))
            return;

        // TerrariaServer 1.4.5.8 case 50 source order:
        // StopSlimeRain -> set unlock -> NewNPC -> velocity RNG/update -> downedSlimeKing.
        worldEvents?.TryStopSlimeRain(kingSlimeDeathRandom);

        if (worldEvents is not null && progressionMutations.MarkSlimeBlueSpawnUnlocked())
        {
            worldEvents.MarkSlimeBlueSpawnUnlocked();
            if (TryCreateNerdySlimeSpawnIntent(in before, out NpcAiSpawnIntent intent))
            {
                bool spawned = mutations.TrySpawn(in intent, out NpcSnapshot nerdy);
                float velocityX = kingSlimeDeathRandom.NextFloatDirection() * 3f;
                if (spawned &&
                    !mutations.TryUpdateVelocity(nerdy.Handle, velocityX, -10f, out _))
                {
                    throw new InvalidOperationException(
                        "The committed Nerdy Slime spawn could not receive its source-ordered launch velocity.");
                }
            }
        }

        progressionMutations.MarkCompleted(VanillaWorldProgressionId.KingSlime);
    }

    private static bool TryCreateKingSlimeTerminalTransition(
        in NpcSnapshot npc,
        out NpcStateUpdate next)
    {
        if (npc.Type != VanillaNpcIds.KingSlime.Value ||
            npc.Simulation.LifeMax <= 0 ||
            npc.Simulation.Life != 0)
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            npc.Target,
            npc.Ai,
            npc.Simulation with { TimeLeft = 0 });
        return true;
    }

    private static bool TryCreateNerdySlimeSpawnIntent(
        in NpcSnapshot source,
        out NpcAiSpawnIntent intent)
    {
        intent = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        intent = new NpcAiSpawnIntent(
            VanillaNpcIds.TownSlimeBlue,
            BottomX: (int)centerX - 10,
            BottomY: (int)centerY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: checked((ushort)VanillaNpcDefinitionCatalog.DefaultTarget));
        return true;
    }

    private static bool IsCommittedKingSlimeDeathShape(in NpcSnapshot before, in NpcSnapshot committed) =>
        before.Handle == committed.Handle &&
        before.Type == VanillaNpcIds.KingSlime.Value &&
        before.Simulation.LifeMax > 0 &&
        before.Simulation.Life == 0 &&
        committed.Simulation.Life == 0 &&
        committed.Simulation.TimeLeft == 0;

    private VanillaGroundFighterDoorEnvironment ResolveDoorEnvironment(in NpcStateUpdate aiState)
    {
        bool bloodMoonActive = worldEvents?.BloodMoonActive == true;
        if (aiState.Target < byte.MaxValue &&
            targeting is not null &&
            targeting.TryGetCandidate(checked((byte)aiState.Target), out VanillaNpcTargetCandidate target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            return new VanillaGroundFighterDoorEnvironment(
                bloodMoonActive,
                HasTarget: true,
                target.CenterX,
                target.CenterY);
        }

        return new VanillaGroundFighterDoorEnvironment(
            bloodMoonActive,
            HasTarget: false,
            TargetCenterX: 0f,
            TargetCenterY: 0f);
    }

    private static NpcLiquidContactKind MapLiquid(WorldLiquidKind kind) => kind switch
    {
        WorldLiquidKind.Lava => NpcLiquidContactKind.Lava,
        WorldLiquidKind.Honey => NpcLiquidContactKind.Honey,
        WorldLiquidKind.Shimmer => NpcLiquidContactKind.Shimmer,
        _ => NpcLiquidContactKind.Water
    };
}
