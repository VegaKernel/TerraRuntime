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
    private readonly IVanillaNpcRandom random;

    public VanillaSlimeGroundNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

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
        NpcAiState ai = npc.Ai;
        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        float velocityX = npc.VelocityX;
        // NPC.AI_001 initializes a contained Sand Slime item only once. In a Skyblock world
        // whose generation scan found no Fossil blocks, it has a one-in-five Fossil Slime roll.
        if (definition.Type == VanillaNpcIds.SandSlime && ai.Ai1 == 0f)
        {
            ai = ai with { Ai1 = -1f };
            if (context.SkyblockNoFossils && random.NextInt32(0, 5) == 0)
                ai = ai with { Ai1 = 3347f };
        }
        // Ice and Spiked Ice Slimes use the same one-time contained-item slot. Below the world surface,
        // ordinary worlds roll once at 1/40; Skyblock lowTiles makes five 1/20 attempts, stopping at the
        // first success, then chooses Slush (1103) or Snow (593) with a separate source draw.
        if ((definition.Type == VanillaNpcIds.IceSlime || definition.Type == VanillaNpcIds.SpikedIceSlime) &&
            ai.Ai1 == 0f)
        {
            ai = ai with { Ai1 = -1f };
            if (npc.PositionY > context.WorldSurfacePixels)
            {
                int attempts = context.SkyblockLowTiles ? 5 : 1;
                int chance = context.SkyblockLowTiles ? 20 : 40;
                for (int attempt = 0; attempt < attempts && ai.Ai1 == -1f; attempt++)
                {
                    if (random.NextInt32(0, chance) == 0)
                        ai = ai with { Ai1 = random.NextInt32(0, 2) == 0 ? 1103f : 593f };
                }
            }
        }
        // AI_001's Remix item generator is a separate unimplemented source branch. Outside Remix,
        // a Skyblock world without Hellstone grants each normal Lava Slime initialization attempt
        // a post-Skeletron one-in-fifteen Hellstone roll. lowTiles/slime-rain alter attempt count.
        if (definition.Type == VanillaNpcIds.LavaSlime && ai.Ai1 == 0f)
        {
            ai = ai with { Ai1 = -1f };
            if (!context.RemixWorld && context.SkyblockNoHellstone && context.DownedSkeletron)
            {
                int attempts = 1 + (context.SkyblockLowTiles ? 4 : 0) + (context.SlimeRainActive ? 2 : 0);
                for (int attempt = 0; attempt < attempts && ai.Ai1 == -1f; attempt++)
                {
                    if (random.NextInt32(0, 15) == 0)
                        ai = ai with { Ai1 = 174f };
                }
            }
        }
        // Existing contained-item variants are re-applied by AI_001 on every server tick before
        // the shared slime movement state machine. These effects are independent of the branch
        // that originally selected the item into ai[1].
        if (ai.Ai1 == 2f && npc.VelocityY == 0f)
            ai = ai with { Ai0 = ai.Ai0 + 9f };
        if (ai.Ai1 == 9f)
            simulation = simulation with { DefenseOverride = (simulation.BaseDefense ?? definition.Defense) + 16 };
        if (ai.Ai1 == 147f)
            simulation = simulation with { DamageOverride = (simulation.BaseDamage ?? definition.Damage) * 2 };
        if (ai.Ai1 == 3609f)
        {
            simulation = simulation with
            {
                DefenseOverride = (simulation.BaseDefense ?? definition.Defense) + 8,
                DamageOverride = (simulation.BaseDamage ?? definition.Damage) + 6
            };
        }
        // Heart and Hell Slime compare against NPC.defLifeMax, which is the spawn-time value after
        // SetDefaults/scaling. The runtime retains that baseline independently so their initialization
        // does not repeat after a state-only update.
        int baseLifeMax = simulation.BaseLifeMax ?? definition.LifeMax;
        if (ai.Ai1 == 29f)
        {
            simulation = simulation with { DefenseOverride = (simulation.BaseDefense ?? definition.Defense) + 4 };
            if (simulation.LifeMax == baseLifeMax)
            {
                simulation = simulation with
                {
                    Life = simulation.Life == simulation.LifeMax ? baseLifeMax * 2 : simulation.Life,
                    LifeMax = baseLifeMax * 2
                };
            }
        }
        if (ai.Ai1 == 174f)
        {
            simulation = simulation with
            {
                DefenseOverride = (simulation.BaseDefense ?? definition.Defense) + 14,
                DamageOverride = (simulation.BaseDamage ?? definition.Damage) + 20
            };
            if (simulation.LifeMax == baseLifeMax && definition.TryResolveHitbox(simulation, out VanillaNpcHitboxSize body))
            {
                const float expansion = 1.2f;
                int width = (int)(body.Width * expansion);
                int height = (int)(body.Height * expansion);
                simulation = simulation with
                {
                    KnockBackResist = (simulation.KnockBackResist ?? definition.KnockBackResist) / 3f,
                    Life = simulation.Life * 2,
                    LifeMax = simulation.LifeMax * 2,
                    Scale = simulation.Scale * expansion,
                    HitboxOverride = new NpcHitboxDimensions(width, height)
                };
                positionX += body.Width / 2 - width / 2;
                positionY += body.Height - height;
            }
        }
        // The source applies this before the shared ground-motion timer, so Fossil Slime advances
        // ai[0] twice per grounded tick: once here and once in VanillaBlueSlimeMotion.
        if (definition.Type == VanillaNpcIds.SandSlime && ai.Ai1 == 3347f)
        {
            ai = ai with { Ai0 = ai.Ai0 + 1f };
            simulation = simulation with
            {
                Alpha = 125,
                DamageOverride = (simulation.BaseDamage ?? definition.Damage) + 10
            };
        }
        // AI_001 increments Rainbow Slime's synchronized timer before its generic movement branch,
        // including while airborne. The balloon sentinel returns before this source branch.
        if (definition.Type == VanillaNpcIds.RainbowSlime && ai.Ai0 != -999f)
            ai = ai with { Ai0 = ai.Ai0 + 2f };
        if (definition.Type == VanillaNpcIds.SpikedIceSlime || definition.Type == VanillaNpcIds.SpikedSlime)
        {
            NpcAiState localAi = simulation.LocalAi;
            if (localAi.Ai0 > 0f)
                localAi = localAi with { Ai0 = localAi.Ai0 - 1f };

            if (!simulation.Wet && localAi.Ai0 == 0f && npc.VelocityY == 0f &&
                npc.Target < byte.MaxValue &&
                context.TryFindCandidate((byte)npc.Target, out VanillaNpcTargetCandidate target) &&
                target.Active && !target.Dead && !target.NoAggro &&
                definition.TryResolveHitbox(simulation, out VanillaNpcHitboxSize hitbox) &&
                context.ProjectileEnvironment is not null)
            {
                float centerX = npc.PositionX + hitbox.Width * .5f;
                float centerY = npc.PositionY + hitbox.Height * .5f;
                float targetTopY = target.CenterY - target.Height * .5f;
                float dx = target.CenterX - centerX;
                float dy = targetTopY - centerY;
                float distanceSquared = dx * dx + dy * dy;
                bool canHit = context.ProjectileEnvironment.CanHit(
                    npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height,
                    target.CenterX - target.Width * .5f, targetTopY,
                    (int)target.Width, (int)target.Height);
                bool expertBurst = context.ExpertMode && distanceSquared < 120f * 120f;
                if (canHit && (expertBurst || distanceSquared < 200f * 200f))
                {
                    ai = ai with { Ai0 = -40f };
                    velocityX *= .9f;
                    localAi = localAi with { Ai0 = expertBurst ? 30f : 50f };
                }
            }

            simulation = simulation with { LocalAi = localAi };
        }
        if (definition.Type == VanillaNpcIds.SpikedJungleSlime)
        {
            NpcAiState localAi = simulation.LocalAi;
            if (localAi.Ai0 > 0f)
                localAi = localAi with { Ai0 = localAi.Ai0 - 1f };

            if (!simulation.Wet && npc.VelocityY == 0f && npc.Target < byte.MaxValue &&
                context.TryFindCandidate((byte)npc.Target, out VanillaNpcTargetCandidate target) &&
                target.Active && !target.Dead && !target.NoAggro &&
                definition.TryResolveHitbox(simulation, out VanillaNpcHitboxSize hitbox) &&
                context.ProjectileEnvironment is not null)
            {
                float centerX = npc.PositionX + hitbox.Width * .5f;
                float centerY = npc.PositionY + hitbox.Height * .5f;
                float targetTopY = target.CenterY - target.Height * .5f;
                float dx = target.CenterX - centerX;
                float dy = targetTopY - centerY;
                float distanceSquared = dx * dx + dy * dy;
                bool canHit = context.ProjectileEnvironment.CanHit(
                    npc.PositionX, npc.PositionY - 20f, hitbox.Width, hitbox.Height + 20,
                    target.CenterX - target.Width * .5f, targetTopY,
                    (int)target.Width, (int)target.Height);
                if (context.ExpertMode && distanceSquared < 200f * 200f && canHit)
                {
                    ai = ai with { Ai0 = -40f };
                    velocityX *= .9f;
                    if (localAi.Ai0 == 0f)
                        localAi = localAi with { Ai0 = 80f };
                }
                // The source intentionally uses a second independent if after the Expert burst. That branch
                // resets ai[0] to -80 and applies another 0.9 velocity multiplier even after arming the burst.
                if (distanceSquared < 400f * 400f && canHit)
                {
                    ai = ai with { Ai0 = -80f };
                    velocityX *= .9f;
                    if (localAi.Ai0 == 0f)
                        localAi = localAi with { Ai0 = 65f };
                }
            }

            simulation = simulation with { LocalAi = localAi };
        }
        if (definition.Type == VanillaNpcIds.QueenSlimeMinionBlue || definition.Type == VanillaNpcIds.QueenSlimeMinionPink)
        {
            NpcAiState localAi = simulation.LocalAi;
            if (localAi.Ai0 > 0f)
                localAi = localAi with { Ai0 = localAi.Ai0 - 1f };

            if (!simulation.Wet && npc.VelocityY == 0f && npc.Target < byte.MaxValue &&
                context.TryFindCandidate((byte)npc.Target, out VanillaNpcTargetCandidate target) &&
                target.Active && !target.Dead && !target.NoAggro &&
                definition.TryResolveHitbox(simulation, out VanillaNpcHitboxSize hitbox) &&
                context.ProjectileEnvironment is not null)
            {
                float centerX = npc.PositionX + hitbox.Width * .5f;
                float centerY = npc.PositionY + hitbox.Height * .5f;
                float dx = target.CenterX - centerX;
                float dy = target.CenterY - centerY;
                bool canHit = MathF.Abs(dx) < 500f && MathF.Abs(dy) < 550f &&
                    context.ProjectileEnvironment.CanHit(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height,
                        target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f,
                        (int)target.Width, (int)target.Height);
                if (canHit)
                {
                    ai = ai with { Ai0 = -40f };
                    velocityX *= .9f;
                    if (localAi.Ai0 == 0f)
                    {
                        if (definition.Type == VanillaNpcIds.QueenSlimeMinionBlue && context.ExpertMode &&
                            context.CountNpcPeers(VanillaNpcIds.QueenSlimeMinionBlue) < 5)
                        {
                            localAi = localAi with { Ai0 = 25f };
                        }
                        else if (definition.Type == VanillaNpcIds.QueenSlimeMinionBlue)
                        {
                            localAi = localAi with { Ai0 = 50f };
                        }
                        else
                        {
                            localAi = localAi with { Ai0 = context.ExpertMode ? 30f : 40f };
                        }
                    }
                }
            }

            simulation = simulation with { LocalAi = localAi };
        }
        bool damaged = simulation.LifeMax > 0 && simulation.Life != simulation.LifeMax;
        bool engaged = definition.Type == VanillaNpcIds.CorruptSlime ||
                       definition.Type == VanillaNpcIds.Crimslime ||
                       definition.Type == VanillaNpcIds.RainbowSlime ||
                       definition.Type == VanillaNpcIds.SpikedIceSlime ||
                       definition.Type == VanillaNpcIds.SpikedSlime ||
                       definition.Type == VanillaNpcIds.SpikedJungleSlime ||
                       definition.Type == VanillaNpcIds.QueenSlimeMinionBlue ||
                       definition.Type == VanillaNpcIds.QueenSlimeMinionPink ||
                       !context.DayTime ||
                       damaged ||
                       context.SlimeRainActive ||
                       npc.PositionY > context.WorldSurfacePixels;
        if (definition.Type == VanillaNpcIds.LavaSlime && context.RemixWorld && !damaged)
            engaged = false;
        if (!VanillaSlimeNpcCatalog.TryGetMotionProfile(definition.Type, out VanillaSlimeMotionProfile profile) ||
            !profile.IsValid)
        {
            next = default;
            return false;
        }
        float timerBonus = definition.Type == VanillaNpcIds.LavaSlime && context.RemixWorld
            ? 0f
            : profile.TimerBonus;
        // Corrupt Slime (AI_001 type 81) takes its ordinary +4 cadence only for a nonnegative scale;
        // the source's negative-scale branch instead contributes +1.
        if (definition.Type == VanillaNpcIds.CorruptSlime && simulation.Scale < 0f)
            timerBonus = 1f;
        // AI_001 Hoppin' Jack: `(1 - life / lifeMax) * 10` uses integer division in the source,
        // so every damaged state receives the full ten-tick grounded cadence bonus.
        if (definition.Type == VanillaNpcIds.HoppinJack &&
            simulation.LifeMax > 0 && simulation.Life < simulation.LifeMax)
        {
            timerBonus += 10f;
        }
        var input = new VanillaBlueSlimeMotionInput(
            PositionX: positionX,
            VelocityX: velocityX,
            VelocityY: npc.VelocityY,
            OldVelocityY: simulation.OldVelocityY,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            Target: npc.Target,
            Ai: ai,
            Wet: simulation.Wet,
            CollideX: simulation.CollideX,
            CollideY: simulation.CollideY,
            Engaged: engaged,
            SolidCollision: simulation.SolidCollision,
            ClosestTarget: closest,
            TimerBonus: timerBonus,
            JumpTimerBand: profile.JumpTimerBand,
            UsesLavaSlimeMotion: definition.Type == VanillaNpcIds.LavaSlime,
            RemixWorld: context.RemixWorld);

        if (!VanillaBlueSlimeMotion.TryStep(in input, out VanillaBlueSlimeMotionResult result))
        {
            next = default;
            return false;
        }

        // AI_001 applies this only after selecting and constructing a grounded jump, so do not fold it into the
        // generic timer profile (which would incorrectly affect water escape and airborne steering).
        if (definition.Type == VanillaNpcIds.ToxicSludge && npc.VelocityY == 0f && result.VelocityY < 0f)
        {
            result = result with
            {
                VelocityX = result.VelocityX * 1.2f,
                VelocityY = result.VelocityY * 1.3f
            };
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            result.PositionX,
            positionY,
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

        // AI_003 transforms Snow Moon type 348 before the common fighter work. NPC.Transform clears ai[],
        // applies target-349 SetDefaults, preserves its bottom edge and scales life; both source hitboxes are
        // 28-by-76, so this specific transform has no position delta before the same-tick type-349 movement.
        if (definition.Type.Value == 348 && npc.Simulation.Life * 100 <= npc.Simulation.LifeMax * 55 &&
            VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(349), new NpcNetId(349), out VanillaNpcDefinition transformedDefinition))
        {
            int transformedLife = ScaleTransformLife(npc.Simulation.Life, npc.Simulation.LifeMax, 1800);
            NpcSnapshot transformed = npc with
            {
                Type = 349,
                NetId = 349,
                Ai = default,
                Simulation = npc.Simulation with
                {
                    Life = transformedLife,
                    LifeMax = 1800,
                    HitboxOverride = null,
                    BaseDamage = null,
                    BaseDefense = null,
                    BaseLifeMax = null,
                    DefenseOverride = null,
                    DamageOverride = null,
                    KnockBackResist = null,
                    NoGravity = false,
                    NoTileCollide = false,
                    DirectionX = 1,
                    DirectionY = 1,
                    LocalAi = default,
                    FrameCounter = 0d,
                    TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                    Alpha = 0,
                    Hidden = false,
                    DontTakeDamage = false,
                    ReflectsProjectiles = false,
                    JustHit = false,
                    CanBeReplacedByOtherNpcs = false,
                    Wet = false,
                    LiquidContact = NpcLiquidContactKind.None,
                    CollideX = false,
                    CollideY = false,
                    SpriteDirection = VanillaNpcDefinitionCatalog.DefaultSpriteDirection,
                    Rotation = null,
                    Friendly = null,
                    Chaseable = null,
                    Immortal = null
                }
            };
            return TryStep(in transformed, in transformedDefinition, context, inner, out next);
        }

        bool daytimeSurface = context.DayTime &&
            npc.PositionY < context.WorldSurfacePixels &&
            parameters.DaySurfaceEncouragesDespawn;
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
            ScaleAdjustsMaximumHorizontalSpeed = parameters.ScaleAdjustsMaximumHorizontalSpeed,
            ReversingVelocityDamping = parameters.ReversingVelocityDamping,
            MotionProfile = parameters.MotionProfile,
            Life = simulation.Life,
            LifeMax = simulation.LifeMax,
            HalfHealthSpeedMultiplier = parameters.HalfHealthSpeedMultiplier,
            OverspeedGroundDamping = parameters.OverspeedGroundDamping,
            MissingHealthSpeedBonus = parameters.MissingHealthSpeedBonus,
            MissingHealthAccelerationBonus = parameters.MissingHealthAccelerationBonus
        };

        if (!VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result))
        {
            next = default;
            return false;
        }

        if (definition.Type.Value == 258 &&
            result.VelocityY != 0f &&
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh airborneTarget) &&
            context.TryFindCandidate(checked((byte)airborneTarget.Target), out VanillaNpcTargetCandidate airborneCandidate) &&
            VanillaGroundFighter258Motion.TryResolveAirborne(
                new VanillaGroundFighter258AirborneInput(
                    npc.PositionX,
                    definition.Width,
                    result.VelocityX,
                    result.VelocityY,
                    airborneTarget.DirectionX,
                    airborneCandidate.CenterX),
                out VanillaGroundFighter258AirborneResult airborne))
        {
            result = result with
            {
                VelocityX = airborne.VelocityX,
                DirectionX = airborneTarget.DirectionX,
                DirectionY = airborneTarget.DirectionY,
                Target = airborneTarget.Target,
                SpriteDirection = airborne.SpriteDirection
            };
        }

        if (definition.Type == VanillaNpcIds.VampireHumanoid && result.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)result.Target, out VanillaNpcTargetCandidate vampireTarget))
        {
            float dx = vampireTarget.CenterX - (npc.PositionX + definition.Width * .5f);
            float dy = vampireTarget.CenterY - (npc.PositionY + definition.Height * .5f);
            if (dx * dx + dy * dy > 90_000f)
            {
                int transformedLife = ScaleTransformLife(simulation.Life, simulation.LifeMax, 750);
                next = new NpcStateUpdate(VanillaNpcIds.Vampire.Value, (short)VanillaNpcIds.Vampire.Value,
                    npc.PositionX, npc.PositionY + 18f, result.VelocityX, result.VelocityY, result.Target, default,
                    simulation with { Life = transformedLife, LifeMax = 750, HitboxOverride = null, BaseDamage = null, BaseDefense = null, BaseLifeMax = null,
                        DefenseOverride = null, DamageOverride = null, KnockBackResist = null, NoGravity = true, NoTileCollide = false,
                        DirectionX = vampireTarget.CenterX < npc.PositionX + 11f ? -1 : 1,
                        DirectionY = vampireTarget.CenterY < npc.PositionY + 29f ? -1 : 1,
                        LocalAi = default, FrameCounter = 0d, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                        Alpha = 0, Hidden = false, DontTakeDamage = false, ReflectsProjectiles = false, JustHit = false,
                        CanBeReplacedByOtherNpcs = false, Wet = false, LiquidContact = NpcLiquidContactKind.None,
                        CollideX = false, CollideY = false, SpriteDirection = VanillaNpcDefinitionCatalog.DefaultSpriteDirection,
                        Rotation = null, Friendly = null, Chaseable = null, Immortal = null });
                return true;
            }
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

    private static int ScaleTransformLife(int life, int lifeMax, int transformedLifeMax) =>
        lifeMax > 0 ? Math.Max(1, (int)((long)life * transformedLifeMax / lifeMax)) : transformedLifeMax;
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
        // AI_057 sets flag62 for both sustained attack branches before the common pursuit tail.
        // The low-life 3/4 branches retain pursuit until they reach the usual 50-pixel dead zone.
        bool stopHorizontal = ai0 is 1f or 2f || MathF.Abs(centerX - player.CenterX) < 50f;
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

/// <summary>Source AI_058/059 movement core for Pumpkin Moon Pumpking and its blades.</summary>
internal sealed class VanillaPumpkingNpcBehaviorStrategy(IVanillaNpcRandom random) : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi58Pumpking)
            return StepPumpking(in npc, in definition, context, out next);
        if (definition.Type == VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi59PumpkingBlade)
            return StepBlade(in npc, in definition, context, out next);
        next = default; return false;
    }

    private bool StepPumpking(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!TryTarget(in npc, in definition, context, out ushort target, out VanillaNpcTargetCandidate player))
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcAiState local = npc.Simulation.LocalAi;
        local = local with { Ai0 = local.Ai0 + 1f };
        if (local.Ai0 > 6f) local = local with { Ai0 = 0f, Ai1 = local.Ai1 >= 4f ? 0f : local.Ai1 + 1f };
        local = local with { Ai2 = local.Ai2 + 1f };
        if (local.Ai2 > 300f) { local = local with { Ai2 = 0f }; ai = ai with { Ai3 = random.NextInt32(0, 3) }; }

        // AI_058 initializes the linked blades only after TargetClosest has selected their target.
        if (ai.Ai0 == 0f)
        {
            if (!TrySelectClosest(in npc, in definition, context, out target, out player))
            {
                next = default;
                return false;
            }
            ai = ai with { Ai0 = 1f };
        }

        bool targetLost = player.Dead || MathF.Abs(npc.PositionX - (player.CenterX - player.Width * .5f)) > 2000f ||
            MathF.Abs(npc.PositionY - (player.CenterY - player.Height * .5f)) > 2000f;
        if (targetLost)
        {
            if (TrySelectClosest(in npc, in definition, context, out ushort refreshedTarget, out VanillaNpcTargetCandidate refreshedPlayer))
            {
                target = refreshedTarget;
                player = refreshedPlayer;
                targetLost = player.Dead || MathF.Abs(npc.PositionX - (player.CenterX - player.Width * .5f)) > 2000f ||
                    MathF.Abs(npc.PositionY - (player.CenterY - player.Height * .5f)) > 2000f;
            }
            if (targetLost)
                ai = ai with { Ai1 = 2f };
        }

        float vx = npc.VelocityX, vy = npc.VelocityY;
        if (context.DayTime) { vy += .3f; vx *= .9f; }
        else if (ai.Ai1 == 0f)
        {
            ai = ai with { Ai2 = ai.Ai2 + 1f };
            if (ai.Ai2 >= 300f)
            {
                if (ai.Ai3 != 1f)
                    ai = ai with { Ai1 = 0f, Ai2 = 0f };
                else
                {
                    ai = ai with { Ai1 = 1f, Ai2 = 0f };
                    if (TrySelectClosest(in npc, in definition, context, out ushort refreshedTarget, out VanillaNpcTargetCandidate refreshedPlayer))
                    {
                        target = refreshedTarget;
                        player = refreshedPlayer;
                    }
                }
            }
            float cx = npc.PositionX + 50f, cy = npc.PositionY + 50f;
            float dx = player.CenterX - cx, dy = player.CenterY - 200f - cy;
            float distance = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
            float speed = ai.Ai3 == 1f ? distance > 900f ? 12f : distance > 600f ? 10f : distance > 300f ? 8f : 6f : 6f;
            if (distance > 50f)
            {
                vx = (vx * 14f + dx / distance * speed) / 15f;
                vy = (vy * 14f + dy / distance * speed) / 15f;
            }
        }
        else if (ai.Ai1 == 1f)
        {
            ai = ai with { Ai2 = ai.Ai2 + 1f };
            if (ai.Ai2 >= 600f || ai.Ai3 != 1f)
                ai = ai with { Ai1 = 0f, Ai2 = 0f };
            float cx = npc.PositionX + 50f, cy = npc.PositionY + 50f;
            float dx = player.CenterX - cx, dy = player.CenterY - cy;
            float distance = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
            vx = (vx * 49f + dx / distance * 16f) / 50f;
            vy = (vy * 49f + dy / distance * 16f) / 50f;
        }
        else if (ai.Ai1 == 2f)
        {
            vy += .1f;
            if (vy < 0f) vy *= .95f;
            vx *= .95f;
        }

        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai,
            npc.Simulation with
            {
                LocalAi = local,
                NoGravity = true,
                NoTileCollide = true,
                DirectionX = vx < 0f ? -1 : 1,
                SpriteDirection = vx < 0f ? -1 : 1,
                Rotation = vx * -.02f,
                TimeLeft = ai.Ai1 == 2f ? EncourageDespawn(npc.Simulation.TimeLeft, 500) : npc.Simulation.TimeLeft
            });
        return true;
    }

    private static bool StepBlade(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!context.TryFindNpcPeer((byte)Math.Clamp((int)npc.Ai.Ai1, 0, byte.MaxValue), out NpcSnapshot parent) ||
            parent.TypeIdentity != VanillaMoonEventSpecialCatalog1458.PumpkinMoonAi58Pumpking)
        {
            next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY,
                npc.VelocityX * .9f, npc.VelocityY * .9f, npc.Target, npc.Ai,
                npc.Simulation with { Life = 0, TimeLeft = 0, JustHit = false });
            return true;
        }
        if (!TryTarget(in npc, in definition, context, out ushort target, out VanillaNpcTargetCandidate player))
        {
            next = default;
            return false;
        }

        int sign = (int)npc.Ai.Ai0;
        float cx = npc.PositionX + 40f, cy = npc.PositionY + 40f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        if (parent.Ai.Ai3 == 2f)
        {
            float clock = simulation.LocalAi.Ai1 + 1f;
            simulation = simulation with { LocalAi = simulation.LocalAi with { Ai1 = clock > 90f ? 0f : clock } };
        }

        if (context.DayTime)
        {
            vy += .3f;
            vx *= .9f;
        }
        else if (ai.Ai2 is 0f or 3f)
        {
            if (parent.Ai.Ai1 == 2f)
                simulation = simulation with { TimeLeft = EncourageDespawn(simulation.TimeLeft, 10) };
            ai = ai with { Ai3 = ai.Ai3 + 1f };
            if (ai.Ai3 >= 180f)
                ai = ai with { Ai2 = ai.Ai2 + 1f, Ai3 = 0f };

            float dx = (player.CenterX + parent.PositionX + 50f) * .5f - 170f * sign - cx;
            float dy = (player.CenterY + parent.PositionY + 50f) * .5f + 90f - cy;
            float playerParentDistance = MathF.Abs(player.CenterX - (parent.PositionX + 50f)) + MathF.Abs(player.CenterY - (parent.PositionY + 50f));
            if (playerParentDistance > 700f)
            {
                dx = parent.PositionX + 50f - 170f * sign - cx;
                dy = parent.PositionY + 140f - cy;
            }
            float distance = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
            float speed = distance > 1000f ? 21f : distance > 800f ? 18f : distance > 600f ? 15f : distance > 400f ? 12f : distance > 200f ? 9f : 6f;
            if (sign < 0 && cx > parent.PositionX + 50f) dx -= 4f;
            if (sign > 0 && cx < parent.PositionX + 50f) dx += 4f;
            vx = (vx * 14f + dx / distance * speed) / 15f;
            vy = (vy * 14f + dy / distance * speed) / 15f;
            if (distance > 20f)
                simulation = simulation with { Rotation = MathF.Atan2(dy, dx) + 1.57f };
        }
        else if (ai.Ai2 == 1f)
        {
            float dx = parent.PositionX + 50f - 200f * sign - cx;
            float dy = parent.PositionY + 230f - cy;
            simulation = simulation with { Rotation = MathF.Atan2(dy, dx) + 1.57f };
            vx *= .95f;
            vy = MathF.Max(-14f, vy - .3f);
            if (npc.PositionY < parent.PositionY - 200f)
            {
                if (TrySelectClosest(in npc, in definition, context, out ushort refreshedTarget, out VanillaNpcTargetCandidate refreshedPlayer))
                {
                    target = refreshedTarget;
                    player = refreshedPlayer;
                }
                ai = ai with { Ai2 = 2f };
                dx = player.CenterX - cx;
                dy = player.CenterY - cy;
                Normalize(ref dx, ref dy, 18f);
                vx = dx;
                vy = dy;
            }
        }
        else if (ai.Ai2 == 2f)
        {
            float parentDistance = MathF.Abs(cx - (parent.PositionX + 50f)) + MathF.Abs(cy - (parent.PositionY + 50f));
            if (npc.PositionY > player.CenterY - player.Height * .5f || vy < 0f || parentDistance > 800f)
                ai = ai with { Ai2 = 3f };
        }
        else if (ai.Ai2 == 4f)
        {
            float dx = parent.PositionX + 50f - 200f * sign - cx;
            float dy = parent.PositionY + 230f - cy;
            simulation = simulation with { Rotation = MathF.Atan2(dy, dx) + 1.57f };
            vy *= .95f;
            vx = Math.Clamp(vx - .3f * sign, -14f, 14f);
            if (cx < parent.PositionX - 500f || cx > parent.PositionX + 500f)
            {
                if (TrySelectClosest(in npc, in definition, context, out ushort refreshedTarget, out VanillaNpcTargetCandidate refreshedPlayer))
                {
                    target = refreshedTarget;
                    player = refreshedPlayer;
                }
                ai = ai with { Ai2 = 5f };
                dx = player.CenterX - cx;
                dy = player.CenterY - cy;
                Normalize(ref dx, ref dy, 17f);
                vx = dx;
                vy = dy;
            }
        }
        else if (ai.Ai2 == 5f)
        {
            float parentDistance = MathF.Abs(cx - (parent.PositionX + 50f)) + MathF.Abs(cy - (parent.PositionY + 50f));
            if ((vx > 0f && cx > player.CenterX) || (vx < 0f && cx < player.CenterX) || parentDistance > 800f)
                ai = ai with { Ai2 = 0f };
        }

        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai,
            simulation with { NoGravity = true, NoTileCollide = true, SpriteDirection = -sign });
        return true;
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player)
    {
        if (npc.Target < byte.MaxValue && context.TryFindCandidate((byte)npc.Target, out player))
        {
            target = npc.Target;
            return true;
        }
        return TrySelectClosest(in npc, in definition, context, out target, out player);
    }

    private static bool TrySelectClosest(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            closest.Target < byte.MaxValue && context.TryFindCandidate((byte)closest.Target, out player))
        {
            target = closest.Target;
            return true;
        }
        target = VanillaNpcDefinitionCatalog.DefaultTarget;
        player = default;
        return false;
    }

    private static int EncourageDespawn(int currentTimeLeft, int maximum) =>
        currentTimeLeft < 0 ? maximum : Math.Min(currentTimeLeft, maximum);

    private static void Normalize(ref float x, ref float y, float speed)
    {
        float distance = MathF.Max(1f, MathF.Sqrt(x * x + y * y));
        x = x / distance * speed;
        y = y / distance * speed;
    }
}

/// <summary>TerrariaServer 1.4.5.8 AI_060 state and flight motion for Snow Moon Santank.</summary>
internal sealed class VanillaSnowMoonSantankNpcBehaviorStrategy(IVanillaNpcRandom random) : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type != VanillaMoonEventSpecialCatalog1458.SnowMoonAi60Santank || definition.AiStyle.Value != 60)
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        float rotation = npc.Simulation.Rotation ?? 0f;
        ushort target = npc.Target;
        VanillaNpcTargetCandidate player = default;
        if (context.DayTime)
        {
            velocityX += velocityX > 0f ? .25f : -.25f;
            velocityY -= .1f;
            rotation = velocityX * .05f;
        }
        else
        {
            if (!TrySelectClosest(in npc, in definition, context, out target, out player))
            {
                next = default;
                return false;
            }

            float playerTop = player.CenterY - player.Height * .5f;
            float playerLeft = player.CenterX - player.Width * .5f;
            float npcCenterX = npc.PositionX + definition.Width * .5f;
            float life = simulation.Life > 0 ? simulation.Life : definition.LifeMax;
            if (ai.Ai0 == 0f)
            {
                if (ai.Ai2 == 0f)
                    ai = ai with { Ai2 = npcCenterX < player.CenterX ? 1f : -1f };
                if ((ai.Ai2 == 1f && npcCenterX > player.CenterX + 800f) ||
                    (ai.Ai2 == -1f && npcCenterX < player.CenterX - 800f))
                    ai = ai with { Ai2 = 0f };

                float acceleration = .45f, maximumSpeed = 7f;
                if (life < definition.LifeMax * .75f) { acceleration = .55f; maximumSpeed = 8f; }
                if (life < definition.LifeMax * .5f) { acceleration = .7f; maximumSpeed = 10f; }
                if (life < definition.LifeMax * .25f) { acceleration = .8f; maximumSpeed = 11f; }
                velocityX = Math.Clamp(velocityX + ai.Ai2 * acceleration, -maximumSpeed, maximumSpeed);
                float verticalDifference = playerTop - (npc.PositionY + definition.Height);
                if (verticalDifference < 150f) velocityY -= .2f;
                else if (verticalDifference > 200f) velocityY += .2f;
                velocityY = Math.Clamp(velocityY, -8f, 8f);
                rotation = velocityX * .05f;

                if ((MathF.Abs(npcCenterX - player.CenterX) < 500f || ai.Ai3 < 0f) && npc.PositionY < playerTop)
                {
                    int cadence = life < definition.LifeMax * .25f ? 10 : life < definition.LifeMax * .5f ? 11 :
                        life < definition.LifeMax * .75f ? 12 : 13;
                    cadence++;
                    ai = ai with { Ai3 = ai.Ai3 + 1f };
                    if (ai.Ai3 > cadence)
                        ai = ai with { Ai3 = -cadence };
                }
                else if (ai.Ai3 < 0f)
                    ai = ai with { Ai3 = ai.Ai3 + 1f };

                ai = ai with { Ai1 = ai.Ai1 + random.NextInt32(1, 4) };
                if (ai.Ai1 > 800f && MathF.Abs(npcCenterX - player.CenterX) < 600f)
                    ai = ai with { Ai0 = -1f };
            }
            else if (ai.Ai0 == 1f)
            {
                float acceleration = .15f, maximumSpeed = 7f;
                if (life < definition.LifeMax * .75f) { acceleration = .17f; maximumSpeed = 8f; }
                if (life < definition.LifeMax * .5f) { acceleration = .2f; maximumSpeed = 9f; }
                if (life < definition.LifeMax * .25f) { acceleration = .25f; maximumSpeed = 10f; }
                acceleration -= .05f;
                maximumSpeed--;
                if (npcCenterX < player.CenterX) velocityX += acceleration;
                else velocityX -= acceleration;
                if ((velocityX > 0f && npcCenterX > player.CenterX) || (velocityX < 0f && npcCenterX < player.CenterX)) velocityX *= .98f;
                if (velocityX > maximumSpeed) velocityX *= .95f;
                if (velocityX < -maximumSpeed) velocityX *= .95f;
                float verticalDifference = playerTop - (npc.PositionY + definition.Height);
                if (verticalDifference < 180f) velocityY -= .1f;
                else if (verticalDifference > 200f) velocityY += .1f;
                velocityY = Math.Clamp(velocityY, -6f, 6f);
                rotation = velocityX * .01f;

                int cadence = life < definition.LifeMax * .1f ? 8 : life < definition.LifeMax * .25f ? 10 :
                    life < definition.LifeMax * .5f ? 12 : life < definition.LifeMax * .75f ? 14 : 15;
                cadence += 3;
                ai = ai with { Ai3 = ai.Ai3 + 1f };
                if (ai.Ai3 >= cadence)
                    ai = ai with { Ai3 = 0f };
                ai = ai with { Ai1 = ai.Ai1 + random.NextInt32(1, 4) };
                if (ai.Ai1 > 600f) ai = ai with { Ai0 = -1f };
            }
            else if (ai.Ai0 == 2f)
            {
                float shotX = random.NextInt32(-1000, 1001);
                float shotY = random.NextInt32(-1000, 1001);
                Normalize(ref shotX, ref shotY, 15f);
                simulation = simulation with { LocalAi = simulation.LocalAi with { Ai0 = shotX, Ai1 = shotY } };
                velocityX *= .95f;
                velocityY *= .95f;
                rotation += .2f;
                ai = ai with { Ai3 = ai.Ai3 + 1f };
                int cadence = life < definition.LifeMax * .1f ? 4 : life < definition.LifeMax * .25f ? 3 :
                    life < definition.LifeMax * .5f ? 2 : life < definition.LifeMax * .75f ? 1 : 7;
                if (ai.Ai3 > cadence) ai = ai with { Ai3 = 0f };
                ai = ai with { Ai1 = ai.Ai1 + random.NextInt32(1, 4) };
                if (ai.Ai1 > 500f) ai = ai with { Ai0 = -1f };
            }

            if (ai.Ai0 == -1f)
            {
                int state = random.NextInt32(0, 3);
                if (MathF.Abs(npcCenterX - player.CenterX) > 1000f) state = 0;
                ai = new NpcAiState(state, 0f, 0f, 0f);
            }
        }

        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY,
            target, ai, simulation with
            {
                NoGravity = true, NoTileCollide = true, DirectionX = velocityX < 0f ? -1 : 1,
                SpriteDirection = velocityX < 0f ? -1 : 1, Rotation = rotation, JustHit = false
            });
        return true;
    }

    private static bool TrySelectClosest(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            closest.Target < byte.MaxValue && context.TryFindCandidate((byte)closest.Target, out player) &&
            player.Active && !player.Dead && !player.Ghost)
        {
            target = closest.Target;
            return true;
        }
        target = VanillaNpcDefinitionCatalog.DefaultTarget;
        player = default;
        return false;
    }

    private static void Normalize(ref float x, ref float y, float speed)
    {
        float distance = MathF.Max(1f, MathF.Sqrt(x * x + y * y));
        x = x / distance * speed;
        y = y / distance * speed;
    }
}

/// <summary>TerrariaServer 1.4.5.8 AI_062 pursuit, retreat and stationary shot clock.</summary>
internal sealed class VanillaSnowMoonAi62NpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaNpcProjectileEnvironment? environment;

    public void SetEnvironment(IVanillaNpcProjectileEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type != VanillaMoonEventSpecialCatalog1458.SnowMoonAi62 || definition.AiStyle.Value != 62 ||
            environment is null ||
            !TryTarget(in npc, in definition, context, out ushort target, out VanillaNpcTargetCandidate player, out int directionX))
        { next = default; return false; }
        float centerX = npc.PositionX + 25f, centerY = npc.PositionY + 25f;
        float sourceX = centerX + directionX * 20f, sourceY = centerY + 6f;
        float dx = player.CenterX - sourceX, dy = player.CenterY - player.Height * .5f - sourceY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        Normalize(ref dx, ref dy, 7f);
        float velocityX = npc.VelocityX, velocityY = npc.VelocityY;
        NpcSimulationState simulation = npc.Simulation;
        if (context.DayTime)
        {
            velocityX = (velocityX * 59f - dx) / 60f;
            velocityY = (velocityY * 59f - dy) / 60f;
            simulation = simulation with { TimeLeft = EncourageDespawn(simulation.TimeLeft, 10) };
        }
        else if (distance > 600f || !environment.CanHit(centerX, centerY, 1, 1, player.CenterX, player.CenterY, 1, 1))
        {
            velocityX = (velocityX * 59f + dx) / 60f;
            velocityY = (velocityY * 59f + dy) / 60f;
        }
        else
        {
            velocityX *= .98f; velocityY *= .98f;
            if (MathF.Abs(velocityX) < 1f && MathF.Abs(velocityY) < 1f)
            {
                float clock = simulation.LocalAi.Ai0 + 1f;
                simulation = simulation with { LocalAi = simulation.LocalAi with { Ai0 = clock >= 15f ? 0f : clock } };
            }
        }
        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY, target,
            npc.Ai, simulation with { NoGravity = true, NoTileCollide = true, DirectionX = directionX, SpriteDirection = directionX,
                Rotation = MathF.Abs(velocityX) * directionX * .1f, JustHit = false });
        return true;
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player, out int directionX)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            closest.Target < byte.MaxValue && context.TryFindCandidate((byte)closest.Target, out player))
        { target = closest.Target; directionX = closest.DirectionX; return true; }
        target = VanillaNpcDefinitionCatalog.DefaultTarget; player = default; directionX = 0; return false;
    }

    private static int EncourageDespawn(int timeLeft, int maximum) => timeLeft < 0 ? maximum : Math.Min(timeLeft, maximum);
    private static void Normalize(ref float x, ref float y, float speed) { float d = MathF.Max(1f, MathF.Sqrt(x * x + y * y)); x = x / d * speed; y = y / d * speed; }
}

/// <summary>TerrariaServer 1.4.5.8 AI_063 close-range orbit and pursuit motion.</summary>
internal sealed class VanillaSnowMoonAi63NpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type != VanillaMoonEventSpecialCatalog1458.SnowMoonAi63 || definition.AiStyle.Value != 63 ||
            !TryTarget(in npc, in definition, context, out ushort target, out VanillaNpcTargetCandidate player, out int direction))
        { next = default; return false; }
        float sourceX = npc.PositionX + 27f + direction * 20f, sourceY = npc.PositionY + 33f;
        float dx = player.CenterX - sourceX, dy = player.CenterY - sourceY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        Normalize(ref dx, ref dy, 11f);
        if (context.DayTime) { dx = -dx; dy = -dy; }
        NpcAiState ai = npc.Ai with { Ai0 = npc.Ai.Ai0 - 1f };
        float velocityX = npc.VelocityX, velocityY = npc.VelocityY;
        NpcSimulationState simulation = npc.Simulation;
        if (distance < 200f || ai.Ai0 > 0f)
        {
            if (distance < 200f) ai = ai with { Ai0 = 20f };
            direction = velocityX < 0f ? -1 : 1;
            simulation = simulation with { Rotation = (simulation.Rotation ?? 0f) + direction * .3f };
        }
        else
        {
            velocityX = (velocityX * 50f + dx) / 51f;
            velocityY = (velocityY * 50f + dy) / 51f;
            if (distance < 350f) { velocityX = (velocityX * 10f + dx) / 11f; velocityY = (velocityY * 10f + dy) / 11f; }
            if (distance < 300f) { velocityX = (velocityX * 7f + dx) / 8f; velocityY = (velocityY * 7f + dy) / 8f; }
            simulation = simulation with { Rotation = velocityX * .15f };
        }
        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY, target, ai,
            simulation with { NoGravity = true, NoTileCollide = true, DirectionX = direction, SpriteDirection = direction, JustHit = false });
        return true;
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player, out int direction)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            closest.Target < byte.MaxValue && context.TryFindCandidate((byte)closest.Target, out player))
        { target = closest.Target; direction = closest.DirectionX; return true; }
        target = VanillaNpcDefinitionCatalog.DefaultTarget; player = default; direction = 0; return false;
    }

    private static void Normalize(ref float x, ref float y, float speed) { float d = MathF.Max(1f, MathF.Sqrt(x * x + y * y)); x = x / d * speed; y = y / d * speed; }
}

/// <summary>TerrariaServer 1.4.5.8 AI_061 flight and phase clock for Snow Moon Ice Queen.</summary>
internal sealed class VanillaSnowMoonIceQueenNpcBehaviorStrategy(IVanillaNpcRandom random) : IVanillaNpcBehaviorStrategy
{
    private IVanillaEverscreamEnvironment? environment;

    public void SetEnvironment(IVanillaEverscreamEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        if (definition.Type != VanillaMoonEventSpecialCatalog1458.SnowMoonAi61IceQueen || definition.AiStyle.Value != 61 ||
            environment is null || !TryTarget(in npc, in definition, context, out ushort target, out VanillaNpcTargetCandidate player,
                out int directionX, out int directionY))
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        NpcAiState ai = npc.Ai;
        float life = simulation.Life > 0 ? simulation.Life : definition.LifeMax;
        float speed = life < definition.LifeMax * .25f ? 5f : life < definition.LifeMax * .5f ? 4f :
            life < definition.LifeMax * .75f ? 3f : 2f;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        bool haltHorizontal = false;
        int timeLeft = simulation.TimeLeft;
        float centerX = npc.PositionX + definition.Width * .5f;
        float centerY = npc.PositionY + definition.Height * .5f;
        float playerLeft = player.CenterX - player.Width * .5f;
        float playerTop = player.CenterY - player.Height * .5f;
        if (context.DayTime)
        {
            timeLeft = EncourageDespawn(timeLeft, 10);
            speed = 8f;
            if (velocityX == 0f) velocityX = .1f;
        }
        else if (ai.Ai0 == 0f)
        {
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            if (ai.Ai1 >= 300f) ai = ai with { Ai0 = 1f, Ai1 = 0f };
        }
        else if (ai.Ai0 == 1f)
        {
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            haltHorizontal = true;
            if (ai.Ai1 > 240f) ai = ai with { Ai0 = 0f, Ai1 = 0f };
        }

        if (!context.DayTime)
        {
            int spikeRate = life < definition.LifeMax * .25f ? 300 : life < definition.LifeMax * .5f ? 450 : life < definition.LifeMax * .75f ? 540 : 600;
            int flareRate = life < definition.LifeMax * .25f ? 600 : life < definition.LifeMax * .5f ? 900 : life < definition.LifeMax * .75f ? 1080 : 1200;
            int waveRate = life < definition.LifeMax * .25f ? 1350 : life < definition.LifeMax * .5f ? 2025 : life < definition.LifeMax * .75f ? 2430 : 2700;
            NpcAiState local = simulation.LocalAi with { Ai0 = 0f };
            if (random.NextInt32(0, spikeRate) == 0)
                local = local with { Ai0 = 1f, Ai3 = random.NextInt32(1, 100) * directionX };
            if (random.NextInt32(0, flareRate) == 0) local = local with { Ai1 = 1f };
            if (local.Ai1 >= 1f)
            {
                local = local with { Ai1 = local.Ai1 + 1f };
                if (local.Ai1 >= 100f) local = local with { Ai1 = 0f };
            }
            if (random.NextInt32(0, waveRate) == 0) local = local with { Ai2 = 2f };
            if (local.Ai2 > 0f)
            {
                local = local with { Ai2 = local.Ai2 + 1f };
                if (local.Ai2 >= 100f) local = local with { Ai2 = 0f };
            }
            simulation = simulation with { LocalAi = local };
        }

        if (MathF.Abs(centerX - player.CenterX) < 50f) haltHorizontal = true;
        if (haltHorizontal)
        {
            velocityX *= .9f;
            if (velocityX is > -.1f and < .1f) velocityX = 0f;
        }
        else if (directionX > 0)
            velocityX = (velocityX * 20f + speed) / 21f;
        else if (directionX < 0)
            velocityX = (velocityX * 20f - speed) / 21f;

        bool overlapsFromAbove = npc.PositionX < playerLeft && npc.PositionX + definition.Width > playerLeft + player.Width &&
            npc.PositionY + definition.Height < playerTop + player.Height - 16f;
        bool hasGroundBelow = environment.SolidCollision(centerX - 40f, npc.PositionY + definition.Height - 20f, 80, 20);
        if (overlapsFromAbove)
            velocityY += .5f;
        else if (hasGroundBelow)
        {
            if (velocityY > 0f) velocityY = 0f;
            velocityY -= velocityY > -.2f ? .025f : .2f;
            if (velocityY < -4f) velocityY = -4f;
        }
        else
        {
            if (velocityY < 0f) velocityY = 0f;
            velocityY += velocityY < .1f ? .025f : .5f;
        }
        if (velocityY > 10f) velocityY = 10f;

        next = new NpcStateUpdate(definition.Type.Value, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY,
            target, ai, simulation with
            {
                NoGravity = true, NoTileCollide = true, DirectionX = directionX, DirectionY = directionY,
                SpriteDirection = directionX, JustHit = false, TimeLeft = timeLeft
            });
        return true;
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out ushort target, out VanillaNpcTargetCandidate player, out int directionX, out int directionY)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) && closest.HasTarget &&
            closest.Target < byte.MaxValue && context.TryFindCandidate((byte)closest.Target, out player))
        {
            target = closest.Target; directionX = closest.DirectionX; directionY = closest.DirectionY;
            return true;
        }
        target = VanillaNpcDefinitionCatalog.DefaultTarget; player = default; directionX = 0; directionY = 0;
        return false;
    }

    private static int EncourageDespawn(int timeLeft, int maximum) => timeLeft < 0 ? maximum : Math.Min(timeLeft, maximum);
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
            bool idleClearJustHit = TryAdvanceGoodWorldEaterSpitClock(in npc, context, out NpcAiState idleLocalAi);
            NpcSimulationState idleSimulation = npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = definition.NoTileCollideAtSpawn,
                LocalAi = idleLocalAi,
                JustHit = idleClearJustHit ? false : npc.Simulation.JustHit
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
        bool clearJustHit = TryAdvanceGoodWorldEaterSpitClock(in npc, context, out localAi);
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
                LocalAi = localAi,
                JustHit = clearJustHit ? false : npc.Simulation.JustHit
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

    public NpcSnapshot CompleteHornetStingerAttack(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (!IsHornetStingerShooter(before.TypeIdentity) || committed.TypeIdentity != before.TypeIdentity ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(committed.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return committed;
        }

        float timer = committed.Ai.Ai1 == 101f ? 0f : committed.Ai.Ai1;
        float scale = committed.Simulation.Scale;
        if (!float.IsFinite(scale) || scale <= 0f)
            return committed;

        timer += random.NextInt32(5, 20) * .1f * scale;
        if (before.TypeIdentity == VanillaNpcIds.MossHornet)
            timer += random.NextInt32(5, 20) * .1f * scale;
        if (context.GoodWorld)
            timer += random.NextInt32(5, 20) * .1f * scale;

        VanillaNpcTargetCandidate target = default;
        bool hasPlayer = committed.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)committed.Target, out target) &&
            target.Active && !target.Dead && !target.Ghost;
        if (hasPlayer && target.Stealth == 0f && target.ItemAnimation == 0)
            timer = 0f;

        bool hasShot = false;
        float shotX = 0f;
        float shotY = 0f;
        if (timer >= 130f)
        {
            float centerX = committed.PositionX + hitbox.Width * .5f;
            float centerY = committed.PositionY + hitbox.Height * .5f;
            bool canShoot = hasPlayer && projectileEnvironment is not null &&
                VanillaNpcGlobalFiringDistance.Contains(centerX, centerY, target.CenterX, target.CenterY) &&
                projectileEnvironment.CanHit(
                    committed.PositionX, committed.PositionY, hitbox.Width, hitbox.Height,
                    target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f,
                    (int)target.Width, (int)target.Height);
            if (canShoot)
            {
                shotX = target.CenterX - centerX + random.NextInt32(-20, 21);
                shotY = target.CenterY - centerY + random.NextInt32(-20, 21);
                if ((shotX < 0f && committed.VelocityX < 0f) || (shotX > 0f && committed.VelocityX > 0f))
                {
                    float length = MathF.Sqrt(shotX * shotX + shotY * shotY);
                    if (length > 0f && float.IsFinite(length))
                    {
                        shotX = shotX / length * 8f;
                        shotY = shotY / length * 8f;
                        timer = 101f;
                        hasShot = true;
                    }
                    else
                    {
                        timer = 0f;
                    }
                }
                else
                {
                    timer = 0f;
                }
            }
            else
            {
                timer = 0f;
            }
        }

        NpcAiState ai = committed.Ai with { Ai1 = timer };
        if (ai == committed.Ai)
            return committed;

        if (!mutations.TryUpdateAi(in committed, ai, out NpcSnapshot completed))
            return committed;

        if (hasShot)
        {
            float centerX = completed.PositionX + hitbox.Width * .5f;
            float centerY = completed.PositionY + hitbox.Height * .5f;
            int damage = (int)((before.TypeIdentity == VanillaNpcIds.MossHornet ? 30f : 10f) * scale);
            var intent = new NpcAiProjectileIntent(
                VanillaProjectileIds.HornetStinger, centerX, centerY, shotX, shotY, damage, 0f)
            {
                TimeLeftOverride = 300
            };
            mutations.TrySpawnProjectile(in completed, in intent, out _);
        }

        return completed;
    }

    public static bool IsHornetStingerShooter(NpcTypeId type) =>
        type == VanillaNpcIds.Hornet ||
        type == VanillaNpcIds.MossHornet ||
        type.Value is >= 231 and <= 235;

    private static bool TryAdvanceGoodWorldEaterSpitClock(
        in NpcSnapshot npc,
        VanillaNpcBehaviorContext context,
        out NpcAiState localAi)
    {
        localAi = npc.Simulation.LocalAi;
        if (npc.TypeIdentity != VanillaNpcIds.EaterOfSouls || !context.GoodWorld ||
            context.CountNpcPeers(VanillaNpcIds.EaterOfWorldsHead) == 0)
        {
            return false;
        }

        // AI_005: a hit restarts this server-only clock, then the same tick advances it. The resulting exact
        // 60-tick edge is consumed after the NPC motion proposal commits, even if no player target exists.
        float timer = npc.Simulation.JustHit ? 0f : localAi.Ai0;
        timer += 1f;
        localAi = localAi with { Ai0 = timer == 60f ? 0f : timer };
        return true;
    }

    public void SpawnGoodWorldEaterSpit(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.EaterOfSouls || committed.TypeIdentity != before.TypeIdentity ||
            !context.GoodWorld || context.CountNpcPeers(VanillaNpcIds.EaterOfWorldsHead) == 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(committed.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return;
        }

        float beforeTimer = before.Simulation.JustHit ? 0f : before.Simulation.LocalAi.Ai0;
        if (beforeTimer + 1f != 60f || committed.Simulation.LocalAi.Ai0 != 0f ||
            committed.Target >= byte.MaxValue || !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost || projectileEnvironment is null)
        {
            return;
        }

        float centerX = committed.PositionX + hitbox.Width * .5f;
        float centerY = committed.PositionY + hitbox.Height * .5f;
        if (!VanillaNpcGlobalFiringDistance.Contains(centerX, centerY, target.CenterX, target.CenterY) ||
            !projectileEnvironment.CanHit(
                committed.PositionX, committed.PositionY, hitbox.Width, hitbox.Height,
                target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f,
                (int)target.Width, (int)target.Height))
        {
            return;
        }

        mutations.TrySpawn(in committed, new NpcAiSpawnIntent(
            VanillaNpcIds.EaterOfWorldsSpit,
            (int)(centerX + committed.VelocityX),
            (int)(centerY + committed.VelocityY),
            0f,
            0f,
            byte.MaxValue), out _);
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
