using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Server-authoritative TerrariaServer 1.4.5.8 AI_037 Destroyer slice. Head movement uses the
/// source worm steering law and the runtime worm collision query; body/tail slots are linked by ai[1],
/// share root identity in ai[3], and terminate when their predecessor disappears. Chain allocation and
/// body Death Laser side effects are planned after a successful source commit.
/// </summary>
internal sealed class VanillaDestroyerNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom _random;
    private IVanillaWormEnvironment? _environment;

    public VanillaDestroyerNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        _random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetEnvironment(IVanillaWormEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.Destroyer ||
            (npc.TypeIdentity != VanillaNpcIds.Destroyer && npc.TypeIdentity != VanillaNpcIds.DestroyerBody && npc.TypeIdentity != VanillaNpcIds.DestroyerTail) ||
            _environment is null || !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        float x = npc.PositionX, y = npc.PositionY, vx = npc.VelocityX, vy = npc.VelocityY;
        ushort targetSlot = npc.Target;

        if (npc.TypeIdentity != VanillaNpcIds.Destroyer)
        {
            if (!TryResolveParent(in npc, context, out NpcSnapshot parent))
            {
                sim = sim with { Life = 0, TimeLeft = 0 };
                next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
                return true;
            }

            if (!VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, parent.NetIdentity, out VanillaNpcDefinition parentDef) ||
                !parentDef.TryResolveHitbox(parent.Simulation.Scale, out VanillaNpcHitboxSize parentHitbox))
            { next = default; return false; }

            // realLife ownership: every linked segment mirrors the head's authoritative life.
            if (float.IsFinite(ai.Ai3) && ai.Ai3 >= 0f && ai.Ai3 < byte.MaxValue && ai.Ai3 == MathF.Truncate(ai.Ai3) &&
                context.TryFindNpcPeer((byte)ai.Ai3, out NpcSnapshot root) && root.TypeIdentity == VanillaNpcIds.Destroyer)
                sim = sim with { Life = root.Simulation.Life };

            float cx = x + hitbox.Width * .5f, cy = y + hitbox.Height * .5f;
            float px = parent.PositionX + parentHitbox.Width * .5f, py = parent.PositionY + parentHitbox.Height * .5f;
            float dx = px - cx, dy = py - cy;
            float distance = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
            float gap = 44f * npc.Simulation.Scale;
            float ratio = (distance - gap) / distance;
            x += dx * ratio; y += dy * ratio; vx = 0f; vy = 0f;

            if (npc.TypeIdentity == VanillaNpcIds.DestroyerBody)
            {
                float laserCounter = local.Ai0 + _random.NextInt32(0, 4);
                if (laserCounter >= _random.NextInt32(1400, 26000)) laserCounter = 0f;
                local = local with { Ai0 = laserCounter };
            }
            sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, JustHit = false };
            next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
            return true;
        }

        if (ai.Ai0 == 0f && ai.Ai3 == 0f)
            ai = ai with { Ai3 = npc.Handle.Slot };

        if (!TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target))
            TryRefresh(in npc, in definition, context, ref targetSlot, out target);
        bool hasTarget = TryGetTarget(targetSlot, context, out target);
        bool digging = _environment.IsDigging(x, y, hitbox.Width, hitbox.Height);
        local = local with { Ai1 = digging ? 0f : 1f };

        if (context.DayTime || !hasTarget)
        {
            digging = false;
            vy += 1f;
            if (y > (float)context.WorldSurfacePixels) vy += 1f;
            vy = MathF.Min(vy, 32f);
            int tl = sim.TimeLeft;
            if (tl < 0 || tl > 10) tl = 10;
            sim = sim with { TimeLeft = tl, NoGravity = true, NoTileCollide = true, LocalAi = local };
            next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
            return true;
        }

        float maxSpeed = 16f;
        float turn = context.GoodWorld ? .12f : .1f;
        float align = context.GoodWorld ? .18f : .15f;
        float cxh = MathF.Truncate((x + hitbox.Width * .5f) / 16f) * 16f;
        float cyh = MathF.Truncate((y + hitbox.Height * .5f) / 16f) * 16f;
        float tx = MathF.Truncate(target.CenterX / 16f) * 16f;
        float ty = MathF.Truncate(target.CenterY / 16f) * 16f;
        float dxh = tx - cxh, dyh = ty - cyh;
        float dist = MathF.Max(.001f, MathF.Sqrt(dxh * dxh + dyh * dyh));

        if (!digging)
        {
            vy += .15f;
            vy = MathF.Min(vy, maxSpeed);
            if (MathF.Abs(vx) + MathF.Abs(vy) < maxSpeed * .4f)
                vx += vx < 0f ? -turn * 1.1f : turn * 1.1f;
            else if (vy >= maxSpeed)
                Approach(ref vx, dxh, turn);
            else if (vy > 4f)
                vx += vx < 0f ? turn * .9f : -turn * .9f;
        }
        else
        {
            float inv = maxSpeed / dist;
            float desiredX = dxh * inv, desiredY = dyh * inv;
            bool sameX = (vx > 0f && desiredX > 0f) || (vx < 0f && desiredX < 0f);
            bool sameY = (vy > 0f && desiredY > 0f) || (vy < 0f && desiredY < 0f);
            if (sameX && sameY)
            { Approach(ref vx, desiredX, align); Approach(ref vy, desiredY, align); }
            else if (sameX || sameY)
            {
                Approach(ref vx, desiredX, turn); Approach(ref vy, desiredY, turn);
                if (MathF.Abs(desiredY) < maxSpeed*.2f && MathF.Sign(vx) != MathF.Sign(desiredX)) vy += vy > 0f ? turn*2f : -turn*2f;
                if (MathF.Abs(desiredX) < maxSpeed*.2f && MathF.Sign(vy) != MathF.Sign(desiredY)) vx += vx > 0f ? turn*2f : -turn*2f;
            }
            else if (MathF.Abs(dxh) > MathF.Abs(dyh))
            { Approach(ref vx, desiredX, turn*1.1f); if (MathF.Abs(vx)+MathF.Abs(vy)<maxSpeed*.5f) vy += vy>0f?turn:-turn; }
            else
            { Approach(ref vy, desiredY, turn*1.1f); if (MathF.Abs(vx)+MathF.Abs(vy)<maxSpeed*.5f) vx += vx>0f?turn:-turn; }
        }

        sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, JustHit = false };
        next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
        return true;
    }

    private static bool TryResolveParent(in NpcSnapshot npc, VanillaNpcBehaviorContext context, out NpcSnapshot parent)
    {
        if (float.IsFinite(npc.Ai.Ai1) && npc.Ai.Ai1 >= 0f && npc.Ai.Ai1 < byte.MaxValue && npc.Ai.Ai1 == MathF.Truncate(npc.Ai.Ai1) &&
            context.TryFindNpcPeer((byte)npc.Ai.Ai1, out parent) && parent.Simulation.Life > 0 &&
            (parent.TypeIdentity == VanillaNpcIds.Destroyer || parent.TypeIdentity == VanillaNpcIds.DestroyerBody)) return true;
        parent = default; return false;
    }
    private static bool TryGetTarget(ushort slot, VanillaNpcBehaviorContext c, out VanillaNpcTargetCandidate t)
    { if(slot<byte.MaxValue&&c.TryFindCandidate((byte)slot,out t)&&t.Active&&!t.Dead&&!t.Ghost)return true;t=default;return false; }
    private static bool TryRefresh(in NpcSnapshot n,in VanillaNpcDefinition d,VanillaNpcBehaviorContext c,ref ushort slot,out VanillaNpcTargetCandidate t)
    { if(c.TrySelectClosestTarget(in n,in d,out VanillaBlueSlimeTargetRefresh r)&&r.HasTarget&&r.Target<byte.MaxValue&&c.TryFindCandidate((byte)r.Target,out t)&&t.Active&&!t.Dead&&!t.Ghost){slot=r.Target;return true;}t=default;return false; }
    private static void Approach(ref float value,float desired,float amount){if(value<desired)value=MathF.Min(value+amount,desired);else if(value>desired)value=MathF.Max(value-amount,desired);}
}
