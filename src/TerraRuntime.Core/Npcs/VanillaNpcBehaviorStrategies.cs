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
    private IVanillaFlyingEyeEnvironment? _environment;

    public void SetEnvironment(IVanillaFlyingEyeEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

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

        if (_environment is null ||
            !NpcTypeId.TryCreate(npc.Type, out NpcTypeId type) ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return TryLegacyTargetRefresh(in npc, in definition, context, inner, out next);
        }

        NpcSnapshot staged = npc;
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = TryGetCurrentTarget(in npc, context, out target);
        bool pigron = VanillaFlyingEyeNpcCatalog.IsPigron(type);

        // Pigrons call TargetClosest before their LOS/phasing branch. Other style-2 eyes evaluate
        // daylight discouragement against the current target and only call TargetClosest when not discouraged.
        if ((pigron || !hasTarget) &&
            TryRefreshClosest(in staged, in definition, context, out NpcSnapshot refreshed, out target))
        {
            staged = refreshed;
            hasTarget = true;
        }

        bool targetInGraveyard = hasTarget && _environment.IsGraveyardAt(target.CenterX, target.CenterY);
        bool hasLineOfSight = false;
        bool solidCollision = false;
        if (pigron)
        {
            if (hasTarget)
            {
                float targetX = target.CenterX - VanillaNpcBehaviorContext.BasePlayerWidth * 0.5f;
                float targetY = target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
                hasLineOfSight = _environment.CanHit(
                    staged.PositionX,
                    staged.PositionY,
                    hitbox.Width,
                    hitbox.Height,
                    targetX,
                    targetY,
                    (int)VanillaNpcBehaviorContext.BasePlayerWidth,
                    (int)VanillaNpcBehaviorContext.BasePlayerHeight);
            }

            solidCollision = _environment.SolidCollision(
                staged.PositionX,
                staged.PositionY,
                hitbox.Width,
                hitbox.Height);
        }

        var lifecycleInput = new VanillaFlyingEyeLifecycleInput(
            PositionY: staged.PositionY,
            VelocityY: staged.VelocityY,
            Ai: staged.Ai,
            TimeLeft: staged.Simulation.TimeLeft,
            NoTileCollide: staged.Simulation.NoTileCollide,
            DayTime: context.DayTime,
            WorldSurfacePixels: context.WorldSurfacePixels,
            TargetInGraveyard: targetInGraveyard,
            HasLineOfSight: hasLineOfSight,
            SolidCollision: solidCollision);
        if (!VanillaFlyingEyeLifecycle.TryStep(type, in lifecycleInput, out VanillaFlyingEyeLifecycleResult lifecycle))
        {
            next = default;
            return false;
        }

        if (!pigron && !lifecycle.Discouraged &&
            TryRefreshClosest(in staged, in definition, context, out NpcSnapshot retargeted, out _))
        {
            staged = retargeted;
        }
        else if (lifecycle.Discouraged)
        {
            staged = staged with
            {
                Simulation = staged.Simulation with
                {
                    DirectionX = staged.VelocityY > 0f ? 1 : staged.Simulation.DirectionX,
                    DirectionY = -1,
                    TimeLeft = lifecycle.TimeLeft
                }
            };
        }

        // Preserve pre-transition NoTileCollide through collision response. The source applies generic
        // collideX/collideY rebound before it updates the Pigron phase flag for subsequent movement.
        staged = staged with { Ai = lifecycle.Ai };
        if (!inner.TryStepState(in staged, out next))
            return false;

        next = next with
        {
            Ai = lifecycle.Ai,
            Simulation = next.Simulation with
            {
                NoTileCollide = lifecycle.NoTileCollide,
                TimeLeft = lifecycle.TimeLeft
            }
        };
        return true;
    }

    private static bool TryLegacyTargetRefresh(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        NpcSnapshot targeted = npc;
        if (TryRefreshClosest(in npc, in definition, context, out NpcSnapshot refreshed, out _))
            targeted = refreshed;
        return inner.TryStepState(in targeted, out next);
    }

    private static bool TryGetCurrentTarget(
        in NpcSnapshot npc,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate candidate)
    {
        if (npc.Target < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)npc.Target), out candidate) &&
            candidate.Active && !candidate.Dead && !candidate.Ghost)
        {
            return true;
        }

        candidate = default;
        return false;
    }

    private static bool TryRefreshClosest(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out NpcSnapshot refreshed,
        out VanillaNpcTargetCandidate candidate)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) &&
            context.TryFindCandidate(checked((byte)closest.Target), out candidate))
        {
            refreshed = npc with
            {
                Target = closest.Target,
                Simulation = npc.Simulation with
                {
                    DirectionX = closest.DirectionX,
                    DirectionY = closest.DirectionY
                }
            };
            return true;
        }

        refreshed = npc;
        candidate = default;
        return false;
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
            TargetTopY: candidate.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f,
            ExpertMode: context.ExpertMode,
            GoodWorld: context.GoodWorld);

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
    private const int ProbeClassicDamage = 25;
    private const int ProbeExpertDamage = 22;
    private const int BloodSquidDamage = 35;
    private const float BloodSquidKnockBack = 1f;

    private readonly IVanillaNpcRandom random;
    private IVanillaNpcProjectileEnvironment? projectileEnvironment;

    public VanillaServantOfCthulhuNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment) =>
        projectileEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));

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

        var input = new VanillaFlyerAiMotionInput(
            PositionY: npc.PositionY,
            NpcCenterX: npc.PositionX + hitbox.Width * 0.5f,
            NpcCenterY: npc.PositionY + hitbox.Height * 0.5f,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            TargetCenterX: candidate.CenterX,
            TargetCenterY: candidate.CenterY,
            TargetTopY: candidate.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f,
            OldVelocityX: npc.Simulation.OldVelocityX,
            OldVelocityY: npc.Simulation.OldVelocityY,
            DirectionX: closest.DirectionX,
            Ai: npc.Ai,
            Scale: npc.Simulation.Scale,
            CollideX: npc.Simulation.CollideX,
            CollideY: npc.Simulation.CollideY,
            Wet: npc.Simulation.Wet,
            DayTime: context.DayTime,
            ExpertMode: context.ExpertMode,
            WorldSurfacePixels: context.WorldSurfacePixels,
            TimeLeft: npc.Simulation.TimeLeft);
        if (!VanillaFlyerAiMotion.TryStep(
                definition.Type,
                in input,
                in profile,
                out VanillaFlyerAiMotionResult result))
        {
            next = default;
            return false;
        }

        float finalVelocityX = result.VelocityX;
        float finalVelocityY = result.VelocityY;
        NpcAiState localAi = npc.Simulation.LocalAi;
        if (VanillaFlyerProjectileAttack.IsSupportedShooter(definition.Type) &&
            VanillaFlyerProjectileAttack.TryStep(
                definition.Type,
                in npc,
                in hitbox,
                in candidate,
                result.VelocityX,
                result.VelocityY,
                projectileEnvironment,
                out VanillaFlyerProjectileAttackResult attack))
        {
            finalVelocityX = attack.VelocityX;
            finalVelocityY = attack.VelocityY;
            localAi = attack.LocalAi;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            finalVelocityX,
            finalVelocityY,
            closest.Target,
            result.Ai,
            npc.Simulation with
            {
                DirectionX = closest.DirectionX,
                DirectionY = closest.DirectionY,
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn,
                TimeLeft = result.TimeLeft,
                LocalAi = localAi
            });
        return true;
    }

    public int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        VanillaNpcBehaviorContext context,
        Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty ||
            proposed.Type != source.Type ||
            !NpcTypeId.TryCreate(source.Type, out NpcTypeId type) ||
            !VanillaFlyerProjectileAttack.IsSupportedShooter(type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, source.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            proposed.Target >= byte.MaxValue ||
            !context.TryFindCandidate(checked((byte)proposed.Target), out VanillaNpcTargetCandidate target))
        {
            return 0;
        }

        if (!VanillaFlyerProjectileAttack.TryStep(
                type,
                in source,
                in hitbox,
                in target,
                proposed.VelocityX,
                proposed.VelocityY,
                projectileEnvironment,
                out VanillaFlyerProjectileAttackResult attack) ||
            !attack.ProjectileReady ||
            !attack.LocalAi.Equals(proposed.Simulation.LocalAi))
        {
            return 0;
        }

        float sourceCenterX = source.PositionX + hitbox.Width * 0.5f;
        float sourceCenterY = source.PositionY + hitbox.Height * 0.5f;
        if (type == VanillaNpcIds.Probe)
        {
            if (!VanillaFlyerNpcCatalog.TryGetMotionProfile(type, out VanillaFlyerMotionProfile profile))
                return 0;

            float velocityX = target.CenterX - sourceCenterX;
            float velocityY = target.CenterY - sourceCenterY;
            float distanceSquared = velocityX * velocityX + velocityY * velocityY;
            if (distanceSquared < profile.MaximumSpeed * profile.MaximumSpeed)
            {
                velocityX = source.VelocityX;
                velocityY = source.VelocityY;
            }
            else
            {
                VanillaFlyerProjectileAttack.Normalize(ref velocityX, ref velocityY, profile.MaximumSpeed);
            }

            destination[0] = new NpcAiProjectileIntent(
                VanillaProjectileIds.ProbePinkLaser,
                sourceCenterX,
                sourceCenterY,
                velocityX,
                velocityY,
                context.ExpertMode ? ProbeExpertDamage : ProbeClassicDamage,
                KnockBack: 0f);
            return 1;
        }

        float targetPositionY = target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
        int offsetX = random.NextInt32(-100, 101);
        int offsetY = random.NextInt32(-100, 101);
        float shotX = target.CenterX + offsetX - sourceCenterX;
        float shotY = targetPositionY + offsetY - sourceCenterY;
        VanillaFlyerProjectileAttack.Normalize(
            ref shotX,
            ref shotY,
            VanillaFlyerProjectileAttack.BloodSquidProjectileSpeed);
        destination[0] = new NpcAiProjectileIntent(
            VanillaProjectileIds.BloodShot,
            sourceCenterX,
            sourceCenterY,
            shotX,
            shotY,
            BloodSquidDamage,
            BloodSquidKnockBack);
        return 1;
    }
}

internal sealed class VanillaWormNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaWormEnvironment? environment;

    public void SetEnvironment(IVanillaWormEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

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

        if (IsEaterOfWorlds(definition.Type) &&
            TryStepEaterOfWorldsLinks(in npc, in worm, context, out next))
        {
            return true;
        }

        if (worm.Role == VanillaWormSegmentRole.Head)
            return TryStepHead(in npc, in definition, in worm, context, inner, out next);

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

    private static bool TryStepEaterOfWorldsLinks(
        in NpcSnapshot npc,
        in VanillaWormNpcEntry worm,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        WormLinkState predecessor = ResolveWormLink(npc.Ai.Ai1, context);
        WormLinkState successor = ResolveWormLink(npc.Ai.Ai0, context);
        bool predecessorActive = predecessor != WormLinkState.Missing;
        bool successorActive = successor != WormLinkState.Missing;
        bool predecessorCompatible = predecessor == WormLinkState.ActiveWorm;
        bool successorCompatible = successor == WormLinkState.ActiveWorm;

        // Vanilla AI_006 uses raw active-state checks for Eater of Worlds structural death, then
        // separately uses active+aiStyle compatibility when a body decides whether to split into a
        // replacement head/tail. Do not collapse those predicates: a live slot reused by a non-worm NPC
        // keeps an existing head/tail alive but makes an attached body split at that boundary.
        //
        // TerraRuntime materializes vanilla's immediate chain allocation incrementally. A zero successor
        // with a non-negative construction countdown therefore receives its follower after this commit.
        bool awaitingFollower = npc.Ai.Ai0 == 0f && npc.Ai.Ai2 >= 0f;
        if (worm.Role == VanillaWormSegmentRole.Head)
        {
            if (!successorActive && !awaitingFollower)
            {
                next = Terminal(in npc);
                return true;
            }

            next = default;
            return false;
        }

        if (worm.Role == VanillaWormSegmentRole.Tail)
        {
            if (!predecessorActive)
            {
                next = Terminal(in npc);
                return true;
            }

            next = default;
            return false;
        }

        if (!predecessorActive && !successorActive && !awaitingFollower)
        {
            next = Terminal(in npc);
            return true;
        }

        if (!predecessorCompatible)
        {
            next = TransformEaterOfWorldsSegment(
                in npc,
                VanillaNpcIds.EaterOfWorldsHead,
                new NpcAiState(npc.Ai.Ai0, 0f, 0f, 0f));
            return true;
        }

        if (!successorCompatible && !awaitingFollower)
        {
            next = TransformEaterOfWorldsSegment(
                in npc,
                VanillaNpcIds.EaterOfWorldsTail,
                new NpcAiState(0f, npc.Ai.Ai1, 0f, 0f));
            return true;
        }

        next = default;
        return false;
    }

    private static WormLinkState ResolveWormLink(float rawSlot, VanillaNpcBehaviorContext context)
    {
        if (!float.IsFinite(rawSlot) ||
            rawSlot < 0f ||
            rawSlot > byte.MaxValue ||
            rawSlot != MathF.Truncate(rawSlot) ||
            !context.TryFindNpcPeer(checked((byte)rawSlot), out NpcSnapshot peer))
        {
            return WormLinkState.Missing;
        }

        if (!NpcTypeId.TryCreate(peer.Type, out NpcTypeId peerType) ||
            !VanillaNpcDefinitionCatalog.TryGet(
                peerType,
                peer.NetIdentity,
                out VanillaNpcDefinition peerDefinition) ||
            peerDefinition.AiStyle != VanillaNpcAiStyles.Worm)
        {
            return WormLinkState.ActiveOtherAiStyle;
        }

        return WormLinkState.ActiveWorm;
    }

    private enum WormLinkState : byte
    {
        Missing = 0,
        ActiveWorm = 1,
        ActiveOtherAiStyle = 2
    }

    private static NpcStateUpdate TransformEaterOfWorldsSegment(
        in NpcSnapshot npc,
        NpcTypeId type,
        NpcAiState ai) =>
        new(
            type.Value,
            checked((short)type.Value),
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            npc.Target,
            ai,
            npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = true
            });

    private bool TryStepHead(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaWormNpcEntry worm,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (environment is null ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            !context.TrySelectClosestTarget(
                in npc,
                in definition,
                out VanillaBlueSlimeTargetRefresh closest) ||
            !context.TryFindCandidate(
                checked((byte)closest.Target),
                out VanillaNpcTargetCandidate target))
        {
            return inner.TryStepState(in npc, out next);
        }

        var input = new VanillaWormHeadMotionInput(
            npc.PositionX + hitbox.Width * 0.5f,
            npc.PositionY + hitbox.Height * 0.5f,
            npc.VelocityX,
            npc.VelocityY,
            target.CenterX,
            target.CenterY,
            worm.Motion.AlwaysDig || environment.IsDigging(
                npc.PositionX,
                npc.PositionY,
                hitbox.Width,
                hitbox.Height));
        VanillaWormMotionProfile profile = worm.Motion;
        if (!VanillaWormMotion.TryStepHead(in input, in profile, out VanillaWormHeadMotionResult result))
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
                NoTileCollide = true
            });
        return true;
    }

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
