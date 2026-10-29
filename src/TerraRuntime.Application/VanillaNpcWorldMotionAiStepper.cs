using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct VanillaNpcTownCollisionContext1458(
    int HomeTileY, bool DayTime, bool Eclipse, bool InvasionActive);

internal delegate bool VanillaNpcCollisionContinuation1458(in NpcStateUpdate collision,
    out NpcStateUpdate continued);

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
    private RuntimeGroundCritterLifecycle1458? gemBunnyLifecycle;

    internal void SetGroundCritterLifecycle(RuntimeGroundCritterLifecycle1458 lifecycle) =>
        gemBunnyLifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

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
        if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var selected) &&
            !selected.SupportsRuntimeLifecycle)
        {
            next = default;
            return false;
        }
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
            targeting.HasSlimeContainedPlan(in npc) || targeting.HasBeePlan(in npc) || targeting.GroundCritter.HasPlan(in npc) ||
            targeting.NpcPhase.HasPlan(in npc))) ||
            npc.TypeIdentity == VanillaNpcIds.Nailhead || npc.TypeIdentity == VanillaNpcIds.DrManFly || npc.TypeIdentity == VanillaNpcIds.Frankenstein)
        {
            next = aiState;
            return true;
        }
        return TryApplyWorldMotion(in npc, in aiState, out next);
    }

    internal bool TryApplyWorldMotion(in NpcSnapshot npc, in NpcStateUpdate proposed, out NpcStateUpdate next,
        VanillaNpcRawPlayer1458? rawTarget = null)
    {
        NpcStateUpdate adjusted = proposed;
        // AI_003's Granite Golem defense returns before its terrain/jump tail,
        // including the tick on which the negative counter reaches zero.
        if (!(npc.Type == 482 && npc.Ai.Ai2 < 0f) &&
            !TryApplyTerrainMotion(in npc, in proposed, out adjusted, rawTarget))
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
        if (!TryFinishPhysics(tiles, worldSurfaceTiles, in npc, in adjusted, out next)) return false;
        // NPC.FindFrame runs after collision; both admitted undead frame branches face their
        // current direction on the ground. Signed identities retain their own physical bodies.
        if (VanillaGroundFighterNpcCatalog.IsUndeadVariantParent(npc.TypeIdentity) && next.VelocityY == 0f)
            next = next with { Simulation = next.Simulation with { SpriteDirection = next.Simulation.DirectionX } };
        return true;
    }

    internal bool TryApplyNpcPhaseWorldMotion(in NpcSnapshot before, in NpcStateUpdate proposed,
        in VanillaNpcRawPlayer1458 rawTarget, VanillaNpcCollisionContinuation1458 continuation,
        in VanillaNpcTownCollisionContext1458 home, out NpcStateUpdate next)
    {
        // Mid-AI Transform already materialized its new body. AI003's earlier
        // flag6 still belongs to the incoming actor, before SetDefaults clears JustHit.
        var working = before with { Type = proposed.Type, NetId = proposed.NetId,
            PositionX = proposed.PositionX, PositionY = proposed.PositionY,
            Simulation = proposed.Simulation };
        var adjusted = proposed;
        if (!(before.Type == 482 && before.Ai.Ai2 < 0f) &&
            !TryApplyTerrainMotion(in working, in proposed, out adjusted, rawTarget,
                before.VelocityX == 0f && !before.Simulation.JustHit))
        { next = default; return false; }
        NpcLiquidContactKind effective = proposed.Simulation.HoneyWet == true
            ? NpcLiquidContactKind.Honey : proposed.Simulation.LiquidContact;
        if (!TryApplyGravity(tiles, worldSurfaceTiles, in adjusted, out var accelerated, out var gravity, effective) ||
            !gravity.HasValue)
        { next = default; return false; }
        accelerated = accelerated with { Simulation = accelerated.Simulation with
            { HostileContactImmunity = Math.Max(0, accelerated.Simulation.HostileContactImmunity - 1) } };
        // NPCID.Sets.TakesDamageFromHostilesWithoutBeingFriendly includes these
        // exact passive endpoints, independently of the later catch-protection flag.
        if (accelerated.Simulation.Friendly == true || accelerated.Type is 55 or 230 or 592 or 593 or 687)
            accelerated = RuntimeTownNpcCombat1458.PlanFriendlyRegeneration(in accelerated);
        return TryFinishCollision(tiles, in accelerated, gravity.Value.Parameters.Gravity,
            out next, townContext: home, continuation: continuation);
    }

    internal bool TryApplyTerrainMotion(in NpcSnapshot npc, in NpcStateUpdate proposed, out NpcStateUpdate next,
        VanillaNpcRawPlayer1458? rawTarget = null, bool? sourceStuckHopEligible = null)
    {
        if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var selected) &&
            !selected.SupportsRuntimeLifecycle)
        {
            next = default;
            return false;
        }
        NpcStateUpdate aiState = proposed;
        bool fighterStuckHopEligible = sourceStuckHopEligible ?? (npc.VelocityX == 0f && !npc.Simulation.JustHit);
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.None)
        {
            next = aiState;
            return true;
        }
        bool hostileGroundCritter = VanillaNonresidentGroundCritterCatalog1458.TryGetEffectiveFighterDefinition(
            npc.Type, npc.Simulation, definition, out var fighterDefinition);
        if (hostileGroundCritter) definition = fighterDefinition;

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
            !(VanillaNpcTransformation1458.IsZombieMerman(definition.Type) && npc.Simulation.Wet) &&
            !(definition.Type == VanillaNpcIds.ThePossessed && simulation.NoGravity))
        {
            bool verifiedFighter = sourceStuckHopEligible.HasValue || hostileGroundCritter ||
                VanillaGroundFighterNpcCatalog.UsesVerifiedGroundFighterTargeting1458(definition.Type);
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
            if (definition.Type.Value is 508 or 580 or 582)
                obstacleParameters = obstacleParameters with { ForwardProbeOffset = hitboxWidth / 2 + 16 };
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

            VanillaGroundFighterDoorEnvironment doorEnvironment = ResolveDoorEnvironment(in aiState, rawTarget);
            NpcAiState beforeDoorAi = aiState.Ai;
            // AI_003 flag8=false keeps attack/defense counters out of the door
            // clock. Ground support still participates in the following jump tail.
            bool skipsDoorPressure = hostileGroundCritter ||
                definition.Type.Value is 381 or 382 or 449 or 450 or 451 or 452 or 464 or 470 or 481 or 482 or
                    508 or 580 or 582 or 520 or >= 524 and <= 529;
            skipsDoorPressure |= sourceStuckHopEligible.HasValue && definition.Type.Value is 199 or 532;
            VanillaZombieDoorContactResult doorContact = skipsDoorPressure
                ? new VanillaZombieDoorContactResult(velocityX, aiState.Ai,
                    velocityY == 0f && simulation.DirectionX != 0 &&
                    VanillaWorldZombieDoorContact.HasGroundSupport(tiles, aiState.PositionX, aiState.PositionY,
                        hitboxWidth, hitboxHeight), false, false)
                : (definition.Type == VanillaNpcIds.Psycho || definition.Type == VanillaNpcIds.DrManFly || definition.Type == VanillaNpcIds.ThePossessed)
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
                obstacleParameters,
                verifiedFighter ? simulation.SpriteDirection : null);
            velocityX = obstacle.VelocityX;
            velocityY = obstacle.VelocityY;
            bool movingWithSprite = velocityX < 0f && simulation.SpriteDirection == -1 ||
                velocityX > 0f && simulation.SpriteDirection == 1;
            // Source resets a non-door clock only in the final no-jump branch,
            // after the horizontal-facing gate. The door primitive precedes it.
            if (verifiedFighter && !doorContact.TouchingDoor && doorContact.GroundSupported &&
                (obstacle.Jumped || !movingWithSprite))
                aiState = aiState with { Ai = aiState.Ai with { Ai1 = beforeDoorAi.Ai1, Ai2 = beforeDoorAi.Ai2 } };
            if (obstacle.Jumped && verifiedFighter)
                aiState = aiState with { RequiresImmediateSync = true };

            if (doorContact.GroundSupported &&
                velocityY == 0f &&
                fighterStuckHopEligible &&
                (!verifiedFighter || movingWithSprite) &&
                aiState.Ai.Ai3 == 1f)
            {
                velocityY = stuckHopVelocity;
            }

            // AI_003's Expert elevated-target jump follows obstacle/stuck launches but precedes
            // the type-specific lunge. It deliberately does not set netUpdate itself.
            VanillaNpcTargetCandidate elevated = default;
            bool hasElevated;
            if (rawTarget is { } raw)
            {
                elevated = raw.Candidate;
                hasElevated = raw.Slot == aiState.Target;
            }
            else hasElevated = aiState.Target < byte.MaxValue && targeting is not null &&
                targeting.TryGetCandidate((byte)aiState.Target, out elevated);
            if (verifiedFighter &&
                doorContact.GroundSupported && velocityY == 0f &&
                (targeting?.GroundFighterExpertMode == true || VanillaNpcTransformation1458.IsZombieMerman(definition.Type)) &&
                ((velocityX < 0f && simulation.SpriteDirection == -1) ||
                 (velocityX > 0f && simulation.SpriteDirection == 1)) &&
                hasElevated)
            {
                float targetBottom = elevated.CenterY + elevated.Height * .5f;
                if (targetBottom < aiState.PositionY &&
                    MathF.Abs(aiState.PositionX + hitboxWidth * .5f - elevated.CenterX) < elevated.Width * 3f &&
                    VanillaWorldCanHit.HasLineOfSight(tiles, aiState.PositionX, aiState.PositionY,
                        hitboxWidth, hitboxHeight, elevated.CenterX - elevated.Width * .5f,
                        elevated.CenterY - elevated.Height * .5f, (int)elevated.Width, (int)elevated.Height))
                {
                    if (VanillaNpcTransformation1458.IsZombieMerman(definition.Type))
                    {
                        float difference = aiState.PositionY - targetBottom;
                        velocityY = difference < 7 * 16f ? -8.8f : difference < 8 * 16f ? -9.2f :
                            difference < 9 * 16f ? -9.7f : difference < 10 * 16f ? -10.3f :
                            difference < 11 * 16f ? -10.6f : difference < 14 * 16f ? -11f : 0f;
                    }
                    bool launch = targetBottom > aiState.PositionY - 6 * 16f;
                    int column = (int)((aiState.PositionX + hitboxWidth * .5f) / 16f);
                    int bottom = (int)((aiState.PositionY + hitboxHeight) / 16f) - 1;
                    for (int y = bottom; !launch && y > bottom - 6; y--)
                        launch = (uint)column < (uint)tiles.Dimensions.WidthTiles &&
                            (uint)y < (uint)tiles.Dimensions.HeightTiles &&
                            tiles.Get(column, y) is { IsActive: true, IsActuated: false } tile &&
                            VanillaTileIds.IsPlatform(tile.TileType);
                    if (launch && velocityY == 0f) velocityY = -7.9f;
                }
            }

            if (!hostileGroundCritter && hasFighterProfile &&
                fighterProfile.CloseRangeLunge &&
                doorEnvironment.HasTarget &&
                VanillaGroundFighterCloseRangeLunge.TryResolve(
                    aiState.PositionX + hitboxWidth / 2,
                    aiState.PositionY + hitboxHeight / 2,
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
                if (VanillaGroundFighterNpcCatalog.UsesVerifiedGroundFighterTargeting1458(definition.Type))
                    aiState = aiState with { RequiresImmediateSync = true };
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
        in NpcStateUpdate aiState, out NpcStateUpdate next, out VanillaNpcGravityResult? appliedGravity,
        NpcLiquidContactKind? liquidOverride = null)
    {
        appliedGravity = null;
        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(aiState.Type), new NpcNetId(aiState.NetId), out var definition) ||
            definition.PhysicsFamily == VanillaNpcPhysicsFamily.None ||
            !definition.TryResolveHitbox(aiState.Simulation, out var hitbox))
        {
            next = default;
            return false;
        }
        if (VanillaNonresidentGroundCritterCatalog1458.TryGetEffectiveFighterDefinition(
                aiState.Type, aiState.Simulation, definition, out var fighterDefinition))
            definition = fighterDefinition;
        var simulation = aiState.Simulation;
        int hitboxWidth = hitbox.Width, hitboxHeight = hitbox.Height;
        float velocityX = aiState.VelocityX, velocityY = aiState.VelocityY;
        if (!VanillaNpcGravity.TryApply(
                in definition,
                aiState.PositionY,
                velocityY,
                simulation.Wet,
                liquidOverride ?? simulation.LiquidContact,
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
        float gravityAcceleration, out NpcStateUpdate next, bool? fallThroughOverride = null,
        VanillaNpcTownCollisionContext1458? townContext = null,
        VanillaNpcCollisionContinuation1458? continuation = null)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(aiState.Type), new NpcNetId(aiState.NetId), out var definition) ||
            !definition.TryResolveHitbox(aiState.Simulation, out var hitbox))
        { next = default; return false; }
        // Collision observes a turn performed by AI007 immediately, even though
        // that first update did not execute the AI003 terrain section.
        if (VanillaNonresidentGroundCritterCatalog1458.TryGetEffectiveFighterDefinition(
                aiState.Type, aiState.Simulation, definition, out var fighterDefinition))
            definition = fighterDefinition;
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

        if (continuation is not null)
        {
            if (simulation.WetCount is not (>= 0 and <= 10) || simulation.LavaWet is null ||
                simulation.HoneyWet is null || simulation.ShimmerWet is not false ||
                VanillaWorldCollision.LavaCollision(tiles, aiState.PositionX, aiState.PositionY, hitboxWidth, hitboxHeight))
            { next = default; return false; }
            var contact = VanillaWorldCollision.GetLiquidContacts(tiles, aiState.PositionX, aiState.PositionY,
                hitboxWidth, hitboxHeight);
            if (contact.Shimmer || contact.Lava) { next = default; return false; }
            int cooldown = simulation.WetCount.Value;
            if (simulation.Wet != wet && cooldown == 0) cooldown = 10;
            if (cooldown > 0) cooldown--;
            simulation = simulation with { WetCount = cooldown,
                LavaWet = wet && simulation.LavaWet == true,
                HoneyWet = wet && (simulation.HoneyWet == true || contact.Honey), ShimmerWet = false };
        }

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
        if (definition.AiStyle.Value == 7 && townContext is { } home)
            fallThroughPlatforms = DecideTownPlatformFall(in aiState, hitboxHeight, in home);
        var collisionState = aiState;
        if (continuation is not null)
        {
            collisionState = aiState with { VelocityX = velocityX, VelocityY = velocityY,
                Simulation = simulation with { Wet = wet, LiquidContact = liquidContact,
                    OldVelocityX = oldVelocityX, OldVelocityY = oldVelocityY, CollideX = false, CollideY = false } };
            if (!continuation(in collisionState, out var continued) || !RuntimeNpcStore.IsValid(in continued) ||
                !VanillaNpcDefinitionCatalog.TryGet(new(continued.Type), new(continued.NetId), out definition) ||
                !definition.TryResolveHitbox(continued.Simulation, out hitbox))
            { next = default; return false; }
            collisionState = continued;
            simulation = continued.Simulation;
            velocityX = continued.VelocityX; velocityY = continued.VelocityY;
            hitboxWidth = hitbox.Width; hitboxHeight = hitbox.Height;
        }
        float oldDryVelocityX = velocityX, oldDryVelocityY = velocityY;
        VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
            tiles,
            collisionState.PositionX,
            collisionState.PositionY,
            velocityX,
            velocityY,
            hitboxWidth,
            hitboxHeight,
            fallThrough: fallThroughPlatforms,
            fall2: fallThroughPlatforms);

        float collidedVelocityX = collision.VelocityX;
        float collidedVelocityY = collision.HitCeiling ? 0.01f : collision.VelocityY;
        bool collideX = (wet ? oldDryVelocityX : oldVelocityX) != collidedVelocityX;
        bool collideY = (wet ? oldDryVelocityY : oldVelocityY) != collidedVelocityY;

        float movementX = collidedVelocityX;
        float movementY = collidedVelocityY;
        if (wet)
        {
            float slowdown = continuation is not null && simulation.HoneyWet == true ? HoneyMovementSpeed : liquidKind switch
            {
                WorldLiquidKind.Honey => HoneyMovementSpeed,
                WorldLiquidKind.Shimmer => ShimmerMovementSpeed,
                WorldLiquidKind.Lava => LavaMovementSpeed,
                _ => WaterMovementSpeed
            };

            movementX = collideX ? collidedVelocityX : collidedVelocityX * slowdown;
            movementY = collideY ? collidedVelocityY : collidedVelocityY * slowdown;
        }

        float oldPositionX = collisionState.PositionX;
        float oldPositionY = collisionState.PositionY;
        float nextPositionX = collisionState.PositionX + movementX;
        float nextPositionY = collisionState.PositionY + movementY;
        bool stairFall = simulation.StairFall == true || fallThroughPlatforms;
        if (definition.AiStyle.Value == 7 && townContext is { } slopeHome)
        {
            if (continuation is not null && simulation.HomeTileY is { } actualHome)
                slopeHome = slopeHome with { HomeTileY = actualHome };
            stairFall = DecideTownStairFall(tiles, nextPositionX, nextPositionY, hitboxWidth, hitboxHeight,
                simulation.TownNpc == true, in slopeHome, stairFall);
        }
        VanillaSlopeCollisionResult slope = VanillaWorldSlopeCollision.Resolve(
            tiles,
            nextPositionX,
            nextPositionY,
            collidedVelocityX,
            collidedVelocityY,
            hitboxWidth,
            hitboxHeight,
            stairFall);
        // A falling actor retains the incoming flag even without stair contact. Otherwise only
        // actual slope contact retains it (NPC.Collision_MoveSlopesAndStairFall, 1.4.5.8).
        stairFall = slope.StairFall || (fallThroughPlatforms && stairFall);
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

        next = collisionState with
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
                SolidCollision = solidCollision,
                StairFall = stairFall
            }
        };
        return true;
    }

    // Collision_DecideFallThroughPlatforms runs before movement; the subsequent stair policy
    // observes integrated coordinates and deliberately uses a different home threshold.
    internal static bool DecideTownPlatformFall(in NpcStateUpdate state, int height,
        in VanillaNpcTownCollisionContext1458 home)
    {
        bool returning = state.Simulation.TownNpc == true &&
            (!home.DayTime || home.InvasionActive || home.Eclipse);
        if (returning)
            return (state.PositionY + height - 8f) / 16f < home.HomeTileY - 1;
        return home.HomeTileY - (int)(state.PositionY + height) / 16 > 16;
    }

    // The source stair policy does not use invasion here. HomeTileY is the stored home, rather
    // than the resting-floor candidate computed by the AI007 furniture search.
    internal static bool DecideTownStairFall(WorldTileStore tiles, float x, float y, int width, int height,
        bool townNpc, in VanillaNpcTownCollisionContext1458 home, bool retained)
    {
        int tileX = (int)(x + width * .5f) / 16, tileY = (int)y / 16;
        if ((uint)tileX >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)tileY >= (uint)tiles.Dimensions.HeightTiles) return retained;
        WorldTile head = tiles.Get(tileX, tileY);
        bool adjust = townNpc && head.IsActive && VanillaTileCollisionCatalog.IsSolid(head.TileType);
        adjust |= !home.DayTime || home.Eclipse || home.HomeTileY - (int)(y + height) / 16 > 16;
        return adjust ? (y + height - 8f) / 16f < home.HomeTileY : retained;
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
        if (targeting is not null && targeting.NpcPhase.HasPlan(in before))
            return targeting.NpcPhase.Complete(in before, in committed);
        if (targeting is not null && targeting.GroundCritter.HasPlan(in before))
        {
            if (gemBunnyLifecycle is null ||
                !targeting.GroundCritter.TryGetConditions(in before, out bool dayTime, out bool eclipse))
            { targeting.GroundCritter.Cancel(); return default; }
            // Invasion only changes Collision_DecideFallThroughPlatforms for townNPC=true.
            // These exact seven nonresident subtypes retain their captured day/eclipse/home policy.
            return gemBunnyLifecycle.Complete(in before, in committed, targeting.GroundCritter,
                dayTime, eclipse, invasionActive: false);
        }
        if (targeting is not null && targeting.HasBeePlan(in before))
        {
            if (!targeting.TryGetBeePlan(in before, in committed, out var planned) ||
                !TryFinishPhysics(tiles, worldSurfaceTiles, in before, in planned, out var final, true))
            {
                targeting.CancelBeePlan();
                return default;
            }
            return targeting.CompleteBeePlan(in before, in committed, in final);
        }
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

    private VanillaGroundFighterDoorEnvironment ResolveDoorEnvironment(in NpcStateUpdate aiState,
        VanillaNpcRawPlayer1458? rawTarget = null)
    {
        bool bloodMoonActive = worldEvents?.BloodMoonActive == true;
        if (rawTarget is { IsValid: true } raw && raw.Slot == aiState.Target)
            return new(bloodMoonActive, true, raw.PositionX + raw.Width / 2, raw.PositionY + raw.Height / 2);
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
