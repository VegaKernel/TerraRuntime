using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Converts high-level NPC actor intent into bounded AI velocity/target state. It never advances position itself;
/// the returned state is intended to flow into TerraRuntime's source-backed world-motion/collision stepper.
/// </summary>
public sealed class RuntimeNpcActorIntentStateStepper : INpcAiStateStepper
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
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        RuntimeNpcActorControlSnapshot snapshot = _controls.Snapshot;
        if (!snapshot.TryGet(npc.Handle, out NpcActorControlBinding binding))
            return _fallback.TryStepState(in npc, out next);

        if (npc.TypeIdentity != VanillaNpcIds.Zombie ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition) ||
            definition.AiStyle != VanillaNpcAiStyles.Fighter)
        {
            return _fallback.TryStepState(in npc, out next);
        }

        NpcActorIntent intent = binding.Intent;
        if (!intent.IsValid)
        {
            next = default;
            return false;
        }

        NpcActorMotionOptions motion = intent.Motion;
        switch (intent.Kind)
        {
            case NpcActorIntentKind.Stop:
                next = BuildControlledUpdate(
                    in npc,
                    velocityX: MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
                    directionX: npc.Simulation.DirectionX,
                    directionY: 0,
                    target: npc.Target);
                return true;

            case NpcActorIntentKind.MoveTo:
                return TryBuildMoveTo(
                    in npc,
                    in definition,
                    intent.TargetX,
                    intent.TargetY,
                    npc.Target,
                    in motion,
                    out next);

            case NpcActorIntentKind.FollowPlayer:
                if (!_players.TryGetPlayer(intent.TargetPlayer, out PlayerStateSnapshot player) ||
                    (player.HasHealth && player.IsDead))
                {
                    next = BuildControlledUpdate(
                        in npc,
                        velocityX: MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
                        directionX: npc.Simulation.DirectionX,
                        directionY: 0,
                        target: npc.Target);
                    return true;
                }

                float targetX = player.PositionX + VanillaBasePlayerWidth * 0.5f;
                float targetY = player.PositionY + VanillaBasePlayerHeight * 0.5f;
                return TryBuildMoveTo(
                    in npc,
                    in definition,
                    targetX,
                    targetY,
                    player.Player.Slot.Value,
                    in motion,
                    out next);

            default:
                next = default;
                return false;
        }
    }

    private static bool TryBuildMoveTo(
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

        if (motion.MaximumDistance > 0f)
        {
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;
            float maximumSquared = motion.MaximumDistance * motion.MaximumDistance;
            if (!float.IsFinite(distanceSquared) || distanceSquared > maximumSquared)
            {
                next = BuildControlledUpdate(
                    in npc,
                    velocityX: MoveTowards(npc.VelocityX, 0f, motion.HorizontalAcceleration),
                    directionX: npc.Simulation.DirectionX,
                    directionY: 0,
                    target: npc.Target);
                return true;
            }
        }

        int directionX = Math.Abs(deltaX) <= motion.StopDistance
            ? 0
            : deltaX > 0f ? 1 : -1;
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
            facing,
            directionY,
            target);
        return true;
    }

    private static NpcStateUpdate BuildControlledUpdate(
        in NpcSnapshot npc,
        float velocityX,
        int directionX,
        int directionY,
        ushort target)
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
            npc.VelocityY,
            target,
            npc.Ai,
            npc.Simulation with
            {
                DirectionX = directionX,
                DirectionY = directionY,
                SpriteDirection = spriteDirection,
                NoGravity = false
            });
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (current < target)
            return Math.Min(current + maxDelta, target);
        if (current > target)
            return Math.Max(current - maxDelta, target);
        return target;
    }
}
