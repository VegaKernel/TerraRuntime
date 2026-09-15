using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// TerrariaServer 1.4.5.8 AI_011 state and accepted RedHat effects for Skeletron Head.
/// The executor owns irreversible effects; this strategy retains source phase and pre-motion anchors.
/// </summary>
internal sealed class VanillaSkeletronHeadNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public IVanillaSkeletronEnvironment? Environment { get; set; }

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
        if (definition.AiStyle != VanillaNpcAiStyles.SkeletronHead || npc.TypeIdentity != VanillaNpcIds.SkeletronHead ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox))
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        bool initialized = ai.Ai0 == 0f;
        if (initialized)
        {
            if (context.TrySelectClosestTarget(in npc, in definition, out var refresh) && refresh.HasTarget)
                targetSlot = refresh.Target;
            ai = ai with { Ai0 = 1f };
        }

        bool targetValid = TryGetHeadTarget(in npc, context, ref targetSlot, out var target, out bool refreshed);
        if (initialized || refreshed) FaceTarget(in npc, hitbox, in target, ref simulation);
        if (!targetValid)
        {
            ai = ai with { Ai1 = 3f };
        }
        else if (context.DayTime && ai.Ai1 is not 2f and not 3f)
        {
            ai = ai with { Ai1 = 2f };
        }

        bool redHat = VanillaSkeletronCombat.HasRedHatAdjustments(npc.TypeIdentity, ai, simulation.LocalAi);
        int handCount = context.ExpertMode ? context.CountNpcPeers(VanillaNpcIds.SkeletronHand) : 0;
        int defense = (simulation.BaseDefense ?? definition.Defense) + (context.ExpertMode ? handCount * 25 : 0);
        int baseDamage = simulation.BaseDamage ?? definition.Damage;
        int? damageOverride = simulation.DamageOverride ?? baseDamage;
        bool reflectsProjectiles = false;
        int timeLeft = simulation.TimeLeft;
        float? rotation = simulation.Rotation;

        switch ((int)ai.Ai1)
        {
            case 0:
                damageOverride = redHat ? (int)(baseDamage * 1.3d) : baseDamage;
                rotation = velocityX / 15f;
                StepHover(in npc, in target, hitbox, context.ExpertMode, context.GoodWorld, redHat, ref ai, ref velocityX, ref velocityY);
                if (ai.Ai1 == 1f && context.TrySelectClosestTarget(in npc, in definition, out var hoverTarget) &&
                    hoverTarget.HasTarget && context.TryFindCandidate((byte)hoverTarget.Target, out var facedTarget))
                {
                    targetSlot = hoverTarget.Target;
                    FaceTarget(in npc, hitbox, in facedTarget, ref simulation);
                }
                break;

            case 1:
                defense -= 10;
                StepSpin(in npc, in target, hitbox, context.ExpertMode, context.GoodWorld, redHat, handCount, ref ai, ref velocityX, ref velocityY);
                // NPC.GetAttackDamage_LerpBetweenFinalValues reads the NPC's retained spawn difficulty.
                float blend = Math.Clamp((simulation.SpawnDifficulty ?? 1f) - 1f, 0f, 1f);
                damageOverride = (int)(baseDamage + (baseDamage * 1.3f - baseDamage) * blend);
                if (redHat) damageOverride = (int)(damageOverride.Value * 1.3d);
                reflectsProjectiles = (context.GoodWorld || redHat) && handCount > 0;
                rotation = (rotation ?? 0f) + simulation.DirectionX * .3f;
                break;

            case 2:
                defense = 9999;
                damageOverride = 9999;
                rotation = (rotation ?? 0f) + simulation.DirectionX * .3f;
                SetVelocityToward(in npc, in target, hitbox, 8f, ref velocityX, ref velocityY);
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
            DefenseOverride = defense,
            DamageOverride = damageOverride,
            ReflectsProjectiles = reflectsProjectiles,
            TimeLeft = timeLeft,
            Rotation = rotation
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
        VanillaNpcHitboxSize hitbox,
        bool expertMode,
        bool goodWorld,
        bool redHat,
        ref NpcAiState ai,
        ref float velocityX,
        ref float velocityY)
    {
        float timer = ai.Ai2 + 1f;
        if (redHat) timer += .5f;
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
        if (redHat)
        {
            verticalAcceleration *= 1.35f;
            verticalMaximum *= 1.35f;
            horizontalAcceleration *= 1.35f;
            horizontalMaximum *= 1.35f;
        }
        else if (goodWorld)
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

        float centerX = npc.PositionX + hitbox.Width * 0.5f;
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
        VanillaNpcHitboxSize hitbox,
        bool expertMode,
        bool goodWorld,
        bool redHat,
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

        float dx = target.CenterX - (npc.PositionX + hitbox.Width * 0.5f);
        float dy = target.CenterY - (npc.PositionY + hitbox.Height * 0.5f);
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= 0f) distance = 1f;
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
        if (redHat)
            speed *= 1.4f;
        else if (goodWorld)
            speed *= 1.3f;

        float multiplier = speed / distance;
        velocityX = dx * multiplier;
        velocityY = dy * multiplier;
    }

    private static void SetVelocityToward(
        in NpcSnapshot npc,
        in VanillaNpcTargetCandidate target,
        VanillaNpcHitboxSize hitbox,
        float speed,
        ref float velocityX,
        ref float velocityY)
    {
        float dx = target.CenterX - (npc.PositionX + hitbox.Width * 0.5f);
        float dy = target.CenterY - (npc.PositionY + hitbox.Height * 0.5f);
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= 0f) distance = 1f;
        float multiplier = speed / distance;
        velocityX = dx * multiplier;
        velocityY = dy * multiplier;
    }

    internal static bool TryGetHeadTarget(in NpcSnapshot npc, VanillaNpcBehaviorContext context,
        ref ushort targetSlot, out VanillaNpcTargetCandidate target, out bool refreshed)
    {
        refreshed = false;
        if (targetSlot < byte.MaxValue && context.TryFindCandidate((byte)targetSlot, out target) &&
            target.Active && !target.Dead && !target.Ghost && !Far(in npc, in target)) return true;
        refreshed = true;
        if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition) &&
            context.TrySelectClosestTarget(in npc, in definition, out var selected) && selected.HasTarget &&
            context.TryFindCandidate((byte)selected.Target, out target))
        {
            targetSlot = selected.Target;
            return target.Active && !target.Dead && !target.Ghost && !Far(in npc, in target);
        }
        target = default;
        return false;
    }

    private static bool Far(in NpcSnapshot npc, in VanillaNpcTargetCandidate target) =>
        MathF.Abs(npc.PositionX - (target.CenterX - PlayerWidth * .5f)) > 2000f ||
        MathF.Abs(npc.PositionY - (target.CenterY - PlayerHeight * .5f)) > 2000f;

    private static void FaceTarget(in NpcSnapshot npc, VanillaNpcHitboxSize hitbox,
        in VanillaNpcTargetCandidate target, ref NpcSimulationState simulation)
    {
        if (target.Dead || (target.NoAggro && simulation.DirectionX != 0)) return;
        int x = (int)(target.CenterX - PlayerWidth * .5f) + (int)PlayerWidth / 2;
        int y = (int)(target.CenterY - PlayerHeight * .5f) + (int)PlayerHeight / 2;
        simulation = simulation with
        {
            DirectionX = x < npc.PositionX + hitbox.Width / 2 ? -1 : 1,
            DirectionY = y < npc.PositionY + hitbox.Height / 2 ? -1 : 1
        };
    }

    public void ApplyEffects(in NpcSnapshot before, in NpcSnapshot committed, VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random, INpcAiCommittedNpcMutationSink mutations)
    {
        if (context.DayTime || committed.Ai.Ai1 == 3f) return;
        bool redHat = VanillaSkeletronCombat.HasRedHatAdjustments(before.TypeIdentity, before.Ai, before.Simulation.LocalAi);
        if (before.Ai.Ai1 == 0f && redHat && committed.Ai.Ai1 == 1f && committed.Ai.Ai2 == 0f)
        {
            int taunt = random.NextInt32(2, 6);
            mutations.TryAnnounceSkeletronTaunt(in committed, taunt);
        }
        if (before.Ai.Ai1 != 1f || !(context.GoodWorld || redHat) || Environment is null ||
            before.Ai.Ai2 % 200f != 0f ||
            (!redHat && context.ExpertMode && context.CountNpcPeers(VanillaNpcIds.SkeletronHand) != 0) ||
            context.CountNpcPeers(VanillaNpcIds.DarkCaster) >= (redHat ? 4 : 6) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var hitbox)) return;
        if (!Environment.TryFindCasterSpawn(before.PositionX + hitbox.Width * .5f,
                before.PositionY + hitbox.Height * .5f, random, out int x, out int y)) return;
        if (!mutations.TryGetActive(committed.Handle.Slot, out var current) ||
            current.Handle != committed.Handle || current.Revision != committed.Revision) return;
        // NewNPC's own Good World draw remains part of allocation, including a full table.
        mutations.TrySpawn(in committed, new(VanillaNpcIds.DarkCaster, x, y, 0, 0, byte.MaxValue), out _);
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
        if (definition.AiStyle != VanillaNpcAiStyles.SkeletronHand || npc.TypeIdentity != VanillaNpcIds.SkeletronHand ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox))
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation with { SpriteDirection = -(int)npc.Ai.Ai0 };
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        if (ai.Ai1 < 0f || ai.Ai1 > byte.MaxValue ||
            !context.TryFindNpcPeer((byte)ai.Ai1, out NpcSnapshot parent) ||
            !VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, out var parentDefinition) ||
            parentDefinition.AiStyle != VanillaNpcAiStyles.SkeletronHead ||
            !parentDefinition.TryResolveHitbox(parent.Simulation, out var parentHitbox))
        {
            float orphanTimer = ai.Ai2 + 10f;
            ai = ai with { Ai2 = orphanTimer };
            if (orphanTimer > 50f)
                simulation = simulation with { Life = 0 };
            // Invalid parents leave localAI intact; the retained RedHat marker still owns contact damage.
            if (VanillaSkeletronCombat.HasRedHatAdjustments(npc.TypeIdentity, ai, simulation.LocalAi))
                simulation = simulation with { DamageOverride = (int)((simulation.BaseDamage ?? definition.Damage) * 1.3f) };
            next = Build(in npc, velocityX, velocityY, targetSlot, in ai, in simulation);
            return true;
        }

        simulation = simulation with { LocalAi = simulation.LocalAi with { Ai3 = parent.Ai.Ai3 } };

        // AI_012 inherits the RedHat marker from the parent; this is independent of world difficulty.
        bool redHat = VanillaSkeletronCombat.HasRedHatAdjustments(npc.TypeIdentity, ai, simulation.LocalAi);
        if (redHat)
            simulation = simulation with { DamageOverride = (int)((simulation.BaseDamage ?? definition.Damage) * 1.3f) };

        if (!VanillaSkeletronHeadNpcBehaviorStrategy.TryGetTarget(in npc, context, ref targetSlot, out VanillaNpcTargetCandidate target))
            target = default;

        int timeLeft = simulation.TimeLeft;

        int state = (int)ai.Ai2;
        if (state is 0 or 3)
        {
            if (parent.Ai.Ai1 == 3f && (timeLeft < 0 || timeLeft > 10))
                timeLeft = 10;
            if (parent.Ai.Ai1 != 0f && !redHat)
            {
                StepHoverToParent(in npc, in parent, hitbox, parentHitbox, ai.Ai0, -100f, -120f, 0.07f, 6f, 0.1f, 8f, ref velocityX, ref velocityY);
            }
            else
            {
                float timer = ai.Ai3 + 1f;
                if (redHat) timer += 1f;
                if (context.ExpertMode) timer += 0.5f;
                if (timer >= 300f)
                {
                    state++;
                    timer = 0f;
                }
                ai = ai with { Ai2 = state, Ai3 = timer };

                // Expert source executes the same positioning block once in its Expert branch and once again in the
                // shared branch below it. Apply the same duplicate acceleration instead of collapsing it.
                if (context.ExpertMode)
                    StepHoverToParent(in npc, in parent, hitbox, parentHitbox, ai.Ai0, 230f, -200f, 0.04f, 3f, 0.07f, 8f, ref velocityX, ref velocityY);
                StepHoverToParent(in npc, in parent, hitbox, parentHitbox, ai.Ai0, 230f, -200f, 0.04f, 3f, 0.07f, 8f, ref velocityX, ref velocityY);
            }
        }
        else if (state == 1)
        {
            velocityX *= 0.95f;
            velocityY -= 0.1f;
            if (redHat)
            {
                velocityY -= 0.09f;
                velocityY = MathF.Max(velocityY, -15f);
            }
            else if (context.ExpertMode)
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
                RefreshDashTarget(in npc, in definition, context, ref targetSlot, ref target, ref simulation);
                ai = ai with { Ai2 = 2f };
                SetVelocityToward(in npc, in target, hitbox, redHat ? 24f : context.ExpertMode ? 21f : 18f, ref velocityX, ref velocityY);
            }
        }
        else if (state == 2)
        {
            if (!target.Active || target.Dead || target.Ghost ||
                npc.PositionY > target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f ||
                DotPastTarget(in npc, in target, hitbox, velocityX, velocityY) ||
                DistanceToTarget(in npc, in target, hitbox) > 2000f ||
                velocityY < 0f)
            {
                ai = ai with { Ai2 = 3f };
            }
        }
        else if (state == 4)
        {
            velocityY *= 0.95f;
            velocityX += 0.1f * -ai.Ai0;
            if (redHat)
            {
                velocityX += 0.1f * -ai.Ai0;
                velocityX = Math.Clamp(velocityX, -15f, 15f);
            }
            else if (context.ExpertMode)
            {
                velocityX += 0.07f * -ai.Ai0;
                velocityX = Math.Clamp(velocityX, -12f, 12f);
            }
            else
            {
                velocityX = Math.Clamp(velocityX, -8f, 8f);
            }

            float centerX = npc.PositionX + hitbox.Width / 2;
            float parentCenterX = parent.PositionX + parentHitbox.Width / 2;
            if ((centerX < parentCenterX - 500f || centerX > parentCenterX + 500f) &&
                target.Active && !target.Dead && !target.Ghost)
            {
                RefreshDashTarget(in npc, in definition, context, ref targetSlot, ref target, ref simulation);
                ai = ai with { Ai2 = 5f };
                SetVelocityToward(in npc, in target, hitbox, redHat ? 25f : context.ExpertMode ? 22f : 17f, ref velocityX, ref velocityY);
            }
        }
        else if (state == 5 &&
                 (!target.Active || target.Dead || target.Ghost ||
                  (velocityX > 0f && npc.PositionX + hitbox.Width / 2 > target.CenterX) ||
                  (velocityX < 0f && npc.PositionX + hitbox.Width / 2 < target.CenterX) ||
                  DotPastTarget(in npc, in target, hitbox, velocityX, velocityY) ||
                  DistanceToTarget(in npc, in target, hitbox) > 2000f))
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

    private static void RefreshDashTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, ref ushort targetSlot, ref VanillaNpcTargetCandidate target,
        ref NpcSimulationState simulation)
    {
        // AI_012 calls TargetClosest when a wind-up turns into a dash, even if the old target is valid.
        if (context.TrySelectClosestTarget(in npc, in definition, out var refresh) && refresh.HasTarget &&
            refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out var selected))
        {
            targetSlot = refresh.Target;
            target = selected;
            simulation = simulation with { DirectionX = refresh.DirectionX, DirectionY = refresh.DirectionY };
        }
    }

    private static void StepHoverToParent(
        in NpcSnapshot npc,
        in NpcSnapshot parent,
        VanillaNpcHitboxSize hitbox,
        VanillaNpcHitboxSize parentHitbox,
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

        float targetX = parent.PositionX + parentHitbox.Width / 2 + offsetXMultiplier * side;
        float centerX = npc.PositionX + hitbox.Width / 2;
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
        VanillaNpcHitboxSize hitbox,
        float speed,
        ref float velocityX,
        ref float velocityY)
    {
        float dx = target.CenterX - (npc.PositionX + hitbox.Width * 0.5f);
        float dy = target.CenterY - (npc.PositionY + hitbox.Height * 0.5f);
        float distance = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        float multiplier = speed / distance;
        velocityX = dx * multiplier;
        velocityY = dy * multiplier;
    }

    private static bool DotPastTarget(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcHitboxSize hitbox, float velocityX, float velocityY)
    {
        float dx = target.CenterX - (npc.PositionX + hitbox.Width * 0.5f);
        float dy = target.CenterY - (npc.PositionY + hitbox.Height * 0.5f);
        return velocityX * dx + velocityY * dy <= 0f;
    }

    private static float DistanceToTarget(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcHitboxSize hitbox)
    {
        float dx = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f - (npc.PositionX + hitbox.Width * 0.5f);
        float dy = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f - (npc.PositionY + hitbox.Height * 0.5f);
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
