using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Server-authoritative TerrariaServer 1.4.5.8 aiStyle 51/52/53 gameplay slice for Plantera.
/// It owns the two-phase root steering/timers and the linked hook/tentacle motion. Terrain-anchor selection,
/// presentation effects and client-only branches remain outside this bounded slice.
/// </summary>
internal sealed class VanillaPlanteraNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        return npc.TypeIdentity switch
        {
            var type when type == VanillaNpcIds.Plantera => TryStepRoot(in npc, in definition, context, out next),
            var type when type == VanillaNpcIds.PlanteraHook => TryStepHook(in npc, in definition, context, out next),
            var type when type == VanillaNpcIds.PlanteraTentacle => TryStepTentacle(in npc, in definition, context, out next),
            var type when type == VanillaNpcIds.PlanteraSpore => TryStepSpore(in npc, in definition, context, out next),
            _ => Fail(out next)
        };
    }

    private static bool TryStepRoot(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Plantera)
            return Fail(out next);

        ushort targetSlot = npc.Target;
        if (!TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target) &&
            !TryRefresh(in npc, in definition, context, ref targetSlot, out target))
        {
            NpcAiState aiForRetreat = npc.Ai;
            NpcSimulationState retreat = npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = true,
                TimeLeft = npc.Simulation.TimeLeft is < 0 or > 10 ? 10 : npc.Simulation.TimeLeft
            };
            next = Build(in npc, npc.VelocityX, npc.VelocityY - 0.1f, targetSlot, in aiForRetreat, in retreat);
            return true;
        }

        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
        int life = sim.LifeMax > 0 ? sim.Life : lifeMax;
        bool phaseTwo = life <= lifeMax / 2;

        // localAI[0] follows the source bootstrap/phase marker: 0 -> 1 after hooks, 1 -> 2 after tentacles.
        if (local.Ai0 == 0f)
            local = local with { Ai0 = 1f };
        if (phaseTwo && local.Ai0 == 1f)
            local = local with { Ai0 = 2f };

        float anchorX = npc.PositionX + definition.Width * 0.5f;
        float anchorY = npc.PositionY + definition.Height * 0.5f;
        if (context.TryGetAverageOwnedNpcPeerCenter(VanillaNpcIds.PlanteraHook, npc.Handle.Slot, out float hookX, out float hookY))
        {
            anchorX = hookX;
            anchorY = hookY;
        }

        float speed = phaseTwo ? 5f : 2.5f;
        float acceleration = phaseTwo ? 0.05f : 0.025f;
        if (life < lifeMax / 4)
            speed = 7f;
        if (context.ExpertMode)
        {
            speed = (speed + 1f) * 1.1f;
            acceleration = (acceleration + 0.01f) * 1.1f;
        }
        if (context.GoodWorld)
        {
            speed *= 1.15f;
            acceleration *= 1.15f;
        }

        float maxAnchorDistance = 500f + (context.ExpertMode ? 150f : 0f);
        float fromAnchorX = target.CenterX - anchorX;
        float fromAnchorY = target.CenterY - anchorY;
        ClampLength(ref fromAnchorX, ref fromAnchorY, maxAnchorDistance);
        float desiredCenterX = anchorX + fromAnchorX;
        float desiredCenterY = anchorY + fromAnchorY;
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float desiredVx = desiredCenterX - centerX;
        float desiredVy = desiredCenterY - centerY;
        NormalizeToMax(ref desiredVx, ref desiredVy, speed, npc.VelocityX, npc.VelocityY);

        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        ApproachVector(ref vx, ref vy, desiredVx, desiredVy, acceleration);

        float attackTimer = local.Ai1 + 1f;
        if (!phaseTwo)
        {
            if (life < lifeMax * .9f) attackTimer += 1f;
            if (life < lifeMax * .8f) attackTimer += 1f;
            if (life < lifeMax * .7f) attackTimer += 1f;
            if (life < lifeMax * .6f) attackTimer += 1f;
            if (context.ExpertMode) attackTimer += 1f;
            if (context.GoodWorld) attackTimer += 1f;
            if (attackTimer > 80f)
                attackTimer = 0f; // projectile planner observes the wrap.
        }
        else
        {
            if (life < lifeMax * .4f) attackTimer += 1f;
            if (life < lifeMax * .3f) attackTimer += 1f;
            if (life < lifeMax * .2f) attackTimer += 1f;
            if (life < lifeMax * .1f) attackTimer += 1f;
            if (attackTimer >= 350f)
                attackTimer = 0f; // spore planner observes the wrap.
        }
        local = local with { Ai1 = attackTimer };

        sim = sim with
        {
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = local,
            DefenseOverride = phaseTwo ? 10 : 36,
            DamageOverride = phaseTwo ? 70 : 50,
            JustHit = false
        };
        NpcAiState aiForBuild = npc.Ai;
        next = Build(in npc, vx, vy, targetSlot, in aiForBuild, in sim);
        return true;
    }

    private static bool TryStepHook(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.PlanteraHook ||
            !TryOwnedRoot(in npc, context, out NpcSnapshot root))
            return Fail(out next);

        NpcAiState ai = npc.Ai;
        // The clean-room runtime stores a stable fallback anchor in ai[0..1] at spawn. World-aware re-anchoring is
        // deliberately tracked separately; this keeps the authoritative child attached instead of generic-fallback.
        float targetX = ai.Ai0 != 0f ? ai.Ai0 * 16f - 8f : root.PositionX + 43f;
        float targetY = ai.Ai1 != 0f ? ai.Ai1 * 16f - 8f : root.PositionY + 43f;
        float cx = npc.PositionX + definition.Width * 0.5f;
        float cy = npc.PositionY + definition.Height * 0.5f;
        float dx = targetX - cx;
        float dy = targetY - cy;
        float speed = root.Simulation.LifeMax > 0 && root.Simulation.Life < root.Simulation.LifeMax / 2 ? 8f : 6f;
        if (root.Simulation.LifeMax > 0 && root.Simulation.Life < root.Simulation.LifeMax / 4) speed = 10f;
        if (context.ExpertMode) speed += root.Simulation.LifeMax > 0 && root.Simulation.Life < root.Simulation.LifeMax / 2 ? 2f : 1f;
        SetOrClamp(ref dx, ref dy, speed);

        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true, DontTakeDamage = true };
        next = Build(in npc, dx, dy, root.Target, in ai, in sim);
        return true;
    }

    private static bool TryStepTentacle(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.PlanteraTentacle ||
            !TryOwnedRoot(in npc, context, out NpcSnapshot root))
            return Fail(out next);

        NpcSnapshot parent = root;
        int encodedParent = (int)npc.Ai.Ai3 - 1;
        if (encodedParent >= 0 && encodedParent <= byte.MaxValue && context.TryFindNpcPeer((byte)encodedParent, out NpcSnapshot hook))
            parent = hook;

        NpcAiState ai = npc.Ai;
        // Source tentacles continually choose a local offset around their root/hook. Keep the offset server-owned in
        // ai[0..1] and use deterministic initial values when the spawn planner did not provide one.
        float ox = ai.Ai0 == 0f ? ((npc.Handle.Slot % 5) - 2) * 35f : ai.Ai0;
        float oy = ai.Ai1 == 0f ? (((npc.Handle.Slot / 5) % 5) - 2) * 35f : ai.Ai1;
        ai = ai with { Ai0 = ox, Ai1 = oy };
        float parentCx = parent.PositionX + 20f;
        float parentCy = parent.PositionY + 20f;
        float cx = npc.PositionX + definition.Width * 0.5f;
        float cy = npc.PositionY + definition.Height * 0.5f;
        float desiredX = parentCx + ox - cx;
        float desiredY = parentCy + oy - cy;
        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float acceleration = context.ExpertMode ? 0.5f : 0.2f;
        ApproachVector(ref vx, ref vy, desiredX, desiredY, acceleration);
        float maxSpeed = context.GoodWorld ? 12f : 8f;
        ClampLength(ref vx, ref vy, maxSpeed);

        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true };
        next = Build(in npc, vx, vy, root.Target, in ai, in sim);
        return true;
    }

    private static bool TryStepSpore(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.PlanteraSpore)
            return Fail(out next);
        ushort targetSlot = npc.Target;
        if (!TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target) &&
            !TryRefresh(in npc, in definition, context, ref targetSlot, out target))
            return Fail(out next);
        float vx = npc.VelocityX;
        float vy = MathF.Min(1f, npc.VelocityY + .02f);
        float left = npc.PositionX + definition.Width < target.CenterX ? 1f : -1f;
        if (vx * left < 0f) vx *= context.ExpertMode ? .9604f : .98f;
        vx += left * (context.ExpertMode ? .2f : .1f);
        if (MathF.Abs(vx) > 5f) vx *= .97f;
        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true };
        NpcAiState aiForBuild = npc.Ai;
        next = Build(in npc, vx, vy, targetSlot, in aiForBuild, in sim);
        return true;
    }

    private static bool TryGetTarget(ushort slot, VanillaNpcBehaviorContext context, out VanillaNpcTargetCandidate target)
    {
        if (slot < byte.MaxValue && context.TryFindCandidate((byte)slot, out target) && target.Active && !target.Dead && !target.Ghost)
            return true;
        target = default;
        return false;
    }

    private static bool TryRefresh(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        ref ushort slot, out VanillaNpcTargetCandidate target)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh refresh) &&
            refresh.HasTarget && refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            slot = refresh.Target;
            return true;
        }
        target = default;
        return false;
    }

    private static void NormalizeToMax(ref float x, ref float y, float speed, float fallbackX, float fallbackY)
    {
        float d = MathF.Sqrt(x * x + y * y);
        if (d < speed)
        {
            x = fallbackX;
            y = fallbackY;
            return;
        }
        float factor = speed / MathF.Max(.001f, d);
        x *= factor;
        y *= factor;
    }

    private static void SetOrClamp(ref float x, ref float y, float speed)
    {
        float d = MathF.Sqrt(x * x + y * y);
        if (d <= speed + 12f)
            return;
        float factor = speed / MathF.Max(.001f, d);
        x *= factor;
        y *= factor;
    }

    private static void ApproachVector(ref float vx, ref float vy, float tx, float ty, float acceleration)
    {
        Approach(ref vx, tx, acceleration);
        Approach(ref vy, ty, acceleration);
        if (vx < 0f && tx > 0f) Approach(ref vx, tx, acceleration * 2f);
        else if (vx > 0f && tx < 0f) Approach(ref vx, tx, acceleration * 2f);
        if (vy < 0f && ty > 0f) Approach(ref vy, ty, acceleration * 2f);
        else if (vy > 0f && ty < 0f) Approach(ref vy, ty, acceleration * 2f);
    }

    private static void Approach(ref float value, float target, float amount)
    {
        if (value < target) value = MathF.Min(value + amount, target);
        else if (value > target) value = MathF.Max(value - amount, target);
    }

    private static void ClampLength(ref float x, ref float y, float max)
    {
        float d = MathF.Sqrt(x * x + y * y);
        if (d <= max || d <= .001f) return;
        float factor = max / d;
        x *= factor;
        y *= factor;
    }

    private static NpcStateUpdate Build(in NpcSnapshot npc, float vx, float vy, ushort target, in NpcAiState ai, in NpcSimulationState sim) =>
        new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);

    private static bool TryOwnedRoot(in NpcSnapshot child, VanillaNpcBehaviorContext context, out NpcSnapshot root)
    {
        int encoded = (int)child.Simulation.LocalAi.Ai3 - 1;
        if (encoded >= 0 && encoded <= byte.MaxValue &&
            context.TryFindNpcPeer((byte)encoded, out root) &&
            root.TypeIdentity == VanillaNpcIds.Plantera)
            return true;
        return context.TryFindFirstNpcPeer(VanillaNpcIds.Plantera, out root);
    }

    private static bool Fail(out NpcStateUpdate next) { next = default; return false; }
}
