using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>TerrariaServer 1.4.5.8 aiStyle 32 gameplay state for Skeletron Prime.</summary>
internal sealed class VanillaSkeletronPrimeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.SkeletronPrime || npc.TypeIdentity != VanillaNpcIds.SkeletronPrime ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox))
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        float rotation = simulation.Rotation ?? 0f;
        int direction = simulation.DirectionX;
        ushort targetSlot = npc.Target;
        if (ai.Ai0 == 0f)
        {
            if (TryRefresh(in npc, in definition, context, ref targetSlot, out VanillaNpcTargetCandidate initialTarget))
                direction = FaceTarget(in npc, hitbox, in initialTarget);
            ai = ai with { Ai0 = 1f };
        }
        bool hasTarget = TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target);
        if (!hasTarget || MathF.Abs(npc.PositionX - (target.CenterX - 10f)) > 6000f || MathF.Abs(npc.PositionY - (target.CenterY - 21f)) > 6000f)
        {
            if (TryRefresh(in npc, in definition, context, ref targetSlot, out target))
                direction = FaceTarget(in npc, hitbox, in target);
            hasTarget = TryGetTarget(targetSlot, context, out target);
            if (!hasTarget || MathF.Abs(npc.PositionX - (target.CenterX - 10f)) > 6000f || MathF.Abs(npc.PositionY - (target.CenterY - 21f)) > 6000f)
                ai = ai with { Ai1 = 3f };
        }
        if (context.DayTime && ai.Ai1 is not 2f and not 3f) ai = ai with { Ai1 = 2f };

        int defense = simulation.BaseDefense ?? definition.Defense;
        int damage = simulation.BaseDamage ?? definition.Damage;
        int timeLeft = simulation.TimeLeft;
        switch ((int)ai.Ai1)
        {
            case 0:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 600f)
                {
                    ai = ai with { Ai2 = 0f, Ai1 = 1f };
                    if (TryRefresh(in npc, in definition, context, ref targetSlot, out target))
                        direction = FaceTarget(in npc, hitbox, in target);
                    hasTarget = TryGetTarget(targetSlot, context, out target);
                }
                rotation = vx / 15f;
                if (hasTarget) Hover(in npc, in target, hitbox, context.ExpertMode, ref vx, ref vy);
                break;
            case 1:
                defense *= 2; damage *= 2;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 400f) ai = ai with { Ai2 = 0f, Ai1 = 0f };
                rotation += direction * 0.3f;
                if (hasTarget) Charge(in npc, in target, hitbox, context.ExpertMode, ref vx, ref vy);
                break;
            case 2:
                defense = 9999; damage = 9999;
                rotation += direction * 0.3f;
                if (hasTarget) Rage(in npc, in target, hitbox, ref vx, ref vy);
                break;
            case 3:
                if (timeLeft < 0 || timeLeft > 500) timeLeft = 500;
                rotation += direction * 0.3f;
                vy += 0.1f; if (vy < 0f) vy *= 0.95f; vx *= 0.95f;
                break;
        }
        simulation = simulation with { NoGravity = true, NoTileCollide = true, DefenseOverride = defense, DamageOverride = damage, ReflectsProjectiles = false, TimeLeft = timeLeft, JustHit = false, DirectionX = direction, Rotation = rotation };
        next = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, targetSlot, ai, simulation);
        return true;
    }

    private static void Hover(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcHitboxSize hitbox, bool expert, ref float vx, ref float vy)
    {
        float va = expert ? 0.03f : 0.1f, vm = expert ? 4f : 2f, ha = expert ? 0.07f : 0.1f, hm = expert ? 9.5f : 8f;
        float targetTop = target.CenterY - 21f;
        if (npc.PositionY > targetTop - 200f) { if (vy > 0f) vy *= 0.98f; vy -= va; if (vy > vm) vy = vm; }
        else if (npc.PositionY < targetTop - 500f) { if (vy < 0f) vy *= 0.98f; vy += va; if (vy < -vm) vy = -vm; }
        float cx = npc.PositionX + hitbox.Width * .5f;
        if (cx > target.CenterX + 100f) { if (vx > 0f) vx *= 0.98f; vx -= ha; if (vx > hm) vx = hm; }
        if (cx < target.CenterX - 100f) { if (vx < 0f) vx *= 0.98f; vx += ha; if (vx < -hm) vx = -hm; }
    }

    private static void Charge(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcHitboxSize hitbox, bool expert, ref float vx, ref float vy)
    {
        float dx = target.CenterX - (npc.PositionX + hitbox.Width * .5f), dy = target.CenterY - (npc.PositionY + hitbox.Height * .5f);
        float d = (float)Math.Sqrt(OperatingSystem.IsWindows() ? (double)dx * dx + (double)dy * dy : dx * dx + dy * dy); if (d <= 0f) d = 1f;
        float speed = expert ? 6f : 2f;
        if (expert) { if (d > 150f) speed *= 1.05f; for (float t = 200f; t <= 600f; t += 50f) if (d > t) speed *= 1.1f; }
        float multiplier = speed / d;
        vx = dx * multiplier; vy = dy * multiplier;
    }

    private static void Rage(in NpcSnapshot npc, in VanillaNpcTargetCandidate target, VanillaNpcHitboxSize hitbox, ref float vx, ref float vy)
    {
        float dx = target.CenterX - (npc.PositionX + hitbox.Width * .5f);
        float dy = target.CenterY - (npc.PositionY + hitbox.Height * .5f);
        float distance = (float)Math.Sqrt(OperatingSystem.IsWindows() ? (double)dx * dx + (double)dy * dy : dx * dx + dy * dy);
        if (distance <= 0f) distance = 1f;
        // AI_032 stores the Windows wider sum as Single before clamping and normalizing.
        float speed = OperatingSystem.IsWindows() ? (float)(10d + distance / 100d) : 10f + distance / 100f;
        float multiplier = Math.Clamp(speed, 8f, 32f) / distance;
        vx = dx * multiplier; vy = dy * multiplier;
    }

    internal static bool TryGetTarget(ushort slot, VanillaNpcBehaviorContext context, out VanillaNpcTargetCandidate target)
    { if (slot<byte.MaxValue && context.TryFindCandidate((byte)slot,out target)&&target.Active&&!target.Dead&&!target.Ghost) return true; target=default; return false; }
    internal static bool TryRefresh(in NpcSnapshot npc,in VanillaNpcDefinition def,VanillaNpcBehaviorContext context,ref ushort slot,out VanillaNpcTargetCandidate target)
    { if(context.TrySelectClosestTarget(in npc,in def,out VanillaBlueSlimeTargetRefresh r)&&r.HasTarget&&r.Target<byte.MaxValue&&context.TryFindCandidate((byte)r.Target,out target)&&target.Active&&!target.Dead&&!target.Ghost){slot=r.Target;return true;} target=default;return false; }

    private static int FaceTarget(in NpcSnapshot npc, VanillaNpcHitboxSize hitbox, in VanillaNpcTargetCandidate target) =>
        target.CenterX < npc.PositionX + hitbox.Width * 0.5f ? -1 : 1;
}
