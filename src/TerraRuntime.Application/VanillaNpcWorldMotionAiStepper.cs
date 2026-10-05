using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

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
        IKingSlimeDeathRandom? kingSlimeDeathRandom = null,
        RuntimeWorldProgressionMutations? progressionMutations = null)
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
        this.progressionMutations = progressionMutations ?? new RuntimeWorldProgressionMutations();
        this.progressionMutations.SetSlimeBlueSpawnBaseline(worldEvents?.SlimeBlueSpawnUnlocked == true);

        targeting = NpcAiStateStepperComposition.FindCapability<VanillaNpcTargetingAiStepper>(inner);
        if (targeting is not null)
        {
            targeting.EnableBlueSlimeMotion(worldSurfaceTiles);
            targeting.EnableZombieMotion(worldSurfaceTiles);
            targeting.SetKingSlimeEnvironment(new VanillaKingSlimeWorldEnvironment(tiles));
            targeting.SetWormEnvironment(new VanillaWormWorldEnvironment(tiles));
            targeting.SetFishEnvironment(new VanillaFishWorldEnvironment1458(tiles));
            targeting.SetAntlionEnvironment(new VanillaAntlionWorldEnvironment1458(tiles));
            targeting.SetGhostHoverEnvironment(new VanillaGhostHoverWorldEnvironment1458(tiles));
            targeting.SetMothronEnvironment(new VanillaMothronWorldEnvironment1458(tiles, worldSurfaceTiles));
            targeting.SetBigMimicEnvironment(new VanillaBigMimicWorldEnvironment1458(tiles));
        }
    }

    public INpcAiStateStepper InnerStepper => inner;

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (TryCreateKingSlimeTerminalTransition(in npc, out next))
            return true;

        if (!inner.TryStepState(in npc, out NpcStateUpdate aiState))
        {
            next = default;
            return false;
        }
        if ((targeting is not null && (VanillaGhostHoverNpcCatalog1458.IsSupported(npc.TypeIdentity) ||
            VanillaMothronNpcCatalog1458.IsSupported(npc.TypeIdentity) ||
            VanillaBigMimicNpcCatalog1458.IsSupported(npc.TypeIdentity) || targeting.HasFlyingEyeRetainedPlan(in npc) ||
            targeting.HasSlimeContainedPlan(in npc))) ||
            npc.TypeIdentity == VanillaNpcIds.Nailhead || npc.TypeIdentity == VanillaNpcIds.DrManFly || npc.TypeIdentity == VanillaNpcIds.Frankenstein)
        {
            next = aiState;
            return true;
        }
        return TryApplyWorldMotion(in npc, in aiState, out next);
    }

    internal bool TryApplyWorldMotion(in NpcSnapshot npc, in NpcStateUpdate proposed, out NpcStateUpdate next)
    {
        if (!TryApplyTerrainMotion(in npc, in proposed, out var adjusted))
        {
            next = default;
            return false;
        }
        // Preserve the wrapper's existing AI-only path for explicitly unowned/custom physics.
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.None)
        {
            next = adjusted;
            return true;
        }
        return TryFinishPhysics(tiles, worldSurfaceTiles, in npc, in adjusted, out next);
    }

    internal bool TryApplyTerrainMotion(in NpcSnapshot npc, in NpcStateUpdate proposed, out NpcStateUpdate next)
    {
        NpcStateUpdate aiState = proposed;
        bool fighterStuckHopEligible = npc.VelocityX == 0f && !npc.Simulation.JustHit;
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.None)
        {
            next = aiState;
            return true;
        }

        NpcSimulationState simulation = aiState.Simulation;
        if (!definition.TryResolveHitbox(simulation, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        int hitboxWidth = hitbox.Width;
        int hitboxHeight = hitbox.Height;
        float velocityX = aiState.VelocityX;
        float velocityY = aiState.VelocityY;

        // Psycho ambush/reveal and Creature swimming return before AI_003 terrain logic; outer physics still runs.
        if (definition.PhysicsFamily == VanillaNpcPhysicsFamily.GroundFighter &&
            !(definition.Type == VanillaNpcIds.Psycho && npc.Ai.Ai2 <= 0f) &&
            !(definition.Type == VanillaNpcIds.CreatureFromTheDeep && npc.Simulation.Wet) &&
            !(definition.Type == VanillaNpcIds.ThePossessed && simulation.NoGravity))
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

            // These source launches set AI_003's force-ground-scan flag despite negative vertical motion.
            if ((definition.Type == VanillaNpcIds.Fritz || definition.Type == VanillaNpcIds.ThePossessed) &&
                npc.VelocityY == 0f && velocityY < 0f &&
                !VanillaWorldZombieDoorContact.HasGroundSupport(tiles, aiState.PositionX, aiState.PositionY,
                    hitboxWidth, hitboxHeight))
                velocityY = 0f;

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
            // Source flag8 is false for Psycho: its ai[2] remains the ambush clock, never door pressure.
            VanillaZombieDoorContactResult doorContact = (definition.Type == VanillaNpcIds.Psycho || definition.Type == VanillaNpcIds.DrManFly || definition.Type == VanillaNpcIds.ThePossessed)
                ? new VanillaZombieDoorContactResult(velocityX, aiState.Ai, false, false, false)
                : VanillaWorldZombieDoorContact.Resolve(
                tiles,
                aiState.PositionX,
                aiState.PositionY,
                velocityX,
                velocityY,
                hitboxWidth,
                hitboxHeight,
                simulation.DirectionX,
                aiState.Ai,
                definition.Type,
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

            if (hasFighterProfile &&
                fighterProfile.CloseRangeLunge &&
                doorEnvironment.HasTarget &&
                VanillaGroundFighterCloseRangeLunge.TryResolve(
                    aiState.PositionX + hitboxWidth * 0.5f,
                    aiState.PositionY + hitboxHeight * 0.5f,
                    doorEnvironment.TargetCenterX,
                    doorEnvironment.TargetCenterY,
                    velocityX,
                    velocityY,
                    simulation.DirectionX,
                    out float lungedVelocityX,
                    out float lungedVelocityY))
            {
                velocityX = lungedVelocityX;
                velocityY = lungedVelocityY;
            }

            if (definition.Type.Value == 258 &&
                doorEnvironment.HasTarget &&
                aiState.Target < byte.MaxValue &&
                targeting is not null &&
                targeting.TryGetCandidate(checked((byte)aiState.Target), out VanillaNpcTargetCandidate target))
            {
                bool canHit = VanillaWorldCanHit.HasLineOfSight(
                    tiles,
                    aiState.PositionX,
                    aiState.PositionY,
                    hitboxWidth,
                    hitboxHeight,
                    target.CenterX - target.Width * .5f,
                    target.CenterY - target.Height * .5f,
                    (int)target.Width,
                    (int)target.Height);
                if (VanillaGroundFighter258Motion.TryResolveGroundLeap(
                        aiState.PositionY, velocityY, target.CenterY, canHit, out float leapingVelocityY))
                {
                    velocityY = leapingVelocityY;
                }
            }
            if (definition.Type == VanillaNpcIds.Butcher && doorContact.GroundSupported && velocityY < 0f)
            { velocityX *= 1.3f; velocityY *= 1.1f; }
        }
        else if (definition.PhysicsFamily == VanillaNpcPhysicsFamily.UnicornGround)
        {
            VanillaZombieStepUpResult stepUp = VanillaWorldZombieStepUp.Resolve(
                tiles, aiState.PositionX, aiState.PositionY, velocityX, velocityY, hitboxWidth, hitboxHeight);
            if (stepUp.Stepped)
                aiState = aiState with { PositionY = stepUp.PositionY };

            VanillaZombieObstacleMotionResult obstacle = VanillaWorldUnicornObstacleMotion.Resolve(
                tiles, aiState.PositionX, aiState.PositionY, velocityX, velocityY, hitboxWidth, hitboxHeight,
                simulation.DirectionX, simulation.DirectionY, simulation.SpriteDirection);
            velocityX = obstacle.VelocityX;
            velocityY = obstacle.VelocityY;
        }

        next = aiState with { VelocityX = velocityX, VelocityY = velocityY, Simulation = simulation };
        return true;
    }

    internal static bool TryFinishPhysics(WorldTileStore tiles, double worldSurfaceTiles,
        in NpcSnapshot npc, in NpcStateUpdate aiState, out NpcStateUpdate next, bool? fallThroughOverride = null)
    {
        if (!TryApplyGravity(tiles, worldSurfaceTiles, in aiState, out var accelerated, out var gravity))
        { next = default; return false; }
        if (!gravity.HasValue) { next = accelerated; return true; }
        return TryFinishCollision(tiles, in accelerated, gravity.Value.Parameters.Gravity, out next, fallThroughOverride);
    }

    // NPC.UpdateNPC: AI -> gravity -> GetHurtByOtherNPCs -> collision. Keeping these stages
    // separate permits contact knockback to reach collision in the same accepted NPC revision.
    internal static bool TryApplyGravity(WorldTileStore tiles, double worldSurfaceTiles,
        in NpcStateUpdate aiState, out NpcStateUpdate next, out VanillaNpcGravityResult? appliedGravity)
    {
        appliedGravity = null;
        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(aiState.Type), new NpcNetId(aiState.NetId), out var definition) ||
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.None ||
            !definition.TryResolveHitbox(aiState.Simulation, out var hitbox))
        {
            next = default;
            return false;
        }
        var simulation = aiState.Simulation;
        int hitboxWidth = hitbox.Width, hitboxHeight = hitbox.Height;
        float velocityX = aiState.VelocityX, velocityY = aiState.VelocityY;
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

        appliedGravity = gravity;
        if (velocityX < HorizontalVelocityEpsilon && velocityX > -HorizontalVelocityEpsilon)
            velocityX = 0f;
        next = aiState with { VelocityX = velocityX, VelocityY = velocityY };
        return true;
    }

    internal static bool TryFinishCollision(WorldTileStore tiles, in NpcStateUpdate aiState,
        float gravityAcceleration, out NpcStateUpdate next, bool? fallThroughOverride = null)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(aiState.Type), new NpcNetId(aiState.NetId), out var definition) ||
            !definition.TryResolveHitbox(aiState.Simulation, out var hitbox))
        { next = default; return false; }
        var simulation = aiState.Simulation;
        int hitboxWidth = hitbox.Width, hitboxHeight = hitbox.Height;
        float velocityX = aiState.VelocityX, velocityY = aiState.VelocityY;

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
            gravityAcceleration);

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
        bool fallThroughPlatforms = fallThroughOverride ?? (definition.PhysicsFamily switch
        {
            VanillaNpcPhysicsFamily.FlyingEye => true,
            VanillaNpcPhysicsFamily.BatFlight => true,
            VanillaNpcPhysicsFamily.GhostHover => true,
            VanillaNpcPhysicsFamily.Mothron => definition.Type == VanillaNpcIds.Mothron,
            VanillaNpcPhysicsFamily.GroundFighter => simulation.DirectionY == 1,
            VanillaNpcPhysicsFamily.UnicornGround => simulation.DirectionY == 1,
            _ => false
        });
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

    public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) =>
        NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(inner)?
            .DefersStatePublication(in before, in proposed) ?? false;

    public bool DeactivatesAfterCompletion(in NpcSnapshot before, in NpcSnapshot completed) =>
        NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(inner)?
            .DeactivatesAfterCompletion(in before, in completed) ?? false;

    public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (targeting is not null && targeting.HasSlimeContainedPlan(in before))
        {
            if (!targeting.TryGetSlimeContainedPlan(in before, in committed, out var planned) ||
                !TryFinishPhysics(tiles, worldSurfaceTiles, in before, in planned, out var final))
            {
                targeting.CancelSlimeContainedPlan();
                return default;
            }
            return targeting.CompleteSlimeContainedPlan(in before, in committed, in final);
        }
        if (targeting is not null && targeting.HasFlyingEyeRetainedPlan(in before))
        {
            if (!targeting.TryGetFlyingEyeRetainedPlan(in before, in committed, mutations, out var planned) ||
                !TryFinishPhysics(tiles, worldSurfaceTiles, in before, in planned, out var final,
                    VanillaFlyingEyeNpcCatalog.FleesDaylight(before.TypeIdentity)))
            {
                targeting.CancelFlyingEyeRetainedPlan();
                return default;
            }
            return targeting.CompleteFlyingEyeRetainedPlan(in before, in committed, in final, mutations);
        }
        if (targeting is not null && VanillaBigMimicNpcCatalog1458.IsSupported(before.TypeIdentity))
        {
            if (!targeting.TryGetBigMimicAcceptedPlan(in before, in committed, mutations, out var planned, out bool fallThrough) ||
                !TryFinishPhysics(tiles, worldSurfaceTiles, in before, in planned, out var final, fallThrough))
            {
                targeting.CancelBigMimicAcceptedPlan();
                return default;
            }
            // Source FindFrame's AI87 body updates sprite facing after collision only when vertical speed is zero.
            if (final.VelocityY == 0f)
                final = final with { Simulation = final.Simulation with { SpriteDirection = final.Simulation.DirectionX } };
            return targeting.CompleteBigMimicAcceptedPlan(in before, in committed, in final, mutations);
        }
        if (targeting is not null && VanillaMothronNpcCatalog1458.IsSupported(before.TypeIdentity))
        {
            if (!targeting.TryGetMothronAcceptedPlan(in before, in committed, mutations, out var planned) ||
                !TryFinishPhysics(tiles, worldSurfaceTiles, in before, in planned, out var final))
                return default;
            return targeting.CompleteMothronAcceptedPlan(in before, in committed, in final, mutations);
        }
        if (before.TypeIdentity == committed.TypeIdentity &&
            ((targeting is not null && VanillaGhostHoverNpcCatalog1458.IsSupported(before.TypeIdentity)) ||
             before.TypeIdentity == VanillaNpcIds.Nailhead || before.TypeIdentity == VanillaNpcIds.DrManFly || before.TypeIdentity == VanillaNpcIds.Frankenstein) &&
            NpcAiStateStepperComposition.FindCapability<INpcAiAcceptedWorldMotionPlanner>(inner) is { } planner)
        {
            Span<NpcAiProjectileIntent> shots = stackalloc NpcAiProjectileIntent[5];
            if (!planner.TryPlanBeforeWorldMotion(in before, in committed, shots, out int count, out var planned) ||
                !TryApplyWorldMotion(in before, in planned, out var final) ||
                !mutations.TryUpdateState(in committed, in final, out var completed)) return default;
            for (int i = 0; i < count; i++) mutations.TrySpawnProjectile(in completed, in shots[i], out _);
            return completed;
        }
        return NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(inner)?
            .CompleteCommittedState(in before, in committed, mutations) ?? committed;
    }

    public bool DeactivatesAfterStep(in NpcSnapshot before, in NpcStateUpdate proposed) =>
        NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(inner)?
            .DeactivatesAfterStep(in before, in proposed) ?? false;

    public void ApplyCommittedEffect(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(inner)?
            .ApplyCommittedEffect(in before, in committed, mutations);
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

    public void ApplyCommittedEffectAfterSpawns(
        in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
        NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(inner)?
            .ApplyCommittedEffectAfterSpawns(in before, in committed, mutations);

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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
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
