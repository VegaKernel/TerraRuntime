using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Server-authoritative TerrariaServer 1.4.5.8 aiStyle 45-48 gameplay slice for Golem and its linked parts.
/// Child ownership is generation-safe via server-only localAI[3], avoiding the source global golemBoss index.
/// Presentation dust/gore/sound and Good World torch destruction are intentionally omitted.
/// </summary>
internal sealed class VanillaGolemNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity == VanillaNpcIds.Golem)
            return TryRoot(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.GolemHead)
            return TryHead(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.GolemFistLeft || npc.TypeIdentity == VanillaNpcIds.GolemFistRight)
            return TryFist(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.GolemHeadFree)
            return TryFreeHead(in npc, in definition, context, out next);
        next = default;
        return false;
    }

    private static bool TryRoot(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Golem) { next = default; return false; }
        ushort targetSlot = npc.Target;
        if (!TryTarget(in npc, in definition, context, ref targetSlot, out VanillaNpcTargetCandidate target))
        {
            NpcAiState aiForRetreat = npc.Ai;
            NpcSimulationState despawn = npc.Simulation with { NoTileCollide = true, TimeLeft = npc.Simulation.TimeLeft is < 0 or > 10 ? 10 : npc.Simulation.TimeLeft };
            next = Build(in npc, npc.VelocityX, npc.VelocityY + .2f, targetSlot, in aiForRetreat, in despawn);
            return true;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        bool initialized = local.Ai0 != 0f;
        if (!initialized) local = local with { Ai0 = 1f };

        bool attachedHeadAlive = context.HasOwnedNpcPeer(VanillaNpcIds.GolemHead, npc.Handle.Slot);
        bool freeHeadAlive = context.HasOwnedNpcPeer(VanillaNpcIds.GolemHeadFree, npc.Handle.Slot);
        if (initialized && !attachedHeadAlive && !freeHeadAlive && local.Ai2 == 0f)
            local = local with { Ai2 = 1f }; // spawn planner emits the detached head once.

        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float balance = 1f + (context.GoodWorld ? 2f : 0f);
        if (ai.Ai0 == 0f)
        {
            if (sim.CollideY || MathF.Abs(vy) < .001f)
            {
                vx *= .8f;
                float timer = ai.Ai1 + balance;
                if (!context.HasOwnedNpcPeer(VanillaNpcIds.GolemFistLeft, npc.Handle.Slot)) timer += 2f * balance;
                if (!context.HasOwnedNpcPeer(VanillaNpcIds.GolemFistRight, npc.Handle.Slot)) timer += 2f * balance;
                if (!attachedHeadAlive) timer += 2f * balance;
                int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
                int life = sim.LifeMax > 0 ? sim.Life : lifeMax;
                if (life < lifeMax) timer += balance;
                if (life < lifeMax / 2) timer += 4f * balance;
                if (life < lifeMax / 3) timer += 8f * balance;
                if (timer >= 300f)
                {
                    vx = 4f * (target.CenterX < npc.PositionX + 70f ? -1f : 1f);
                    vy = life < lifeMax ? MathF.Max(-19.1f, -12.1f * (balance + 9f) / 10f) : -12.1f;
                    ai = ai with { Ai0 = 1f, Ai1 = 0f };
                    sim = sim with { NoTileCollide = true };
                }
                else ai = ai with { Ai1 = timer };
            }
        }
        else
        {
            if (sim.CollideY && vy >= 0f)
            {
                ai = ai with { Ai0 = 0f, Ai1 = 0f };
                vy = 0f;
            }
            else
            {
                float cx = npc.PositionX + 70f;
                float maxX = 3f;
                int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
                int life = sim.LifeMax > 0 ? sim.Life : lifeMax;
                if (life < lifeMax) maxX += 1f;
                if (life < lifeMax / 2) maxX += 1f;
                if (life < lifeMax / 4) maxX += 1f;
                maxX *= (balance + 1f) / 2f;
                if (cx < target.CenterX - 10f) vx = MathF.Min(vx + .2f, maxX);
                else if (cx > target.CenterX + 10f) vx = MathF.Max(vx - .2f, -maxX);
            }
        }

        sim = sim with
        {
            LocalAi = local,
            DontTakeDamage = attachedHeadAlive,
            DamageOverride = definition.Damage,
            DefenseOverride = definition.Defense
        };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }

    private static bool TryHead(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!TryOwnedRoot(in npc, context, out NpcSnapshot root) || definition.AiStyle != VanillaNpcAiStyles.GolemHead)
        { next = default; return false; }
        ushort targetSlot = root.Target;
        if (!TryTarget(in root, GetGolemDefinition(), context, ref targetSlot, out _))
            targetSlot = npc.Target;
        NpcAiState ai = npc.Ai;
        NpcAiState local = npc.Simulation.LocalAi;
        int lifeMax = npc.Simulation.LifeMax > 0 ? npc.Simulation.LifeMax : definition.LifeMax;
        int life = npc.Simulation.LifeMax > 0 ? npc.Simulation.Life : lifeMax;
        ai = ai with { Ai0 = life < lifeMax / 2 ? 1f : 0f };
        float increment = context.GoodWorld ? 2f : 1f;
        if (ai.Ai0 == 1f)
        {
            if (life < lifeMax * .4f) increment += 1f;
            if (life < lifeMax * .2f) increment += 1f;
        }
        float shotTimer = ai.Ai1 + increment;
        if (shotTimer >= 300f) shotTimer = 0f;
        float fireballTimer = ai.Ai2 + increment;
        if (ai.Ai0 == 1f && fireballTimer >= 600f) fireballTimer = 0f;
        ai = ai with { Ai1 = shotTimer, Ai2 = fireballTimer };

        float desiredX = root.PositionX + 70f - 3f - (npc.PositionX + 35f);
        float desiredY = root.PositionY + 70f - 57f - (npc.PositionY + 35f);
        float d = MathF.Sqrt(desiredX * desiredX + desiredY * desiredY);
        if (d > 100f) { float f = 100f / d; desiredX *= f; desiredY *= f; }
        local = local with { Ai0 = shotTimer < 20f || shotTimer > 280f ? 1f : 0f };
        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true, LocalAi = local };
        next = Build(in npc, desiredX, desiredY, targetSlot, in ai, in sim);
        return true;
    }

    private static bool TryFist(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!TryOwnedRoot(in npc, context, out NpcSnapshot root) || definition.AiStyle != VanillaNpcAiStyles.GolemFist)
        { next = default; return false; }
        ushort targetSlot = root.Target;
        if (!TryTarget(in root, GetGolemDefinition(), context, ref targetSlot, out VanillaNpcTargetCandidate target))
        { next = default; return false; }
        NpcAiState ai = npc.Ai;
        float scale = npc.Simulation.Scale > 0f ? npc.Simulation.Scale : 1f;
        float rootCx = root.PositionX + 70f;
        float rootCy = root.PositionY + 70f;
        bool left = npc.TypeIdentity == VanillaNpcIds.GolemFistLeft;
        float homeX = rootCx + (left ? -84f : 78f) * scale;
        float homeY = rootCy - 9f * scale;
        float cx = npc.PositionX + definition.Width * .5f;
        float cy = npc.PositionY + definition.Height * .5f;
        float homeDx = homeX - cx;
        float homeDy = homeY - cy;
        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        bool noTile = true;
        if (ai.Ai0 == 0f)
        {
            float d = MathF.Sqrt(homeDx * homeDx + homeDy * homeDy);
            float speed = 14f;
            int lifeMax = npc.Simulation.LifeMax > 0 ? npc.Simulation.LifeMax : definition.LifeMax;
            int life = npc.Simulation.LifeMax > 0 ? npc.Simulation.Life : lifeMax;
            if (life < lifeMax / 2) speed += 3f;
            if (life < lifeMax / 4) speed += 3f;
            if (root.Simulation.LifeMax > 0 && root.Simulation.Life < root.Simulation.LifeMax) speed += 8f;
            speed = MathF.Min(32f, speed);
            if (d < 12f + speed)
            {
                vx = homeDx; vy = homeDy;
                float timer = ai.Ai1 + (root.Simulation.LifeMax > 0 && root.Simulation.Life < root.Simulation.LifeMax ? 11f : 1f);
                if (timer >= 60f)
                {
                    bool sideAllows = left ? cx + 100f > target.CenterX : cx - 100f < target.CenterX;
                    ai = ai with { Ai0 = sideAllows ? 1f : 0f, Ai1 = 0f };
                }
                else ai = ai with { Ai1 = timer };
            }
            else { float f = speed / MathF.Max(.001f, d); vx = homeDx * f; vy = homeDy * f; }
        }
        else if (ai.Ai0 == 1f)
        {
            vx = homeDx; vy = homeDy;
            float timer = ai.Ai1 + 1f;
            if (timer >= 30f)
            {
                float speed = root.Simulation.LifeMax > 0 && root.Simulation.Life < root.Simulation.LifeMax ? 22f : 12f;
                SetToward(cx, cy, target.CenterX, target.CenterY, speed, ref vx, ref vy);
                ai = ai with { Ai0 = 2f, Ai1 = 0f };
            }
            else ai = ai with { Ai1 = timer };
        }
        else if (ai.Ai0 == 2f)
        {
            float timer = ai.Ai1 + 1f;
            float dx = homeX - cx, dy = homeY - cy;
            bool passed = MathF.Abs(vx) > MathF.Abs(vy) ? (vx > 0 ? cx > target.CenterX : cx < target.CenterX) : (vy > 0 ? cy > target.CenterY : cy < target.CenterY);
            if (passed) noTile = false;
            if (dx * dx + dy * dy > 700f * 700f || npc.Simulation.CollideX || npc.Simulation.CollideY)
                ai = ai with { Ai0 = 0f, Ai1 = 0f };
            else ai = ai with { Ai1 = timer };
        }
        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = noTile };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }

    private static bool TryFreeHead(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!TryOwnedRoot(in npc, context, out NpcSnapshot root) || definition.AiStyle != VanillaNpcAiStyles.GolemHeadFree)
        { next = default; return false; }
        ushort targetSlot = root.Target;
        if (!TryTarget(in root, GetGolemDefinition(), context, ref targetSlot, out VanillaNpcTargetCandidate target))
        { next = default; return false; }
        float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;
        float dx = target.CenterX - cx, dy = target.CenterY - 300f - cy;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        dx = dx / d * 7f; dy = dy / d * 7f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        Approach(ref vx, dx, .05f); Approach(ref vy, dy, .05f);
        NpcAiState ai = npc.Ai;
        float timer = ai.Ai1 + 1f;
        if (timer >= 300f) timer = 0f;
        float fireball = ai.Ai2 + 1f;
        if (fireball >= 600f) fireball = 0f;
        ai = ai with { Ai1 = timer, Ai2 = fireball };
        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true, DontTakeDamage = true };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }

    private static VanillaNpcDefinition GetGolemDefinition()
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Golem, out VanillaNpcDefinition definition))
            throw new InvalidOperationException("Pinned Golem definition is missing.");
        return definition;
    }

    private static bool TryOwnedRoot(in NpcSnapshot child, VanillaNpcBehaviorContext context, out NpcSnapshot root)
    {
        int encoded = (int)child.Simulation.LocalAi.Ai3 - 1;
        if (encoded >= 0 && encoded <= byte.MaxValue && context.TryFindNpcPeer((byte)encoded, out root) && root.TypeIdentity == VanillaNpcIds.Golem)
            return true;
        return context.TryFindFirstNpcPeer(VanillaNpcIds.Golem, out root);
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        ref ushort targetSlot, out VanillaNpcTargetCandidate target)
    {
        if (targetSlot < byte.MaxValue && context.TryFindCandidate((byte)targetSlot, out target) && target.Active && !target.Dead && !target.Ghost)
            return true;
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh refresh) && refresh.HasTarget &&
            refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out target) && target.Active && !target.Dead && !target.Ghost)
        { targetSlot = refresh.Target; return true; }
        target = default; return false;
    }

    private static void SetToward(float x, float y, float tx, float ty, float speed, ref float vx, ref float vy)
    { float dx = tx - x, dy = ty - y, d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy)); vx = dx / d * speed; vy = dy / d * speed; }
    private static void Approach(ref float v, float target, float amount) { if (v < target) v = MathF.Min(v + amount, target); else if (v > target) v = MathF.Max(v - amount, target); }
    private static NpcStateUpdate Build(in NpcSnapshot npc, float vx, float vy, ushort target, in NpcAiState ai, in NpcSimulationState sim) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
}
