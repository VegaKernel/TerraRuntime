using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

internal interface IVanillaNpcBehaviorStrategy
{
    bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next);
}

internal sealed class VanillaFlyingEyeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.DemonEye)
        {
            next = default;
            return false;
        }

        NpcSnapshot targeted = npc;
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest))
        {
            targeted = npc with
            {
                Target = closest.Target,
                Simulation = npc.Simulation with
                {
                    DirectionX = closest.DirectionX,
                    DirectionY = closest.DirectionY
                }
            };
        }

        return inner.TryStepState(in targeted, out next);
    }
}

internal sealed class VanillaSlimeGroundNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Slime)
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        NpcSimulationState simulation = npc.Simulation;
        bool damaged = simulation.LifeMax > 0 && simulation.Life != simulation.LifeMax;
        bool engaged = !context.DayTime ||
                       damaged ||
                       context.SlimeRainActive ||
                       npc.PositionY > context.WorldSurfacePixels;
        if (!VanillaSlimeNpcCatalog.TryGetMotionProfile(definition.Type, out VanillaSlimeMotionProfile profile) ||
            !profile.IsValid)
        {
            next = default;
            return false;
        }
        var input = new VanillaBlueSlimeMotionInput(
            PositionX: npc.PositionX,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            OldVelocityY: simulation.OldVelocityY,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            Target: npc.Target,
            Ai: npc.Ai,
            Wet: simulation.Wet,
            CollideX: simulation.CollideX,
            CollideY: simulation.CollideY,
            Engaged: engaged,
            SolidCollision: simulation.SolidCollision,
            ClosestTarget: closest,
            TimerBonus: profile.TimerBonus,
            JumpTimerBand: profile.JumpTimerBand);

        if (!VanillaBlueSlimeMotion.TryStep(in input, out VanillaBlueSlimeMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            result.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = false
            });
        return true;
    }
}

internal sealed class VanillaGroundFighterNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Fighter)
        {
            next = default;
            return false;
        }

        if (!VanillaGroundFighterBehaviorCatalog.TryGet(definition.Type, out VanillaGroundFighterBehaviorParameters parameters) ||
            !parameters.IsValid)
        {
            next = default;
            return false;
        }

        bool daytimeSurface = context.DayTime && npc.PositionY < context.WorldSurfacePixels;
        int startingDirectionY = npc.Simulation.DirectionY;
        if (npc.Target < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)npc.Target), out VanillaNpcTargetCandidate currentTarget) &&
            currentTarget.Active &&
            !currentTarget.Dead &&
            !currentTarget.Ghost &&
            currentTarget.CenterY + VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f ==
            npc.PositionY + definition.Height)
        {
            startingDirectionY = -1;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        int fighterDirectionY = closest.DirectionY;
        if (closest.HasTarget &&
            fighterDirectionY > 0 &&
            context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate selectedCandidate) &&
            selectedCandidate.CenterY <= npc.PositionY + definition.Height)
        {
            fighterDirectionY = -1;
        }

        var fighterTarget = new VanillaZombieTargetRefresh(
            closest.HasTarget,
            closest.Target,
            closest.DirectionX,
            fighterDirectionY);

        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaZombieMotionInput(
            PositionX: npc.PositionX,
            OldPositionX: simulation.OldPositionX,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            DirectionX: simulation.DirectionX,
            DirectionY: startingDirectionY,
            Target: npc.Target,
            Ai: npc.Ai,
            Scale: simulation.Scale,
            TargetOverlaps: context.TargetOverlapsNpc(in npc, in definition),
            ClosestTarget: fighterTarget)
        {
            BaseMaximumHorizontalSpeed = parameters.BaseMaximumHorizontalSpeed,
            HorizontalAcceleration = parameters.HorizontalAcceleration,
            StuckThreshold = parameters.StuckThreshold,
            MaximumStuckCounter = parameters.MaximumStuckCounter,
            EncouragedDespawnTime = parameters.EncouragedDespawnTime,
            PursuitAllowed = !daytimeSurface,
            EncourageDespawn = daytimeSurface,
            JustHit = simulation.JustHit,
            TimeLeft = simulation.TimeLeft,
            SpriteDirection = simulation.SpriteDirection
        };

        if (!VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                SpriteDirection = result.SpriteDirection,
                NoGravity = false,
                JustHit = false,
                TimeLeft = result.TimeLeft
            });
        return true;
    }
}

internal sealed class VanillaEyeOfCthulhuNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.EyeOfCthulhu || !definition.IsBoss)
        {
            next = default;
            return false;
        }

        NpcSnapshot targeted = npc;
        bool targetAvailable = TryGetUsableTarget(targeted.Target, context, out VanillaNpcTargetCandidate candidate);
        if (!targetAvailable || candidate.Dead)
        {
            if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) &&
                context.TryFindCandidate(checked((byte)closest.Target), out candidate))
            {
                targeted = npc with
                {
                    Target = closest.Target,
                    Simulation = npc.Simulation with
                    {
                        DirectionX = closest.DirectionX,
                        DirectionY = closest.DirectionY
                    }
                };
                targetAvailable = candidate.Active && !candidate.Ghost;
            }
            else
            {
                targetAvailable = false;
                candidate = default;
            }
        }

        NpcSimulationState simulation = targeted.Simulation;
        int lifeMax = simulation.LifeMax > 0 ? simulation.LifeMax : definition.LifeMax;
        int life = simulation.LifeMax > 0 ? simulation.Life : definition.LifeMax;
        var input = new VanillaEyeOfCthulhuMotionInput(
            NpcCenterX: targeted.PositionX + definition.Width * 0.5f,
            NpcCenterY: targeted.PositionY + definition.Height * 0.5f,
            NpcBottomY: targeted.PositionY + definition.Height,
            VelocityX: targeted.VelocityX,
            VelocityY: targeted.VelocityY,
            Target: targeted.Target,
            Ai: targeted.Ai,
            Life: life,
            LifeMax: lifeMax,
            TimeLeft: simulation.TimeLeft,
            DayTime: context.DayTime,
            TargetAvailable: targetAvailable,
            TargetDead: !targetAvailable || candidate.Dead,
            TargetCenterX: candidate.CenterX,
            TargetCenterY: candidate.CenterY,
            TargetTopY: candidate.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f);

        if (!VanillaEyeOfCthulhuMotion.TryStep(in input, out VanillaEyeOfCthulhuMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            targeted.NetId,
            targeted.PositionX,
            targeted.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                NoGravity = true,
                NoTileCollide = true,
                TimeLeft = result.TimeLeft
            });
        return true;
    }

    private static bool TryGetUsableTarget(
        ushort target,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate candidate)
    {
        if (target < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)target), out candidate) &&
            candidate.Active &&
            !candidate.Ghost)
        {
            return true;
        }

        candidate = default;
        return false;
    }
}

internal sealed class VanillaServantOfCthulhuNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Flyer ||
            !VanillaFlyerNpcCatalog.TryGetMotionProfile(
                definition.Type,
                out VanillaFlyerMotionProfile profile) ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        if (VanillaFlyerNpcCatalog.UsesScaleSpeedHandicap(definition.Type))
        {
            float speedFactor = 2f - npc.Simulation.Scale;
            if (!float.IsFinite(speedFactor) || speedFactor <= 0f)
            {
                next = default;
                return false;
            }

            profile = profile with
            {
                MaximumSpeed = profile.MaximumSpeed * speedFactor,
                Acceleration = profile.Acceleration * speedFactor
            };
        }

        if (!context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) ||
            !context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate candidate))
        {
            NpcSimulationState idleSimulation = npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn
            };
            next = new NpcStateUpdate(
                definition.Type.Value,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                idleSimulation);
            return true;
        }

        var input = new VanillaServantOfCthulhuMotionInput(
            NpcCenterX: npc.PositionX + hitbox.Width * 0.5f,
            NpcCenterY: npc.PositionY + hitbox.Height * 0.5f,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            TargetCenterX: candidate.CenterX,
            TargetCenterY: candidate.CenterY,
            OldVelocityX: npc.Simulation.OldVelocityX,
            OldVelocityY: npc.Simulation.OldVelocityY,
            CollideX: npc.Simulation.CollideX,
            CollideY: npc.Simulation.CollideY,
            Wet: npc.Simulation.Wet);
        if (!VanillaServantOfCthulhuMotion.TryStep(
                in input,
                in profile,
                out VanillaServantOfCthulhuMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            closest.Target,
            npc.Ai,
            npc.Simulation with
            {
                DirectionX = closest.DirectionX,
                DirectionY = closest.DirectionY,
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn
            });
        return true;
    }
}

internal sealed class VanillaWormNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Worm ||
            !VanillaWormNpcCatalog.TryGet(definition.Type, out VanillaWormNpcEntry worm))
        {
            next = default;
            return false;
        }

        if (worm.Role == VanillaWormSegmentRole.Head)
            return inner.TryStepState(in npc, out next);

        float rawLeaderSlot = npc.Ai.Ai1;
        bool validLeaderSlot =
            float.IsFinite(rawLeaderSlot) &&
            rawLeaderSlot >= 0f &&
            rawLeaderSlot <= byte.MaxValue &&
            rawLeaderSlot == MathF.Truncate(rawLeaderSlot);
        if (!validLeaderSlot ||
            !context.TryFindNpcPeer(checked((byte)rawLeaderSlot), out NpcSnapshot leader) ||
            !NpcTypeId.TryCreate(leader.Type, out NpcTypeId leaderType) ||
            !VanillaNpcDefinitionCatalog.TryGet(
                leaderType,
                leader.NetIdentity,
                out VanillaNpcDefinition leaderDefinition) ||
            leaderDefinition.AiStyle != VanillaNpcAiStyles.Worm)
        {
            if (IsEaterOfWorlds(definition.Type))
                return inner.TryStepState(in npc, out next);

            next = Terminal(in npc);
            return true;
        }

        if (!definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            !leaderDefinition.TryResolveHitbox(
                leader.Simulation.Scale,
                out VanillaNpcHitboxSize leaderHitbox))
        {
            next = default;
            return false;
        }

        var input = new VanillaWormSegmentFollowInput(
            npc.PositionX,
            npc.PositionY,
            hitbox.Width,
            hitbox.Height,
            leader.PositionX + leaderHitbox.Width * 0.5f,
            leader.PositionY + leaderHitbox.Height * 0.5f,
            worm.Motion.SegmentGap);
        if (!VanillaWormMotion.TryFollowSegment(in input, out VanillaWormSegmentFollowResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            result.PositionX,
            result.PositionY,
            result.VelocityX,
            result.VelocityY,
            npc.Target,
            npc.Ai,
            npc.Simulation with
            {
                DirectionX = result.DirectionX,
                SpriteDirection = result.DirectionX,
                NoGravity = true,
                NoTileCollide = true
            });
        return true;
    }

    private static bool IsEaterOfWorlds(NpcTypeId type) =>
        type == VanillaNpcIds.EaterOfWorldsHead ||
        type == VanillaNpcIds.EaterOfWorldsBody ||
        type == VanillaNpcIds.EaterOfWorldsTail;

    private static NpcStateUpdate Terminal(in NpcSnapshot npc) =>
        new(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            0f,
            0f,
            npc.Target,
            npc.Ai,
            npc.Simulation with
            {
                Life = 0,
                TimeLeft = 0,
                NoGravity = true,
                NoTileCollide = true
            });
}
