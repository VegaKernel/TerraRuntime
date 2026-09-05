using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// TerrariaServer 1.4.5.8 AI_011 gameplay state for Skeletron Head. Presentation-only rotation, dust, sounds and
/// RedHat/dual-seed extras are intentionally outside this slice; ordinary/Expert hover-spin timing, daytime enrage,
/// target-loss flee, hand-count defense and source-owned hand lifecycle are authoritative.
/// </summary>
internal sealed class VanillaSkeletronHeadNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const float PlayerWidth = VanillaPlayerHitboxFacts.BaseWidth;
    private const float PlayerHeight = VanillaPlayerHitboxFacts.BaseHeight;

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.SkeletronHead || npc.TypeIdentity != VanillaNpcIds.SkeletronHead)
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        if (ai.Ai0 == 0f)
            ai = ai with { Ai0 = 1f };

        if (!TryGetTarget(in npc, context, ref targetSlot, out VanillaNpcTargetCandidate target) ||
            MathF.Abs(npc.PositionX - (target.CenterX - PlayerWidth * 0.5f)) > 2000f ||
            MathF.Abs(npc.PositionY - (target.CenterY - PlayerHeight * 0.5f)) > 2000f)
        {
            ai = ai with { Ai1 = 3f };
        }
        else if (context.DayTime && ai.Ai1 is not 2f and not 3f)
        {
            ai = ai with { Ai1 = 2f };
        }

        int handCount = context.CountNpcPeers(VanillaNpcIds.SkeletronHand);
        int defense = definition.Defense + (context.ExpertMode ? handCount * 25 : 0);
        int? damageOverride = definition.Damage;
        bool reflectsProjectiles = false;
        int timeLeft = simulation.TimeLeft;

        switch ((int)ai.Ai1)
        {
            case 0:
                StepHover(in npc, in target, context.ExpertMode, context.GoodWorld, ref ai, ref velocityX, ref velocityY);
                break;

            case 1:
                defense -= 10;
                StepSpin(in npc, in target, context.ExpertMode, context.GoodWorld, handCount, ref ai, ref velocityX, ref velocityY);
                // GetAttackDamage_LerpBetweenFinalValues remains a separate difficulty-scaling contract; keep the
                // definition contact damage rather than guessing that helper's world-strength projection.
                damageOverride = definition.Damage;
                break;

            case 2:
                defense = 9999;
                damageOverride = 9999;
                SetVelocityToward(in npc, in target, 8f, ref velocityX, ref velocityY);
                break;

            case 3:
                velocityY += 0.1f;
                if (velocityY < 0f)
                    velocityY *= 0.95f;
                velocityX *= 0.95f;
                if (timeLeft < 0 || timeLeft > 50)
                    timeLeft = 50;
                break;
        }

        simulation = simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            DefenseOverride = defense,
            DamageOverride = damageOverride,
            ReflectsProjectiles = reflectsProjectiles,
            TimeLeft = timeLeft,
            JustHit = false
        };
        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            velocityX,
            velocityY,
            targetSlot,
            ai,
            simulation);
        return true;
    }

    private static void StepHover(
        in NpcSnapshot npc,
        in VanillaNpcTargetCandidate target,
        bool expertMode,
        bool goodWorld,
        ref NpcAiState ai,
        ref float velocityX,
        ref float velocityY)
    {
        float timer = ai.Ai2 + 1f;
        if (timer >= 800f)
        {
            timer = 0f;
            ai = ai with { Ai1 = 1f };
        }
        ai = ai with { Ai2 = timer };

        float verticalAcceleration = expertMode ? 0.03f : 0.02f;
        float verticalMaximum = expertMode ? 4f : 2f;
        float horizontalAcceleration = expertMode ? 0.07f : 0.05f;
        float horizontalMaximum = expertMode ? 9.5f : 8f;
        if (goodWorld)
        {
            verticalAcceleration += 0.01f;
            verticalMaximum += 1f;
            horizontalAcceleration += 0.05f;
            horizontalMaximum += 2f;
        }

        float targetTop = target.CenterY - PlayerHeight * 0.5f - 250f;
        if (npc.PositionY > targetTop)
        {
            if (velocityY > 0f)
                velocityY *= 0.98f;
            velocityY -= verticalAcceleration;
            if (velocityY > verticalMaximum)
                velocityY = verticalMaximum;
        }
        else if (npc.PositionY < targetTop)
        {
            if (velocityY < 0f)
                velocityY *= 0.98f;
            velocityY += verticalAcceleration;
            if (velocityY < -verticalMaximum)
                velocityY = -verticalMaximum;
        }

        float centerX = npc.PositionX + 40f;
        if (centerX > target.CenterX)
        {
            if (velocityX > 0f)
                velocityX *= 0.98f;
            velocityX -= horizontalAcceleration;
            if (velocityX > horizontalMaximum)
                velocityX = horizontalMaximum;
        }
        else if (centerX < target.CenterX)
        {
            if (velocityX < 0f)
                velocityX *= 0.98f;
            velocityX += horizontalAcceleration;
            if (velocityX < -horizontalMaximum)
                velocityX = -horizontalMaximum;
        }
    }

    private static void StepSpin(
        in NpcSnapshot npc,
        in VanillaNpcTargetCandidate target,
        bool expertMode,
        bool goodWorld,
        int handCount,
        ref NpcAiState ai,
        ref float velocityX,
        ref float velocityY)
    {
        float timer = ai.Ai2 + 1f;
        if (timer >= 400f)
        {
            timer = 0f;
            ai = ai with { Ai1 = 0f };
        }
        ai = ai with { Ai2 = timer };

        float dx = target.CenterX - (npc.PositionX + 40f);
        float dy = target.CenterY - (npc.PositionY + 51f);
        float distance = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        float speed = 1.5f;
        if (expertMode)
        {
            speed = 3.5f;
            if (distance > 150f)
                speed *= 1.05f;
            for (float threshold = 200f; threshold <= 600f; threshold += 50f)
            {
                if (distance > threshold)
                    speed *= 1.1f;
            }
            if (handCount == 0)
                speed *= 1.1f;
            else if (handCount == 1)
                speed *= 1.05f;
        }
        if (goodWorld)
            speed *= 1.3f;

        velocityX = dx / distance * speed;
        velocityY = dy / distance * speed;
    }

    private static void SetVelocityToward(
        in NpcSnapshot npc,
        in VanillaNpcTargetCandidate target,
        float speed,
        ref float velocityX,
        ref float velocityY)
    {
        float dx = target.CenterX - (npc.PositionX + 40f);
        float dy = target.CenterY - (npc.PositionY + 51f);
        float distance = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        velocityX = dx / distance * speed;
        velocityY = dy / distance * speed;
    }

    internal static bool TryGetTarget(
        in NpcSnapshot npc,
        VanillaNpcBehaviorContext context,
        ref ushort targetSlot,
        out VanillaNpcTargetCandidate target)
    {
        if (targetSlot < byte.MaxValue &&
            context.TryFindCandidate((byte)targetSlot, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            return true;
        }

        if (context.TrySelectClosestTarget(in npc, VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition) ? definition : default, out VanillaBlueSlimeTargetRefresh refresh) &&
            refresh.HasTarget &&
            refresh.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)refresh.Target, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            targetSlot = refresh.Target;
            return true;
        }

        target = default;
        return false;
    }
}

/// <summary>
/// TerrariaServer 1.4.5.8 AI_012 gameplay state for Skeletron Hand. Parent ownership is the exact NPC slot stored in
/// ai[1]. The two alternating attack cycles, Expert accelerations and parent-loss teardown are authoritative.
/// </summary>
internal sealed class VanillaSkeletronHandNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.SkeletronHand || npc.TypeIdentity != VanillaNpcIds.SkeletronHand)
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        if (ai.Ai1 < 0f || ai.Ai1 > byte.MaxValue ||
            !context.TryFindNpcPeer((byte)ai.Ai1, out NpcSnapshot parent) ||
            parent.TypeIdentity != VanillaNpcIds.SkeletronHead)
        {
            float orphanTimer = ai.Ai2 + 10f;
            ai = ai with { Ai2 = orphanTimer };
            if (orphanTimer > 50f)
                simulation = simulation with { Life = 0, TimeLeft = 0 };
            next = Build(in npc, velocityX, velocityY, targetSlot, in ai, in simulation);
            return true;
        }

        if (!VanillaSkeletronHeadNpcBehaviorStrategy.TryGetTarget(in npc, context, ref targetSlot, out VanillaNpcTargetCandidate target))
            target = default;

        int timeLeft = simulation.TimeLeft;
        if (parent.Ai.Ai1 == 3f && (timeLeft < 0 || timeLeft > 10))
            timeLeft = 10;

        int state = (int)ai.Ai2;
        if (state is 0 or 3)
        {
            if (parent.Ai.Ai1 != 0f)
            {
                StepHoverToParent(in npc, in parent, ai.Ai0, -100f, -120f, 0.07f, 6f, 0.1f, 8f, ref velocityX, ref velocityY);
            }
            else
            {
                float timer = ai.Ai3 + 1f + (context.ExpertMode ? 0.5f : 0f);
                if (timer >= 300f)
                {
                    state++;
                    timer = 0f;
                }
                ai = ai with { Ai2 = state, Ai3 = timer };

                // Expert source executes the same positioning block once in its Expert branch and once again in the
                // shared branch below it. Apply the same duplicate acceleration instead of collapsing it.
                if (context.ExpertMode)
                    StepHoverToParent(in npc, in parent, ai.Ai0, 230f, -200f, 0.04f, 3f, 0.07f, 8f, ref velocityX, ref velocityY);
                StepHoverToParent(in npc, in parent, ai.Ai0, 230f, -200f, 0.04f, 3f, 0.07f, 8f, ref velocityX, ref velocityY);
            }
        }
        else if (state == 1)
        {
            velocityX *= 0.95f;
            velocityY -= 0.1f;
            if (context.ExpertMode)
            {
                velocityY -= 0.06f;
                velocityY = MathF.Max(velocityY, -13f);
            }
            else
            {
                velocityY = MathF.Max(velocityY, -8f);
            }

            if (npc.PositionY < parent.PositionY - 200f && target.Active && !target.Dead && !target.Ghost)
            {
                ai = ai with { Ai2 = 2f };
                SetVelocityToward(in npc, in target, context.ExpertMode ? 21f : 18f, ref velocityX, ref velocityY);
            }
        }
        else if (state == 2)
        {
            if (!target.Active || target.Dead || target.Ghost ||
                npc.PositionY > target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f ||
                DotPastTarget(in npc, in target, velocityX, velocityY) ||
                DistanceToTarget(in npc, in target) > 2000f ||
                velocityY < 0f)
            {
                ai = ai with { Ai2 = 3f };
            }
        }
        else if (state == 4)
        {
            velocityY *= 0.95f;
            velocityX += 0.1f * -ai.Ai0;
            if (context.ExpertMode)
            {
                velocityX += 0.07f * -ai.Ai0;
                velocityX = Math.Clamp(velocityX, -12f, 12f);
            }
            else
            {
                velocityX = Math.Clamp(velocityX, -8f, 8f);
            }

            float centerX = npc.PositionX + definition.Width * 0.5f;
            float parentCenterX = parent.PositionX + 40f;
            if ((centerX < parentCenterX - 500f || centerX > parentCenterX + 500f) &&
                target.Active && !target.Dead && !target.Ghost)
            {
                ai = ai with { Ai2 = 5f };
                SetVelocityToward(in npc, in target, context.ExpertMode ? 22f : 17f, ref velocityX, ref velocityY);
            }
        }
        else if (state == 5 &&
                 (!target.Active || target.Dead || target.Ghost ||
                  (velocityX > 0f && npc.PositionX + definition.Width * 0.5f > target.CenterX) ||
                  (velocityX < 0f && npc.PositionX + definition.Width * 0.5f < target.CenterX) ||
                  DotPastTarget(in npc, in target, velocityX, velocityY) ||
                  DistanceToTarget(in npc, in target) > 2000f))
        {
            ai = ai with { Ai2 = 0f };
        }

        simulation = simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            TimeLeft = timeLeft,
            JustHit = false
        };
        next = Build(in npc, velocityX, velocityY, targetSlot, in ai, in simulation);
        return true;
    }

    private static void StepHoverToParent(
        in NpcSnapshot npc,
        in NpcSnapshot parent,
        float side,
        float offsetY,
        float offsetXMultiplier,
        float verticalAcceleration,
        float verticalMaximum,
        float horizontalAcceleration,
        float horizontalMaximum,
        ref float velocityX,
        ref float velocityY)
    {
        float targetY = parent.PositionY + offsetY;
        if (npc.PositionY > targetY)
        {
            if (velocityY > 0f)
                velocityY *= 0.96f;
            velocityY -= verticalAcceleration;
            if (velocityY > verticalMaximum)
                velocityY = verticalMaximum;
        }
        else if (npc.PositionY < targetY)
        {
            if (velocityY < 0f)
                velocityY *= 0.96f;
            velocityY += verticalAcceleration;
            if (velocityY < -verticalMaximum)
                velocityY = -verticalMaximum;
        }

        float targetX = parent.PositionX + 40f + offsetXMultiplier * side;
        float centerX = npc.PositionX + 26f;
        if (centerX > targetX)
        {
            if (velocityX > 0f)
                velocityX *= 0.96f;
            velocityX -= horizontalAcceleration;
            if (velocityX > horizontalMaximum)
                velocityX = horizontalMaximum;
        }
        else if (centerX < targetX)
        {
            if (velocityX < 0f)
                velocityX *= 0.96f;
            velocityX += horizontalAcceleration;
            if (velocityX < -horizontalMaximum)
                velocityX = -horizontalMaximum;
        }
    }

    private static void SetVelocityToward(
        in NpcSnapshot npc,
        in VanillaNpcTargetCandidate target,
        float speed,
        ref float velocityX,
        ref float velocityY)
    {
        float dx = target.CenterX - (npc.PositionX + 26f);
        float dy = target.CenterY - (npc.PositionY + 26f);
        float distance = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        velocityX = dx / distance * speed;
        velocityY = dy / distance * speed;
    }

    private static bool DotPastTarget(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, float velocityX, float velocityY)
    {
        float dx = target.CenterX - (npc.PositionX + 26f);
        float dy = target.CenterY - (npc.PositionY + 26f);
        return velocityX * dx + velocityY * dy <= 0f;
    }

    private static float DistanceToTarget(in NpcSnapshot npc, in VanillaNpcTargetCandidate target)
    {
        float dx = target.CenterX - (npc.PositionX + 26f);
        float dy = target.CenterY - (npc.PositionY + 26f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static NpcStateUpdate Build(
        in NpcSnapshot npc,
        float velocityX,
        float velocityY,
        ushort target,
        in NpcAiState ai,
        in NpcSimulationState simulation) =>
        new(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            velocityX,
            velocityY,
            target,
            ai,
            simulation);
}
