using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
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
                float targetX = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f;
                float targetY = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
                hasLineOfSight = _environment.CanHit(
                    staged.PositionX,
                    staged.PositionY,
                    hitbox.Width,
                    hitbox.Height,
                    targetX,
                    targetY,
                    (int)VanillaPlayerHitboxFacts.BaseWidth,
                    (int)VanillaPlayerHitboxFacts.BaseHeight);
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
            currentTarget.CenterY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f ==
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
            SpriteDirection = simulation.SpriteDirection,
            ScaleAdjustsMaximumHorizontalSpeed = parameters.ScaleAdjustsMaximumHorizontalSpeed
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

/// <summary>State portion of Pumpkin Moon AI_026 for types 315 and 329.</summary>
internal sealed class VanillaMoonEventUnicornNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const int StuckThreshold = 30;
    private const int MaximumStuckCounter = StuckThreshold * 10;

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        bool mourningWood = definition.Type == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi26MourningWood;
        bool unicorn = definition.Type == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi26;
        if ((!mourningWood && !unicorn) || definition.AiStyle.Value != 26)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        int directionX = simulation.DirectionX;
        int directionY = simulation.DirectionY;
        int spriteDirection = simulation.SpriteDirection;
        ushort target = npc.Target;
        float ai0 = npc.Ai.Ai0;
        float ai3 = npc.Ai.Ai3;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        NpcAiState localAi = simulation.LocalAi;

        // AI_026 increments Mourning Wood's server-owned localAI timer before the common stuck/target logic.
        if (mourningWood)
            localAi = localAi with { Ai0 = localAi.Ai0 >= 480f ? 0f : localAi.Ai0 + 1f };

        bool reversingOnGround = velocityY == 0f &&
            ((velocityX > 0f && directionX < 0) || (velocityX < 0f && directionX > 0));
        if (reversingOnGround)
            ai3++;

        bool stuck = false;
        if ((npc.PositionX == simulation.OldPositionX || ai3 >= StuckThreshold) | reversingOnGround)
        {
            ai3++;
            stuck = true;
        }
        else if (ai3 > 0f)
        {
            ai3--;
        }

        if (ai3 > MaximumStuckCounter)
            ai3 = 0f;
        if (simulation.JustHit)
            ai3 = 0f;

        if (!TryResolveCurrentTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
        {
            next = default;
            return false;
        }

        float centerX = npc.PositionX + definition.Width * .5f;
        float centerY = npc.PositionY + definition.Height * .5f;
        float targetTop = player.CenterY - player.Height * .5f;
        float dx = player.CenterX - centerX;
        float dy = targetTop - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 200f && !stuck)
            ai3 = 0f;

        if (unicorn && velocityY == 0f && distance < 100f && MathF.Abs(velocityX) > 3f &&
            ((centerX < player.CenterX && velocityX > 0f) || (centerX > player.CenterX && velocityX < 0f)))
        {
            velocityY -= 4f;
        }

        int timeLeft = simulation.TimeLeft;
        if (ai3 < StuckThreshold)
        {
            if (!context.PumpkinMoonActive)
                timeLeft = 10;
            else
                RefreshTarget(in npc, in definition, context, ref target, ref directionX, ref directionY);
        }
        else
        {
            if (velocityX == 0f && velocityY == 0f)
            {
                ai0++;
                if (ai0 >= 2f)
                {
                    directionX *= -1;
                    spriteDirection = directionX;
                    ai0 = 0f;
                }
            }
            else
            {
                ai0 = 0f;
            }

            directionY = -1;
            if (directionX == 0)
                directionX = 1;
        }

        if (mourningWood)
        {
            if ((velocityX > 6f || velocityX < -6f) && velocityY == 0f)
                velocityX *= .8f;
            else if (velocityX < 6f && directionX == 1)
                velocityX = MathF.Min(6f, velocityX + .07f);
            else if (velocityX > -6f && directionX == -1)
                velocityX = MathF.Max(-6f, velocityX - .07f);
        }
        else if (velocityY == 0f || simulation.Wet ||
                 (velocityX <= 0f && directionX < 0) || (velocityX >= 0f && directionX > 0))
        {
            if (velocityX > 0f && directionX < 0)
                velocityX *= .9f;
            if (velocityX < 0f && directionX > 0)
                velocityX *= .9f;
            if (directionX > 0 && velocityX < 3f)
                velocityX += .1f;
            if (directionX < 0 && velocityX > -3f)
                velocityX -= .1f;
        }

        next = new NpcStateUpdate(
            definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY, target,
            new NpcAiState(ai0, npc.Ai.Ai1, npc.Ai.Ai2, ai3), simulation with
            {
                DirectionX = directionX,
                DirectionY = directionY,
                SpriteDirection = spriteDirection,
                NoGravity = false,
                LocalAi = localAi,
                JustHit = false,
                TimeLeft = timeLeft
            });
        return true;
    }

    private static bool TryResolveCurrentTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, ref ushort target, out VanillaNpcTargetCandidate player)
    {
        if (target < byte.MaxValue && context.TryFindCandidate((byte)target, out player) &&
            player.Active && !player.Dead && !player.Ghost)
        {
            return true;
        }

        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) &&
            closest.HasTarget && context.TryFindCandidate((byte)closest.Target, out player))
        {
            target = closest.Target;
            return true;
        }

        player = default;
        return false;
    }

    private static void RefreshTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, ref ushort target, ref int directionX, ref int directionY)
    {
        if (!context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) || !closest.HasTarget)
            return;
        target = closest.Target;
        directionX = closest.DirectionX;
        directionY = closest.DirectionY;
    }
}

/// <summary>Source AI_022 movement state for Pumpkin Moon type 330.</summary>
internal sealed class VanillaMoonEventGhostNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type != VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi22 || definition.AiStyle.Value != 22)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        ushort target = npc.Target;
        int directionX = simulation.DirectionX;
        int directionY = simulation.DirectionY;
        float ai0 = npc.Ai.Ai0;
        float ai1 = npc.Ai.Ai1;
        float ai2 = simulation.JustHit ? 0f : npc.Ai.Ai2;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;

        if (!TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
        {
            next = default;
            return false;
        }

        if (ai2 >= 0f)
        {
            bool sameX = npc.PositionX > ai0 - 16f && npc.PositionX < ai0 + 16f ||
                         (velocityX < 0f && directionX > 0) || (velocityX > 0f && directionX < 0);
            bool sameY = npc.PositionY > ai1 - 40f && npc.PositionY < ai1 + 40f;
            if (sameX & sameY)
            {
                ai2++;
                if (ai2 >= 60f)
                {
                    ai2 = -200f;
                    directionX *= -1;
                    velocityX *= -1f;
                }
            }
            else
            {
                ai0 = npc.PositionX;
                ai1 = npc.PositionY;
                ai2 = 0f;
            }
            Refresh(in npc, in definition, context, ref target, ref directionX, ref directionY);
        }
        else
        {
            ai2 += .1f;
            directionX = player.CenterX > npc.PositionX + definition.Width * .5f ? -1 : 1;
        }

        bool eventEnded = !context.PumpkinMoonActive;
        bool descend = npc.PositionY + definition.Height <= player.CenterY - player.Height * .5f;
        if (descend)
            velocityY = Math.Min(3f, velocityY + .1f);
        else
        {
            if (directionY < 0 && velocityY > 0f)
                velocityY -= .1f;
            velocityY = Math.Max(-4f, velocityY);
        }

        int timeLeft = simulation.TimeLeft;
        if (!eventEnded)
            Refresh(in npc, in definition, context, ref target, ref directionX, ref directionY);
        else
            timeLeft = 10;

        if (directionX < 0 && velocityX > 0f)
            velocityX *= .9f;
        if (directionX > 0 && velocityX < 0f)
            velocityX *= .9f;
        if (directionX == -1 && velocityX > -4f)
        {
            velocityX -= .1f;
            if (velocityX > 4f) velocityX -= .1f;
            else if (velocityX > 0f) velocityX += .05f;
            if (velocityX < -4f) velocityX = -4f;
        }
        else if (directionX == 1 && velocityX < 4f)
        {
            velocityX += .1f;
            if (velocityX < -4f) velocityX += .1f;
            else if (velocityX < 0f) velocityX -= .05f;
            if (velocityX > 4f) velocityX = 4f;
        }
        if (directionY == -1 && velocityY > -1.5f)
        {
            velocityY -= .04f;
            if (velocityY > 1.5f) velocityY -= .05f;
            else if (velocityY > 0f) velocityY += .03f;
            if (velocityY < -1.5f) velocityY = -1.5f;
        }
        else if (directionY == 1 && velocityY < 1.5f)
        {
            velocityY += .04f;
            if (velocityY < -1.5f) velocityY += .05f;
            else if (velocityY < 0f) velocityY -= .03f;
            if (velocityY > 1.5f) velocityY = 1.5f;
        }

        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY,
            target, new NpcAiState(ai0, ai1, ai2, npc.Ai.Ai3), simulation with
            {
                NoGravity = true,
                NoTileCollide = true,
                Alpha = 0,
                DirectionX = directionX,
                DirectionY = directionY,
                JustHit = false,
                TimeLeft = timeLeft
            });
        return true;
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        ref ushort target, out VanillaNpcTargetCandidate player)
    {
        if (target < byte.MaxValue && context.TryFindCandidate((byte)target, out player) && player.Active && !player.Dead && !player.Ghost)
            return true;
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            context.TryFindCandidate((byte)closest.Target, out player))
        {
            target = closest.Target;
            return true;
        }
        player = default;
        return false;
    }

    private static void Refresh(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        ref ushort target, ref int directionX, ref int directionY)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget)
        {
            target = closest.Target;
            directionX = closest.DirectionX;
            directionY = closest.DirectionY;
        }
    }
}

/// <summary>TerrariaServer 1.4.5.8 AI_057 state and hover motion for Snow Moon Everscream.</summary>
internal sealed class VanillaMoonEventEverscreamNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random;
    private IVanillaEverscreamEnvironment? environment;

    public VanillaMoonEventEverscreamNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetEnvironment(IVanillaEverscreamEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if ((definition.Type != VanillaMoonEventSpecialCatalog1458.SnowMoonAi57Everscream &&
             definition.Type != VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi57MourningWood) ||
            definition.AiStyle.Value != 57 || environment is null ||
            !TryTarget(in npc, in definition, context, out ushort target, out VanillaNpcTargetCandidate player))
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        float ai0 = npc.Ai.Ai0;
        float ai1 = npc.Ai.Ai1;
        float life = simulation.Life > 0 ? simulation.Life : definition.LifeMax;
        float speed = life < definition.LifeMax * .5f ? 4f : life < definition.LifeMax * .75f ? 3f : 2f;
        int timeLeft = simulation.TimeLeft;
        if (context.DayTime)
        {
            timeLeft = 10;
            speed = 8f;
        }

        if (!context.DayTime && ai0 == 0f)
        {
            ai1++;
            if (life < definition.LifeMax * .5f) ai1++;
            if (life < definition.LifeMax * .25f) ai1++;
            if (ai1 >= 300f)
            {
                ai1 = 0f;
                ai0 = life < definition.LifeMax * .25f &&
                    definition.Type == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi57MourningWood
                    ? random.NextInt32(3, 5) : random.NextInt32(1, 3);
            }
        }
        else if (ai0 == 1f)
        {
            ai1++;
            if (ai1 >= (definition.Type == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi57MourningWood ? 120f : 180f))
            { ai0 = 0f; ai1 = 0f; }
        }
        else if (ai0 == 2f)
        {
            ai1++;
            if (ai1 >= 300f) { ai0 = 0f; ai1 = 0f; }
        }
        else if (ai0 == 3f)
        {
            ai1++;
            if (ai1 >= 120f) { ai0 = 0f; ai1 = 0f; }
            speed = 4f;
        }
        else if (ai0 == 4f)
        {
            ai1++;
            if (ai1 >= 240f) { ai0 = 0f; ai1 = 0f; }
            speed = 4f;
        }

        float centerX = npc.PositionX + definition.Width * .5f;
        bool stopHorizontal = MathF.Abs(centerX - player.CenterX) < 50f;
        float velocityX = npc.VelocityX;
        if (stopHorizontal)
            velocityX *= .9f;
        else if (centerX < player.CenterX)
            velocityX = (velocityX * 20f + speed) / 21f;
        else
            velocityX = (velocityX * 20f - speed) / 21f;

        float playerLeft = player.CenterX - player.Width * .5f;
        float playerTop = player.CenterY - player.Height * .5f;
        bool overlapsFromAbove = npc.PositionX < playerLeft && npc.PositionX + definition.Width > playerLeft &&
            npc.PositionY + definition.Height < playerTop + player.Height - 16f;
        bool hasGroundBelow = environment.SolidCollision(centerX - 40f, npc.PositionY + definition.Height - 20f, 80, 20);
        float velocityY = ResolveVerticalVelocity(npc.VelocityY, overlapsFromAbove, hasGroundBelow);

        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY,
            target, new NpcAiState(ai0, ai1, npc.Ai.Ai2, npc.Ai.Ai3), simulation with
            {
                NoGravity = true, NoTileCollide = true, DirectionX = velocityX < 0f ? -1 : 1,
                SpriteDirection = velocityX < 0f ? -1 : 1, JustHit = false, TimeLeft = timeLeft
            });
        return true;
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            context.TryFindCandidate((byte)closest.Target, out player) && player.Active && !player.Dead && !player.Ghost)
        {
            target = closest.Target;
            return true;
        }
        target = VanillaNpcDefinitionCatalog.DefaultTarget;
        player = default;
        return false;
    }

    private static float ResolveVerticalVelocity(float velocityY, bool overlapsFromAbove, bool hasGroundBelow)
    {
        if (overlapsFromAbove) return MathF.Min(10f, velocityY + .5f);
        if (hasGroundBelow)
        {
            if (velocityY > 0f) velocityY = 0f;
            return MathF.Max(-4f, velocityY > -.2f ? velocityY - .025f : velocityY - .2f);
        }
        if (velocityY < 0f) velocityY = 0f;
        return MathF.Min(10f, velocityY < .1f ? velocityY + .025f : velocityY + .5f);
    }
}

internal sealed class VanillaMoonEventJumpingFighterNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type != VanillaMoonEventSpecialCatalog1458.SnowMoonAi25 || definition.AiStyle.Value != 25)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        ushort target = npc.Target;
        int directionX = simulation.DirectionX;
        int directionY = simulation.DirectionY;
        int spriteDirection = simulation.SpriteDirection;
        float ai0 = npc.Ai.Ai0;
        float ai1 = npc.Ai.Ai1;
        float ai2 = npc.Ai.Ai2;

        // Type 341 forces ai[3] to one, bypassing the depth classification of the shared AI_025 body.
        if (ai0 == 0f)
        {
            RefreshTarget(in npc, in definition, context, ref target, ref directionX, ref directionY);
            if (npc.VelocityX != 0f || npc.VelocityY < 0f || npc.VelocityY > .3f)
                ai0 = 1f;
            else if (target < byte.MaxValue && context.TryFindCandidate((byte)target, out VanillaNpcTargetCandidate player) &&
                     player.Active && !player.Dead && !player.Ghost &&
                     IntersectsActivationRectangle(in npc, in definition, in player))
                ai0 = 1f;
        }
        else if (npc.VelocityY == 0f)
        {
            ai2++;
            int wait = ai1 == 0f ? 12 : 20;
            if (ai2 >= wait)
            {
                ai2 = 0f;
                RefreshTarget(in npc, in definition, context, ref target, ref directionX, ref directionY);
                if (directionX == 0)
                    directionX = -1;
                spriteDirection = directionX;
                ai1++;
                if (ai1 == 2f)
                {
                    ai1 = 0f;
                    next = Build(in npc, in definition, target, directionX, directionY, spriteDirection, ai0, ai1, ai2, directionX * 2.5f, -8f);
                    return true;
                }
                next = Build(in npc, in definition, target, directionX, directionY, spriteDirection, ai0, ai1, ai2, directionX * 3.5f, -4f);
                return true;
            }
            next = Build(in npc, in definition, target, directionX, directionY, spriteDirection, ai0, ai1, ai2, npc.VelocityX * .9f, npc.VelocityY);
            return true;
        }
        else if (directionX == 1 && npc.VelocityX < 1f)
        {
            next = Build(in npc, in definition, target, directionX, directionY, spriteDirection, ai0, ai1, ai2, npc.VelocityX + .1f, npc.VelocityY);
            return true;
        }
        else if (directionX == -1 && npc.VelocityX > -1f)
        {
            next = Build(in npc, in definition, target, directionX, directionY, spriteDirection, ai0, ai1, ai2, npc.VelocityX - .1f, npc.VelocityY);
            return true;
        }

        next = Build(in npc, in definition, target, directionX, directionY, spriteDirection, ai0, ai1, ai2, npc.VelocityX, npc.VelocityY);
        return true;
    }

    private static void RefreshTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, ref ushort target, ref int directionX, ref int directionY)
    {
        if (!context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) || !closest.HasTarget)
            return;
        target = closest.Target;
        directionX = closest.DirectionX;
        directionY = closest.DirectionY;
    }

    private static NpcStateUpdate Build(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        ushort target, int directionX, int directionY, int spriteDirection, float ai0, float ai1, float ai2,
        float velocityX, float velocityY)
    {
        NpcSimulationState simulation = npc.Simulation;
        return new NpcStateUpdate(
            definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY, target,
            new NpcAiState(ai0, ai1, ai2, 1f), simulation with
            {
                DirectionX = directionX,
                DirectionY = directionY,
                SpriteDirection = spriteDirection,
                NoGravity = false,
                JustHit = false
            });
    }

    private static bool IntersectsActivationRectangle(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate player)
    {
        // NPC.AI_025 uses integer Rectangle.Intersects with an NPC-sized rectangle expanded by 100 pixels.
        int npcLeft = (int)npc.PositionX - 100;
        int npcTop = (int)npc.PositionY - 100;
        int npcRight = npcLeft + definition.Width + 200;
        int npcBottom = npcTop + definition.Height + 200;
        int playerLeft = (int)(player.CenterX - player.Width * .5f);
        int playerTop = (int)(player.CenterY - player.Height * .5f);
        int playerRight = playerLeft + (int)player.Width;
        int playerBottom = playerTop + (int)player.Height;
        return npcLeft < playerRight && npcRight > playerLeft && npcTop < playerBottom && npcBottom > playerTop;
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
            TargetTopY: candidate.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f,
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
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
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

        if (definition.Type == VanillaNpcIds.Probe && npc.Ai.Ai3 != 0f)
        {
            NpcSnapshot attachedProbe = npc;
            if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh attachedClosest))
            {
                attachedProbe = npc with
                {
                    Target = attachedClosest.Target,
                    Simulation = npc.Simulation with
                    {
                        DirectionX = attachedClosest.DirectionX,
                        DirectionY = attachedClosest.DirectionY
                    }
                };
            }

            if (TryStepMechdusaProbe(in attachedProbe, in definition, in hitbox, context, out next))
                return true;
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
            TargetTopY: candidate.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f,
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
                IsMechQueenUp(context),
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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox) ||
            proposed.Target >= byte.MaxValue ||
            !context.TryFindCandidate(checked((byte)proposed.Target), out VanillaNpcTargetCandidate target))
        {
            return 0;
        }

        NpcSnapshot attackSource = source with { Ai = proposed.Ai };
        if (!VanillaFlyerProjectileAttack.TryStep(
                type,
                in attackSource,
                in hitbox,
                in target,
                proposed.VelocityX,
                proposed.VelocityY,
                projectileEnvironment,
                IsMechQueenUp(context),
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
            bool mechdusaProbe = proposed.Ai.Ai3 != 0f && IsMechQueenUp(context);
            float velocityX = target.CenterX - sourceCenterX;
            float velocityY = target.CenterY - sourceCenterY;
            if (mechdusaProbe)
            {
                // AI_005 snapshots and snaps `vector` before moving the attached Probe, then fires from that
                // old vector while aiming from the new physical center. Keep those two source positions distinct.
                sourceCenterX = (int)(sourceCenterX / 8f) * 8f;
                sourceCenterY = (int)(sourceCenterY / 8f) * 8f;
                float attachedCenterX = proposed.PositionX + hitbox.Width * .5f;
                float attachedCenterY = proposed.PositionY + hitbox.Height * .5f;
                velocityX = target.CenterX - attachedCenterX - target.VelocityX * 20f;
                velocityY = target.CenterY - attachedCenterY - target.VelocityY * 20f;
                VanillaFlyerProjectileAttack.Normalize(ref velocityX, ref velocityY, 8f);
            }
            else
            {
                if (!VanillaFlyerNpcCatalog.TryGetMotionProfile(type, out VanillaFlyerMotionProfile profile))
                    return 0;
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

        float targetPositionY = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
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

    private static bool IsMechQueenUp(VanillaNpcBehaviorContext context) =>
        TryGetMechQueen(context, out _);

    private static bool TryGetMechQueen(VanillaNpcBehaviorContext context, out NpcSnapshot mechQueen)
    {
        Span<NpcSnapshot> primes = stackalloc NpcSnapshot[VanillaNpcSpawnRules.PhysicalSlotCount];
        int count = context.CopyNpcPeers(VanillaNpcIds.SkeletronPrime, primes);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot prime = primes[index];
            if (prime.Ai.Ai3 == prime.Handle.Slot)
            {
                mechQueen = prime;
                return true;
            }
        }

        mechQueen = default;
        return false;
    }

    private bool TryStepMechdusaProbe(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcHitboxSize hitbox,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        bool mechQueenUp = TryGetMechQueen(context, out NpcSnapshot mechQueen);
        if (!mechQueenUp || !float.IsFinite(npc.Ai.Ai2))
            return RecoverMechdusaProbe(in npc, in definition, mechQueenUp, out next);

        NpcAiState ai = npc.Ai;
        NpcSnapshot destroyer;
        int destroyerSlot = (int)ai.Ai2;
        if (destroyerSlot < 0 || destroyerSlot >= VanillaNpcSpawnRules.PhysicalSlotCount)
        {
            // NPC.AI_005 retries FindFirstNPC(134) only when the stored slot lies outside
            // Main.maxNPCs. An in-range reused slot is a distinct source recovery branch.
            if (!TryFindFirstDestroyer(context, out destroyer))
                return RecoverMechdusaProbe(in npc, in definition, mechQueenUp, out next);
            ai = ai with { Ai2 = destroyer.Handle.Slot };
        }
        else if (!context.TryFindNpcPeer((byte)destroyerSlot, out destroyer) ||
                 destroyer.TypeIdentity != VanillaNpcIds.Destroyer)
        {
            return RecoverMechdusaProbe(in npc, in definition, mechQueenUp, out next);
        }

        if (!VanillaNpcDefinitionCatalog.TryGet(destroyer.TypeIdentity, destroyer.NetIdentity, out VanillaNpcDefinition destroyerDefinition) ||
            !destroyerDefinition.TryResolveHitbox(destroyer.Simulation, out VanillaNpcHitboxSize destroyerHitbox))
        {
            return RecoverMechdusaProbe(in npc, in definition, mechQueenUp, out next);
        }

        float angle = destroyer.Simulation.Rotation ?? 0f;
        float offsetX = MathF.Cos(angle) * (26f * npc.Ai.Ai3);
        float offsetY = MathF.Sin(angle) * (26f * npc.Ai.Ai3);
        float centerX = destroyer.PositionX + destroyerHitbox.Width * .5f + offsetX;
        float centerY = destroyer.PositionY + destroyerHitbox.Height * .5f + offsetY;
        NpcAiState local = npc.Simulation.LocalAi;
        if (npc.Simulation.JustHit)
            local = local with { Ai0 = 0f };
        else
            local = local with { Ai0 = local.Ai0 + 3f };
        if (local.Ai0 >= 360f)
            local = local with { Ai0 = 0f };

        next = new NpcStateUpdate(
            definition.Type.Value, npc.NetId,
            centerX - hitbox.Width * .5f, centerY - hitbox.Height * .5f,
            mechQueen.VelocityX, mechQueen.VelocityY, npc.Target, ai,
            npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn,
                DontTakeDamage = true,
                LocalAi = local,
                Rotation = angle
            });
        return true;
    }

    private static bool TryFindFirstDestroyer(VanillaNpcBehaviorContext context, out NpcSnapshot destroyer)
    {
        Span<NpcSnapshot> candidates = stackalloc NpcSnapshot[VanillaNpcSpawnRules.PhysicalSlotCount];
        int count = context.CopyNpcPeers(VanillaNpcIds.Destroyer, candidates);
        if (count > 0)
        {
            destroyer = candidates[0];
            return true;
        }

        destroyer = default;
        return false;
    }

    private static bool RecoverMechdusaProbe(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        bool mechQueenUp,
        out NpcStateUpdate next)
    {
        float localAi0 = npc.Simulation.JustHit ? 0f : npc.Simulation.LocalAi.Ai0 + 1f;
        float threshold = mechQueenUp ? 360f : VanillaFlyerProjectileAttack.ProbeAttackThreshold;
        if (localAi0 >= threshold)
            localAi0 = 0f;
        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            npc.Target,
            npc.Ai with { Ai3 = 0f },
            npc.Simulation with
            {
                DontTakeDamage = false,
                LocalAi = npc.Simulation.LocalAi with { Ai0 = localAi0 }
            });
        return true;
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

        if (!definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox) ||
            !leaderDefinition.TryResolveHitbox(
                leader.Simulation,
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
            !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox) ||
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
