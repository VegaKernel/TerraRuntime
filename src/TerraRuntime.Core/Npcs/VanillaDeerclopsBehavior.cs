using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// World queries consumed by the authoritative portions of TerrariaServer 1.4.5.8 NPC.AI_123_Deerclops.
/// The interface deliberately exposes gameplay-shaped facts instead of WorldTileStore so Core remains independent
/// from persistence/storage ownership.
/// </summary>
public interface IVanillaDeerclopsEnvironment
{
    int WorldHeightTiles { get; }

    bool IsPlayerInSnow(float playerCenterX, float playerCenterY);

    bool IsWalkableTile(int tileX, int tileY);

    bool IsSolidTile(int tileX, int tileY);

    bool SolidCollision(float positionX, float positionY, int width, int height, bool acceptTopSurfaces);
}

/// <summary>
/// Clean-room authoritative gameplay slice of TerrariaServer 1.4.5.8 NPC.AI_123_Deerclops. Presentation-only
/// dust, sounds, camera punches and netUpdate flags are excluded. State timing, target refresh, home/timeout,
/// retreat/teleport/despawn, shield state and movement are retained. Projectile publication is planned separately
/// by <see cref="VanillaNpcTargetingAiStepper"/> so a rejected NPC state commit cannot leak side effects.
/// </summary>
internal sealed class VanillaDeerclopsNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const float PlayerWidth = VanillaPlayerHitboxFacts.BaseWidth;
    private const float PlayerHeight = VanillaPlayerHitboxFacts.BaseHeight;
    private IVanillaDeerclopsEnvironment? environment;

    public void SetEnvironment(IVanillaDeerclopsEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.Deerclops ||
            npc.TypeIdentity != VanillaNpcIds.Deerclops ||
            environment is null ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        NpcAiState localAi = simulation.LocalAi;
        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        bool hasTarget = TryGetCurrentTarget(targetSlot, context, out VanillaNpcTargetCandidate target);
        if (!hasTarget && TryRefreshClosest(in npc, in definition, context, ref targetSlot, ref simulation, out target))
            hasTarget = true;

        float centerX = positionX + hitbox.Width * 0.5f;
        float centerY = positionY + hitbox.Height * 0.5f;
        float targetDistance = hasTarget ? Distance(centerX, centerY, target.CenterX, target.CenterY) : float.PositiveInfinity;
        float shieldCounter = Math.Clamp(localAi.Ai3 + (targetDistance >= 450f ? 1f : -1f), 0f, 30f);
        localAi = localAi with { Ai3 = shieldCounter };
        simulation = simulation with { DontTakeDamage = shieldCounter >= 30f };

        if (ai.Ai2 == 0f && ai.Ai3 == 0f)
        {
            int homeTileX = (int)MathF.Floor((positionX + hitbox.Width * 0.5f) / 16f);
            int homeTileY = (int)MathF.Floor((positionY + hitbox.Height) / 16f);
            ai = ai with { Ai2 = homeTileX, Ai3 = homeTileY };
            simulation = simulation with { TimeLeft = 86_400 };
        }

        int timeLeft = Math.Max(0, simulation.TimeLeft - 1);
        simulation = simulation with { TimeLeft = timeLeft };
        if (!context.ExpertMode)
        {
            localAi = localAi with { Ai2 = 0f };
        }
        else
        {
            int passiveInterval = ResolvePassiveShadowHandInterval(simulation.Life, simulation.LifeMax);
            int passiveCounter = Math.Max(0, (int)localAi.Ai2) + 1;
            if (passiveCounter % passiveInterval == 0 && passiveCounter / passiveInterval >= 3)
                passiveCounter = 0;
            localAi = localAi with { Ai2 = passiveCounter };
        }

        int homeX = (int)ai.Ai2;
        int homeY = (int)ai.Ai3;
        bool haltMovement = false;
        bool goHome = false;

        switch ((int)ai.Ai0)
        {
            case -1:
                localAi = localAi with { Ai3 = -10f };
                break;

            case 6:
                TryRefreshClosestAtCurrentPosition(
                    in npc,
                    positionX,
                    positionY,
                    hitbox,
                    definition,
                    context,
                    ref targetSlot,
                    ref simulation,
                    out target,
                    out hasTarget);
                if (!ShouldRunAway(hasTarget, in target, centerX, centerY, homeX, homeY, isChasing: false, environment))
                {
                    ai = ResetAttack(ai, 0f);
                    localAi = localAi with { Ai1 = 0f };
                    break;
                }
                if (timeLeft <= 0)
                {
                    ai = ResetAttack(ai, 8f);
                    localAi = localAi with { Ai1 = 0f };
                    break;
                }

                goHome = true;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                float homeCenterX = homeX * 16f;
                float homeCenterY = homeY * 16f;
                bool farBelowHome = positionY > homeCenterY + 1600f;
                bool nearHome = Distance(centerX, centerY, homeCenterX, homeCenterY) < 1020f;
                if (nearHome && ai.Ai1 % 600f < 420f)
                    haltMovement = true;
                bool shouldTeleportHome = (farBelowHome && ai.Ai1 >= 300f) || (!nearHome && ai.Ai1 >= 1500f);
                if (shouldTeleportHome)
                {
                    ai = ResetAttack(ai, 7f);
                    localAi = localAi with { Ai1 = 0f };
                }
                break;

            case 0:
                TryRefreshClosestAtCurrentPosition(
                    in npc,
                    positionX,
                    positionY,
                    hitbox,
                    definition,
                    context,
                    ref targetSlot,
                    ref simulation,
                    out target,
                    out hasTarget);
                if (ShouldRunAway(hasTarget, in target, centerX, centerY, homeX, homeY, isChasing: true, environment))
                {
                    ai = ResetAttack(ai, 6f);
                    localAi = localAi with { Ai1 = 0f };
                    break;
                }

                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (hasTarget)
                    SelectChaseAttack(in target, hitbox, positionX, positionY, ref velocityX, velocityY, ref ai, ref localAi);
                break;

            case 1:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                if (ai.Ai1 >= 80f)
                    ai = ResetAttack(ai, 0f);
                break;

            case 4:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                TryRefreshClosestAtCurrentPosition(
                    in npc,
                    positionX,
                    positionY,
                    hitbox,
                    definition,
                    context,
                    ref targetSlot,
                    ref simulation,
                    out target,
                    out hasTarget);
                if (ai.Ai1 >= 90f)
                    ai = ResetAttack(ai, 0f);
                break;

            case 2:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                if (ai.Ai1 >= 60f)
                    ai = ResetAttack(ai, 0f);
                break;

            case 3:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                if (ai.Ai1 == 30f)
                {
                    TryRefreshClosestAtCurrentPosition(
                        in npc,
                        positionX,
                        positionY,
                        hitbox,
                        definition,
                        context,
                        ref targetSlot,
                        ref simulation,
                        out target,
                        out hasTarget);
                }
                if (ai.Ai1 >= 60f)
                    ai = ResetAttack(ai, 0f);
                break;

            case 7:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                if (ai.Ai1 == 40f)
                {
                    TryRefreshClosestAtCurrentPosition(
                        in npc,
                        positionX,
                        positionY,
                        hitbox,
                        definition,
                        context,
                        ref targetSlot,
                        ref simulation,
                        out target,
                        out hasTarget);
                    positionX = homeX * 16f - hitbox.Width * 0.5f;
                    positionY = homeY * 16f - hitbox.Height;
                }
                if (ai.Ai1 >= 60f)
                    ai = ResetAttack(ai, 0f);
                break;

            case 8:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                if (ai.Ai1 >= 40f)
                {
                    simulation = simulation with { Life = -1, TimeLeft = 0 };
                    velocityX = 0f;
                    velocityY = 0f;
                }
                break;

            case 5:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                haltMovement = true;
                if (ai.Ai1 == 30f)
                {
                    TryRefreshClosestAtCurrentPosition(
                        in npc,
                        positionX,
                        positionY,
                        hitbox,
                        definition,
                        context,
                        ref targetSlot,
                        ref simulation,
                        out target,
                        out hasTarget);
                }
                if (ai.Ai1 >= 60f)
                    ai = ResetAttack(ai, 0f);
                break;
        }

        centerX = positionX + hitbox.Width * 0.5f;
        centerY = positionY + hitbox.Height * 0.5f;
        if (simulation.Life < 0 && simulation.TimeLeft == 0)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                positionX,
                positionY,
                0f,
                0f,
                targetSlot,
                ai,
                simulation with
                {
                    NoGravity = true,
                    NoTileCollide = true,
                    LocalAi = localAi,
                    JustHit = false
                });
            return true;
        }

        StepMovement(
            environment,
            hitbox,
            hasTarget,
            in target,
            homeX,
            homeY,
            ai.Ai0,
            haltMovement,
            goHome,
            positionX,
            positionY,
            centerX,
            centerY,
            simulation.Life,
            simulation.LifeMax,
            ref velocityX,
            ref velocityY,
            ref localAi,
            ref simulation);

        simulation = simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = localAi,
            JustHit = false
        };
        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            positionX,
            positionY,
            velocityX,
            velocityY,
            targetSlot,
            ai,
            simulation);
        return true;
    }

    internal static int ResolvePassiveShadowHandInterval(int life, int lifeMax)
    {
        if (lifeMax <= 0)
            return 80;
        float lifePercent = Math.Clamp(life / (float)lifeMax, 0f, 1f);
        return Math.Clamp((int)(40f + 40f * lifePercent), 40, 80);
    }

    private static void SelectChaseAttack(
        in VanillaNpcTargetCandidate target,
        in VanillaNpcHitboxSize hitbox,
        float positionX,
        float positionY,
        ref float velocityX,
        float velocityY,
        ref NpcAiState ai,
        ref NpcAiState localAi)
    {
        float probeX = positionX + hitbox.Width * 0.5f;
        float probeY = positionY + hitbox.Height - 32f;
        float left = target.CenterX - PlayerWidth * 0.5f;
        float right = target.CenterX + PlayerWidth * 0.5f;
        float top = target.CenterY - PlayerHeight * 0.5f;
        float bottom = target.CenterY + PlayerHeight * 0.5f;
        float closestX = Math.Clamp(probeX, left, right);
        float closestY = Math.Clamp(probeY, top, bottom);
        float dx = closestX - probeX;
        float dy = closestY - probeY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        bool mostlyHorizontal = MathF.Abs(dx) >= MathF.Abs(dy) * 0.6f || distance < 48f;
        bool verticalBand = dy <= 100f + PlayerHeight && dy >= -200f;

        if (MathF.Abs(dx) < 120f && verticalBand && velocityY == 0f && localAi.Ai1 >= 2f)
        {
            velocityX = 0f;
            ai = ResetAttack(ai, 4f);
            localAi = localAi with { Ai1 = 0f };
            return;
        }

        if (MathF.Abs(dx) < 120f && verticalBand && velocityY == 0f && mostlyHorizontal)
        {
            velocityX = 0f;
            ai = ResetAttack(ai, 1f);
            localAi = localAi with { Ai1 = localAi.Ai1 + 1f };
            return;
        }

        if (ai.Ai1 >= 240f && velocityY == 0f && velocityX != 0f)
        {
            velocityX = 0f;
            ai = ResetAttack(ai, 2f);
            localAi = localAi with { Ai1 = 0f };
            return;
        }

        if (ai.Ai1 >= 90f && velocityY == 0f && velocityX == 0f)
        {
            velocityX = 0f;
            ai = ResetAttack(ai, 5f);
            localAi = localAi with { Ai1 = 0f };
            return;
        }

        bool canReceiveSlow = !target.SlowBuffImmune && !target.HasSlowBuff;
        if (ai.Ai1 >= 120f && velocityY == 0f && canReceiveSlow && MathF.Abs(dx) > 100f)
        {
            velocityX = 0f;
            ai = ResetAttack(ai, 3f);
            localAi = localAi with { Ai1 = 0f };
        }
    }

    private static void StepMovement(
        IVanillaDeerclopsEnvironment environment,
        in VanillaNpcHitboxSize hitbox,
        bool hasTarget,
        in VanillaNpcTargetCandidate target,
        int homeX,
        int homeY,
        float state,
        bool haltMovement,
        bool goHome,
        float positionX,
        float positionY,
        float centerX,
        float centerY,
        int life,
        int lifeMax,
        ref float velocityX,
        ref float velocityY,
        ref NpcAiState localAi,
        ref NpcSimulationState simulation)
    {
        float lifePercent = lifeMax > 0 ? Math.Clamp((float)life / lifeMax, 0f, 1f) : 1f;
        float speed = 3.5f + (1f - lifePercent);
        float horizontalLerpDivisor = 4f;
        float upwardAcceleration = -0.4f;
        const float minimumUpwardVelocity = -8f;
        const float downwardAcceleration = 0.4f;

        float targetLeft;
        float targetTop;
        float targetWidth;
        float targetHeight;
        if (goHome)
        {
            targetLeft = homeX * 16f;
            targetTop = homeY * 16f;
            targetWidth = 16f;
            targetHeight = 16f;
            if (Distance(centerX, centerY, targetLeft + 8f, targetTop + 8f) < 240f)
                targetLeft = centerX + 160f * NormalizeDirection(simulation.DirectionX) - 8f;
        }
        else if (hasTarget)
        {
            targetLeft = target.CenterX - PlayerWidth * 0.5f;
            targetTop = target.CenterY - PlayerHeight * 0.5f;
            targetWidth = PlayerWidth;
            targetHeight = PlayerHeight;
        }
        else
        {
            targetLeft = centerX;
            targetTop = centerY;
            targetWidth = 1f;
            targetHeight = 1f;
        }

        float targetCenterX = targetLeft + targetWidth * 0.5f;
        float dx = targetCenterX - centerX;
        float absDx = MathF.Abs(dx);
        if (goHome && dx != 0f)
        {
            int direction = Math.Sign(dx);
            simulation = simulation with { DirectionX = direction, SpriteDirection = direction };
        }

        bool nearHorizontal = absDx < 80f;
        bool stopHorizontal = nearHorizontal || haltMovement;
        if (state == -1f)
        {
            dx = 5f;
            speed = 5.35f;
            stopHorizontal = false;
        }

        if (stopHorizontal)
        {
            velocityX *= 0.9f;
            if (velocityX is > -0.1f and < 0.1f)
                velocityX = 0f;
        }
        else
        {
            int direction = Math.Sign(dx);
            float desired = direction * speed;
            velocityX += (desired - velocityX) / horizontalLerpDivisor;
        }

        const int collisionWidth = 40;
        const int collisionHeight = 20;
        float collisionX = centerX - collisionWidth * 0.5f;
        float collisionY = positionY + hitbox.Height - collisionHeight;
        bool spansTargetHorizontally = collisionX < targetLeft && collisionX + hitbox.Width > targetLeft + targetWidth;
        bool targetBelowProbe = collisionY + collisionHeight < targetTop + targetHeight - 16f;
        bool teleportState = state == 7f;
        bool acceptTopSurfaces = positionY + hitbox.Height >= targetTop && !teleportState;
        bool solid20 = environment.SolidCollision(collisionX, collisionY, collisionWidth, collisionHeight, acceptTopSurfaces);
        bool solid16 = environment.SolidCollision(collisionX, collisionY, collisionWidth, collisionHeight - 4, acceptTopSurfaces);
        int directionX = NormalizeDirection(simulation.DirectionX);
        bool forwardClear = !environment.SolidCollision(collisionX + collisionWidth * directionX, collisionY, 16, 80, acceptTopSurfaces);
        const float jumpVelocity = 8f;

        if (solid20 || solid16)
            localAi = localAi with { Ai0 = 0f };
        if (teleportState)
            velocityY = -0.1f;

        if ((spansTargetHorizontally || nearHorizontal) && targetBelowProbe)
        {
            velocityY = Math.Clamp(velocityY + downwardAcceleration * 2f, 0.001f, 16f);
        }
        else if (solid20 && !solid16)
        {
            velocityY = 0f;
        }
        else if (solid20)
        {
            velocityY = Math.Clamp(velocityY + upwardAcceleration, minimumUpwardVelocity, 0f);
        }
        else if (velocityY == 0f && forwardClear)
        {
            velocityY = -jumpVelocity;
            localAi = localAi with { Ai0 = 1f };
        }
        else
        {
            velocityY = Math.Clamp(velocityY + downwardAcceleration, -jumpVelocity, 16f);
        }
    }

    private static bool ShouldRunAway(
        bool hasTarget,
        in VanillaNpcTargetCandidate target,
        float npcCenterX,
        float npcCenterY,
        int homeX,
        int homeY,
        bool isChasing,
        IVanillaDeerclopsEnvironment environment)
    {
        if (!hasTarget)
            return true;

        bool zoneSnow = environment.IsPlayerInSnow(target.CenterX, target.CenterY);
        float homeCenterX = homeX * 16f;
        float homeCenterY = homeY * 16f;
        zoneSnow |= Distance(target.CenterX, target.CenterY, homeCenterX, homeCenterY) <= 480f;
        return target.Dead || (!isChasing && !zoneSnow) || Distance(npcCenterX, npcCenterY, target.CenterX, target.CenterY) >= 2400f;
    }

    private static bool TryGetCurrentTarget(
        ushort targetSlot,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate target)
    {
        if (targetSlot < byte.MaxValue &&
            context.TryFindCandidate((byte)targetSlot, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            return true;
        }

        target = default;
        return false;
    }

    private static bool TryRefreshClosest(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        ref ushort targetSlot,
        ref NpcSimulationState simulation,
        out VanillaNpcTargetCandidate target)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh refresh) &&
            refresh.HasTarget &&
            refresh.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)refresh.Target, out target))
        {
            targetSlot = refresh.Target;
            simulation = simulation with
            {
                DirectionX = refresh.DirectionX,
                DirectionY = refresh.DirectionY,
                SpriteDirection = refresh.DirectionX
            };
            return true;
        }

        target = default;
        return false;
    }

    private static void TryRefreshClosestAtCurrentPosition(
        in NpcSnapshot sourceNpc,
        float positionX,
        float positionY,
        in VanillaNpcHitboxSize hitbox,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        ref ushort targetSlot,
        ref NpcSimulationState simulation,
        out VanillaNpcTargetCandidate target,
        out bool hasTarget)
    {
        var staged = new NpcSnapshot(
            sourceNpc.Handle,
            sourceNpc.Revision,
            sourceNpc.Type,
            sourceNpc.NetId,
            positionX,
            positionY,
            0f,
            0f,
            targetSlot,
            sourceNpc.Ai,
            simulation);
        hasTarget = TryRefreshClosest(in staged, in definition, context, ref targetSlot, ref simulation, out target);
        if (!hasTarget)
            hasTarget = TryGetCurrentTarget(targetSlot, context, out target);
    }

    private static NpcAiState ResetAttack(in NpcAiState ai, float state) =>
        ai with { Ai0 = state, Ai1 = 0f };

    private static float Distance(float ax, float ay, float bx, float by)
    {
        float dx = bx - ax;
        float dy = by - ay;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static int NormalizeDirection(int direction) => direction < 0 ? -1 : 1;
}
