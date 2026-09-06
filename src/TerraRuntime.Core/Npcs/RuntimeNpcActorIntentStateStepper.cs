using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Converts high-level NPC actor intent into bounded AI velocity/target state. It never advances position itself;
/// the returned state is intended to flow into TerraRuntime's source-backed world-motion/collision stepper.
/// Ground actors use the verified fighter traversal lane. Controlled flying actors reuse the verified 1.4.5.8
/// directional pursuit primitives for their admitted family; Stop/near-target damping is TerraRuntime actor-control
/// policy and does not opt the actor back into ordinary vanilla AI side effects such as hostile projectile attacks.
/// </summary>
public sealed class RuntimeNpcActorIntentStateStepper : INpcAiStateStepper, INpcAiStateStepperWrapper
{
    private const float VanillaBasePlayerWidth = 20f;
    private const float VanillaBasePlayerHeight = 42f;
    private const float VerticalDecisionThreshold = 8f;

    private readonly INpcAiStateStepper _fallback;
    private readonly RuntimeNpcActorControlRegistry _controls;
    private readonly IRuntimePlayerSnapshotLookup _players;

    public RuntimeNpcActorIntentStateStepper(
        INpcAiStateStepper fallback,
        RuntimeNpcActorControlRegistry controls,
        IRuntimePlayerSnapshotLookup players)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(players);
        _fallback = fallback;
        _controls = controls;
        _players = players;

        VanillaNpcTargetingAiStepper? targeting =
            NpcAiStateStepperComposition.FindCapability<VanillaNpcTargetingAiStepper>(fallback);
        if (targeting is not null && players is IRuntimePlayerSlotSnapshotLookup playerSlots)
            targeting.SetPlayerSnapshotLookup(playerSlots);
    }

    public INpcAiStateStepper InnerStepper => _fallback;

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        RuntimeNpcActorControlSnapshot snapshot = _controls.Snapshot;
        if (!snapshot.TryGet(npc.Handle, out NpcActorControlBinding binding))
            return _fallback.TryStepState(in npc, out next);

        if (!VanillaNpcActorControlSupport1458.TryGetMotionFamily(
                npc.TypeIdentity,
                out VanillaNpcActorControlMotionFamily1458 family) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition))
        {
            return _fallback.TryStepState(in npc, out next);
        }

        NpcActorIntent intent = binding.Intent;
        if (!intent.IsValid)
        {
            next = default;
            return false;
        }

        return family switch
        {
            VanillaNpcActorControlMotionFamily1458.GroundFighter =>
                TryStepGroundFighter(in npc, in definition, in intent, out next),
            VanillaNpcActorControlMotionFamily1458.FlyingEye =>
                TryStepFlyingEye(in npc, in definition, in intent, out next),
            VanillaNpcActorControlMotionFamily1458.Flyer =>
                TryStepFlyer(in npc, in definition, in intent, out next),
            VanillaNpcActorControlMotionFamily1458.Bat =>
                TryStepBat(in npc, in definition, in intent, out next),
            _ => Fail(out next)
        };
    }

    private bool TryStepGroundFighter(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in NpcActorIntent intent,
        out NpcStateUpdate next)
    {
        NpcActorMotionOptions motion = intent.Motion;
        switch (intent.Kind)
        {
            case NpcActorIntentKind.Stop:
                next = BuildControlledUpdate(
                    in npc,
                    velocityX: MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
                    velocityY: npc.VelocityY,
                    directionX: npc.Simulation.DirectionX,
                    directionY: 0,
                    target: npc.Target,
                    noGravity: false);
                return true;

            case NpcActorIntentKind.MoveTo:
                return TryBuildGroundMoveTo(
                    in npc,
                    in definition,
                    intent.TargetX,
                    intent.TargetY,
                    npc.Target,
                    in motion,
                    out next);

            case NpcActorIntentKind.FollowPlayer:
                if (!TryResolveFollowTarget(intent.TargetPlayer, out PlayerStateSnapshot player))
                {
                    next = BuildControlledUpdate(
                        in npc,
                        velocityX: MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
                        velocityY: npc.VelocityY,
                        directionX: npc.Simulation.DirectionX,
                        directionY: 0,
                        target: npc.Target,
                        noGravity: false);
                    return true;
                }

                return TryBuildGroundMoveTo(
                    in npc,
                    in definition,
                    PlayerCenterX(in player),
                    PlayerCenterY(in player),
                    player.Player.Slot.Value,
                    in motion,
                    out next);

            default:
                return Fail(out next);
        }
    }

    private bool TryStepFlyingEye(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in NpcActorIntent intent,
        out NpcStateUpdate next)
    {
        int lifeMax = npc.Simulation.LifeMax > 0 ? npc.Simulation.LifeMax : definition.LifeMax;
        int life = npc.Simulation.LifeMax > 0 ? npc.Simulation.Life : definition.LifeMax;
        if (!VanillaFlyingEyeNpcCatalog.TryGetMotionProfile(
                definition.Type,
                life,
                lifeMax,
                out VanillaFlyingEyeMotionProfile profile))
        {
            return Fail(out next);
        }

        if (intent.Kind == NpcActorIntentKind.Stop)
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Vertical.Acceleration,
                target: npc.Target);
            return true;
        }

        if (!TryResolveIntentTarget(in intent, out float targetX, out float targetY, out ushort target))
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Vertical.Acceleration,
                target: npc.Target);
            return true;
        }

        float centerX = npc.PositionX + definition.Width * npc.Simulation.Scale * 0.5f;
        float centerY = npc.PositionY + definition.Height * npc.Simulation.Scale * 0.5f;
        float deltaX = targetX - centerX;
        float deltaY = targetY - centerY;
        if (!WithinMaximumDistance(deltaX, deltaY, intent.Motion.MaximumDistance))
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Vertical.Acceleration,
                target: npc.Target);
            return true;
        }

        int directionX = AxisDirection(deltaX, intent.Motion.StopDistance);
        int directionY = AxisDirection(deltaY, intent.Motion.StopDistance);
        var input = new VanillaDemonEyeMotionInput(
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            OldVelocityX: npc.Simulation.OldVelocityX,
            OldVelocityY: npc.Simulation.OldVelocityY,
            DirectionX: directionX,
            DirectionY: directionY,
            Scale: npc.Simulation.Scale,
            NoTileCollide: npc.Simulation.NoTileCollide,
            CollideX: npc.Simulation.CollideX,
            CollideY: npc.Simulation.CollideY,
            Wet: npc.Simulation.Wet);
        if (!VanillaDemonEyeMotion.TryStep(in input, in profile, out VanillaDemonEyeMotionResult result))
            return Fail(out next);

        float velocityX = directionX == 0
            ? MoveTowards(npc.VelocityX, 0f, intent.Motion.HorizontalAcceleration)
            : LimitControlledHorizontalVelocity(npc.VelocityX, result.VelocityX, intent.Motion);
        float velocityY = directionY == 0
            ? MoveTowards(npc.VelocityY, 0f, profile.Vertical.Acceleration)
            : result.VelocityY;
        int facing = directionX == 0 ? npc.Simulation.DirectionX : directionX;
        next = BuildControlledUpdate(
            in npc,
            velocityX,
            velocityY,
            facing,
            directionY,
            target,
            noGravity: result.NoGravity);
        return true;
    }

    private bool TryStepFlyer(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in NpcActorIntent intent,
        out NpcStateUpdate next)
    {
        if (!VanillaFlyerNpcCatalog.TryGetMotionProfile(definition.Type, out VanillaFlyerMotionProfile profile))
            return Fail(out next);

        if (intent.Kind == NpcActorIntentKind.Stop)
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Acceleration,
                target: npc.Target);
            return true;
        }

        if (!TryResolveIntentTarget(in intent, out float targetX, out float targetY, out ushort target))
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Acceleration,
                target: npc.Target);
            return true;
        }

        float centerX = npc.PositionX + definition.Width * npc.Simulation.Scale * 0.5f;
        float centerY = npc.PositionY + definition.Height * npc.Simulation.Scale * 0.5f;
        float deltaX = targetX - centerX;
        float deltaY = targetY - centerY;
        if (!WithinMaximumDistance(deltaX, deltaY, intent.Motion.MaximumDistance))
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Acceleration,
                target: npc.Target);
            return true;
        }

        float distanceSquared = deltaX * deltaX + deltaY * deltaY;
        if (!float.IsFinite(distanceSquared))
            return Fail(out next);
        if (distanceSquared <= intent.Motion.StopDistance * intent.Motion.StopDistance)
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: profile.Acceleration,
                target: target);
            return true;
        }

        var input = new VanillaServantOfCthulhuMotionInput(
            NpcCenterX: centerX,
            NpcCenterY: centerY,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            TargetCenterX: targetX,
            TargetCenterY: targetY,
            OldVelocityX: npc.Simulation.OldVelocityX,
            OldVelocityY: npc.Simulation.OldVelocityY,
            CollideX: npc.Simulation.CollideX,
            CollideY: npc.Simulation.CollideY,
            Wet: npc.Simulation.Wet);
        if (!VanillaServantOfCthulhuMotion.TryStep(in input, in profile, out VanillaServantOfCthulhuMotionResult result))
            return Fail(out next);

        float velocityX = LimitControlledHorizontalVelocity(npc.VelocityX, result.VelocityX, intent.Motion);
        int directionX = AxisDirection(deltaX, intent.Motion.StopDistance);
        int directionY = AxisDirection(deltaY, intent.Motion.StopDistance);
        int facing = directionX == 0 ? npc.Simulation.DirectionX : directionX;
        next = BuildControlledUpdate(
            in npc,
            velocityX,
            result.VelocityY,
            facing,
            directionY,
            target,
            noGravity: true);
        return true;
    }

    private bool TryStepBat(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in NpcActorIntent intent,
        out NpcStateUpdate next)
    {
        const float VanillaBatVerticalAcceleration = 0.04f;

        if (intent.Kind == NpcActorIntentKind.Stop)
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: VanillaBatVerticalAcceleration,
                target: npc.Target);
            return true;
        }

        if (!TryResolveIntentTarget(in intent, out float targetX, out float targetY, out ushort target))
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: VanillaBatVerticalAcceleration,
                target: npc.Target);
            return true;
        }

        float centerX = npc.PositionX + definition.Width * npc.Simulation.Scale * 0.5f;
        float centerY = npc.PositionY + definition.Height * npc.Simulation.Scale * 0.5f;
        float deltaX = targetX - centerX;
        float deltaY = targetY - centerY;
        if (!WithinMaximumDistance(deltaX, deltaY, intent.Motion.MaximumDistance))
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: VanillaBatVerticalAcceleration,
                target: npc.Target);
            return true;
        }

        int directionX = AxisDirection(deltaX, intent.Motion.StopDistance);
        int directionY = AxisDirection(deltaY, intent.Motion.StopDistance);
        if (directionX == 0 && directionY == 0)
        {
            next = BuildFlightStop(
                in npc,
                intent.Motion,
                verticalAcceleration: VanillaBatVerticalAcceleration,
                target: target);
            return true;
        }

        // AI_014's pure pursuit slice validates the TargetClosest result even though the slot does not affect its
        // velocity math. Vanilla TargetClosest always supplies signed directions; actor-control may independently
        // damp one axis inside StopDistance, so keep those two concepts separate. MoveTo deliberately has no hostile
        // NPC.target, therefore it uses a bounded internal steering token and publishes DefaultTarget below.
        ushort pursuitTarget = target == VanillaNpcDefinitionCatalog.DefaultTarget ? (ushort)0 : target;
        int pursuitDirectionX = deltaX > 0f ? 1 : -1;
        int pursuitDirectionY = deltaY > 0f ? 1 : -1;
        var input = new VanillaBatPursuitInput1458(
            npc.VelocityX,
            npc.VelocityY,
            npc.Simulation.OldVelocityX,
            npc.Simulation.OldVelocityY,
            pursuitDirectionX,
            pursuitDirectionY,
            pursuitTarget,
            npc.Simulation.Wet,
            npc.Simulation.CollideX,
            npc.Simulation.CollideY);
        if (!VanillaBatMotion1458.TryStepPursuit(
                definition.Type,
                in input,
                out VanillaBatPursuitResult1458 result))
        {
            return Fail(out next);
        }

        float velocityX = directionX == 0
            ? MoveTowards(npc.VelocityX, 0f, intent.Motion.HorizontalAcceleration)
            : LimitControlledHorizontalVelocity(npc.VelocityX, result.VelocityX, intent.Motion);
        float velocityY = directionY == 0
            ? MoveTowards(npc.VelocityY, 0f, VanillaBatVerticalAcceleration)
            : result.VelocityY;
        int facing = directionX == 0 ? npc.Simulation.DirectionX : directionX;
        next = BuildControlledUpdate(
            in npc,
            velocityX,
            velocityY,
            facing,
            directionY,
            target,
            noGravity: true);
        return true;
    }

    private bool TryResolveIntentTarget(
        in NpcActorIntent intent,
        out float targetX,
        out float targetY,
        out ushort target)
    {
        if (intent.Kind == NpcActorIntentKind.MoveTo)
        {
            targetX = intent.TargetX;
            targetY = intent.TargetY;
            target = VanillaNpcDefinitionCatalog.DefaultTarget;
            return true;
        }

        if (intent.Kind == NpcActorIntentKind.FollowPlayer &&
            TryResolveFollowTarget(intent.TargetPlayer, out PlayerStateSnapshot player))
        {
            targetX = PlayerCenterX(in player);
            targetY = PlayerCenterY(in player);
            target = player.Player.Slot.Value;
            return true;
        }

        targetX = 0f;
        targetY = 0f;
        target = VanillaNpcDefinitionCatalog.DefaultTarget;
        return false;
    }

    private bool TryResolveFollowTarget(PlayerHandle target, out PlayerStateSnapshot player)
    {
        if (!_players.TryGetPlayer(target, out player) ||
            player.Player != target ||
            (player.HasHealth && player.IsDead))
        {
            player = default;
            return false;
        }

        return true;
    }

    private static bool TryBuildGroundMoveTo(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        float targetX,
        float targetY,
        ushort target,
        in NpcActorMotionOptions motion,
        out NpcStateUpdate next)
    {
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float deltaX = targetX - centerX;
        float deltaY = targetY - centerY;

        if (!WithinMaximumDistance(deltaX, deltaY, motion.MaximumDistance))
        {
            next = BuildControlledUpdate(
                in npc,
                velocityX: MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
                velocityY: npc.VelocityY,
                directionX: npc.Simulation.DirectionX,
                directionY: 0,
                target: npc.Target,
                noGravity: false);
            return true;
        }

        int directionX = AxisDirection(deltaX, motion.StopDistance);
        int directionY = deltaY > VerticalDecisionThreshold
            ? 1
            : deltaY < -VerticalDecisionThreshold ? -1 : 0;
        float desiredVelocityX = directionX * motion.MaximumHorizontalSpeed;
        float velocityX = MoveTowards(
            npc.VelocityX,
            desiredVelocityX,
            motion.HorizontalAcceleration);

        int facing = directionX == 0 ? npc.Simulation.DirectionX : directionX;
        next = BuildControlledUpdate(
            in npc,
            velocityX,
            npc.VelocityY,
            facing,
            directionY,
            target,
            noGravity: false);
        return true;
    }

    private static NpcStateUpdate BuildFlightStop(
        in NpcSnapshot npc,
        in NpcActorMotionOptions motion,
        float verticalAcceleration,
        ushort target) =>
        BuildControlledUpdate(
            in npc,
            MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
            MoveTowards(npc.VelocityY, 0f, verticalAcceleration),
            npc.Simulation.DirectionX,
            0,
            target,
            noGravity: true);

    private static NpcStateUpdate BuildControlledUpdate(
        in NpcSnapshot npc,
        float velocityX,
        float velocityY,
        int directionX,
        int directionY,
        ushort target,
        bool noGravity)
    {
        int spriteDirection = directionX == 0
            ? npc.Simulation.SpriteDirection
            : directionX;
        return new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            velocityX,
            velocityY,
            target,
            npc.Ai,
            npc.Simulation with
            {
                DirectionX = directionX,
                DirectionY = directionY,
                SpriteDirection = spriteDirection,
                NoGravity = noGravity
            });
    }

    private static float LimitControlledHorizontalVelocity(
        float current,
        float sourceBackedProposal,
        in NpcActorMotionOptions motion)
    {
        float boundedDelta = Math.Clamp(
            sourceBackedProposal - current,
            -motion.HorizontalAcceleration,
            motion.HorizontalAcceleration);
        return Math.Clamp(
            current + boundedDelta,
            -motion.MaximumHorizontalSpeed,
            motion.MaximumHorizontalSpeed);
    }

    private static bool WithinMaximumDistance(float deltaX, float deltaY, float maximumDistance)
    {
        float distanceSquared = deltaX * deltaX + deltaY * deltaY;
        if (!float.IsFinite(distanceSquared))
            return false;
        return maximumDistance <= 0f || distanceSquared <= maximumDistance * maximumDistance;
    }

    private static int AxisDirection(float delta, float stopDistance) =>
        Math.Abs(delta) <= stopDistance ? 0 : delta > 0f ? 1 : -1;

    private static float PlayerCenterX(in PlayerStateSnapshot player) =>
        player.PositionX + VanillaBasePlayerWidth * 0.5f;

    private static float PlayerCenterY(in PlayerStateSnapshot player) =>
        player.PositionY + VanillaBasePlayerHeight * 0.5f;

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (current < target)
            return Math.Min(current + maxDelta, target);
        if (current > target)
            return Math.Max(current - maxDelta, target);
        return target;
    }

    private static bool Fail(out NpcStateUpdate next)
    {
        next = default;
        return false;
    }
}
