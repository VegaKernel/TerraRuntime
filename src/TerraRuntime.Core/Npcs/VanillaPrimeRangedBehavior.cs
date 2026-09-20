using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

/// <summary>Source-ordered AI_035/036 state and accepted projectile effects for Terraria 1.4.5.8.</summary>
internal sealed class VanillaPrimeRangedBehavior : IVanillaNpcBehaviorStrategy
{
    /// <summary>
    /// AI_035/036 explicitly writes <c>NPC.netUpdate</c> when their source-owned hover/attack clocks
    /// change phase. Projectile emission itself does not set that flag.
    /// </summary>
    internal static bool RequiresImmediateSync(in NpcSnapshot before, in NpcStateUpdate proposed)
    {
        if (proposed.Type != before.Type) return false;
        if (before.TypeIdentity != VanillaNpcIds.PrimeCannon && before.TypeIdentity != VanillaNpcIds.PrimeLaser) return false;
        bool cannon = before.TypeIdentity == VanillaNpcIds.PrimeCannon;

        float phase = before.Ai.Ai2;
        if (cannon)
        {
            return (phase == 0f && before.Ai.Ai3 >= 1099f && proposed.Ai.Ai2 == 1f && proposed.Ai.Ai3 == 0f) ||
                (phase == 1f && before.Ai.Ai3 >= 299f && proposed.Ai.Ai2 == 0f && proposed.Ai.Ai3 == 0f) ||
                (phase == 1f && VanillaSkeletronPrimeNpcBehaviorStrategy.RequiresTargetTrackingSync(in before, in proposed));
        }

        return ((phase == 0f || phase == 3f) && before.Ai.Ai3 >= 799f &&
                proposed.Ai.Ai2 == phase + 1f && proposed.Ai.Ai3 == 0f) ||
            (phase == 1f && before.Ai.Ai3 >= 199f && proposed.Ai.Ai2 == 0f && proposed.Ai.Ai3 == 0f) ||
            ((phase == 0f || phase == 3f || phase == 1f) &&
             VanillaSkeletronPrimeNpcBehaviorStrategy.RequiresTargetTrackingSync(in before, in proposed));
    }

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        next = default;
        bool cannon = npc.TypeIdentity == VanillaNpcIds.PrimeCannon;
        if ((!cannon && npc.TypeIdentity != VanillaNpcIds.PrimeLaser) || !definition.TryResolveHitbox(npc.Simulation, out var size)) return false;
        var ai = npc.Ai;
        var sim = npc.Simulation with { SpriteDirection = -(int)ai.Ai0 };
        float vx = npc.VelocityX, vy = npc.VelocityY;
        ushort target = npc.Target;
        if (!TryParent(in npc, context, out var parent, out var parentSize))
        {
            ai = ai with { Ai2 = ai.Ai2 + 10f };
            if (ai.Ai2 > 50f) sim = sim with { Life = 0 };
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
            return true;
        }

        float phase = ai.Ai2, clock = sim.LocalAi.Ai0;
        float cx = npc.PositionX + size.Width * .5f, cy = npc.PositionY + size.Height * .5f;
        float parentCenter = parent.PositionX + parentSize.Width / 2;
        bool hover = phase == 0f || (!cannon && phase == 3f);
        if (hover)
        {
            if (parent.Ai.Ai1 == 3f && (sim.TimeLeft < 0 || sim.TimeLeft > 10)) sim = sim with { TimeLeft = 10 };
            if (parent.Ai.Ai1 != 0f)
            {
                clock += cannon ? 2f : 3f;
                HoverAxis(ref vy, npc.PositionY, parent.PositionY - 100f, parent.PositionY - 100f, .07f, 6f);
                float anchor = parentCenter - 120f * ai.Ai0;
                HoverAxis(ref vx, npc.PositionX + size.Width / 2, anchor, anchor, .1f, 8f);
            }
            else
            {
                ai = ai with { Ai3 = ai.Ai3 + 1f };
                if (ai.Ai3 >= (cannon ? 1100f : 800f))
                {
                    ai = ai with { Ai2 = cannon ? 1f : ai.Ai2 + 1f, Ai3 = 0f };
                    if (cannon) clock = 0f;
                }
                float y = parent.PositionY - (cannon ? 150f : 100f);
                HoverAxis(ref vy, npc.PositionY, y, y, cannon ? .04f : .1f, 3f);
                float low = cannon ? parentCenter + 160f : parentCenter - 180f * ai.Ai0;
                float high = cannon ? parentCenter + 200f : low;
                HoverAxis(ref vx, npc.PositionX + size.Width / 2, low, high, cannon ? .2f : .14f, 8f);
            }
        }
        else if (phase == 1f)
        {
            ai = ai with { Ai3 = ai.Ai3 + 1f };
            if (ai.Ai3 >= (cannon ? 300f : 200f))
            {
                ai = ai with { Ai2 = 0f, Ai3 = 0f };
                clock = 0f;
            }
            // Movement consumes the retained player before TargetClosest below.
            if (target < byte.MaxValue && context.TryFindCandidate((byte)target, out var previous))
            {
                float dx = (cannon ? parentCenter : previous.CenterX - 350f) - cx;
                float dy = previous.CenterY - (cannon ? 80f : 20f) - cy;
                Aim(ref dx, ref dy, cannon ? 6f : 7f);
                Pursue(ref vx, dx, cannon ? .04f : .1f);
                Pursue(ref vy, dy, cannon ? .08f : .03f);
            }
        }
        else
        {
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
            return true;
        }

        if (cannon && hover)
            sim = sim with { Rotation = (float)Math.Atan2(parent.PositionY + 230f - cy, parentCenter - 200f * ai.Ai0 - cx) + 1.57f };
        else if (context.TrySelectClosestTarget(in npc, in definition, out var refresh) && refresh.HasTarget &&
            refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out var player))
        {
            target = refresh.Target;
            int dx = sim.DirectionX, dy = sim.DirectionY;
            if (!player.Dead && (!player.NoAggro || dx == 0))
                dx = player.CenterX < npc.PositionX + size.Width / 2 ? -1 : 1;
            if (!player.Dead && (!player.NoAggro || dy == 0))
                dy = player.CenterY < npc.PositionY + size.Height / 2 ? -1 : 1;
            sim = sim with { DirectionX = dx, DirectionY = dy,
                Rotation = (float)Math.Atan2(player.CenterY - cy, player.CenterX - cx) - 1.57f };
        }
        clock += 1f;
        if (clock > Threshold(cannon, hover)) clock = 0f;
        sim = sim with { LocalAi = sim.LocalAi with { Ai0 = clock } };
        next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
        return true;
    }

    public void ApplyEffects(in NpcSnapshot before, in NpcSnapshot committed, VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random, INpcAiCommittedNpcMutationSink mutations)
    {
        if (!mutations.TryGetActive(committed.Handle.Slot, out var current) || current.Handle != committed.Handle ||
            current.Revision != committed.Revision ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var size)) return;
        if (!TryParent(in before, context, out var parent, out var parentSize))
        {
            // HitEffect still draws four gore choices on a dedicated server, although NewGore returns its sentinel.
            if (committed.Simulation.Life == 0 && committed.Ai.Ai2 > 50f && committed.Ai.Ai2 == before.Ai.Ai2 + 10f)
                for (int i = 0; i < 4; i++) random.NextInt32(61, 64);
            return;
        }
        bool cannon = before.TypeIdentity == VanillaNpcIds.PrimeCannon;
        bool hover = before.Ai.Ai2 == 0f || (!cannon && before.Ai.Ai2 == 3f);
        if (!hover && before.Ai.Ai2 != 1f) return;
        float clock = before.Simulation.LocalAi.Ai0;
        if (hover)
        {
            if (parent.Ai.Ai1 != 0f) clock += cannon ? 2f : 3f;
            else if (cannon && before.Ai.Ai3 + 1f >= 1100f) clock = 0f;
        }
        else if (before.Ai.Ai3 + 1f >= (cannon ? 300f : 200f)) clock = 0f;
        if (clock + 1f <= Threshold(cannon, hover) || committed.Simulation.LocalAi.Ai0 != 0f) return;

        float cx = before.PositionX + size.Width * .5f, cy = before.PositionY + size.Height * .5f;
        float dx, dy;
        if (cannon && hover)
        {
            dx = parent.PositionX + parentSize.Width / 2 - 200f * before.Ai.Ai0 - cx;
            dy = parent.PositionY + 230f - cy;
            Aim(ref dx, ref dy, -12f);
        }
        else
        {
            if (committed.Target >= byte.MaxValue || !context.TryFindCandidate((byte)committed.Target, out var target)) return;
            dx = target.CenterX - cx; dy = target.CenterY - cy;
            Aim(ref dx, ref dy, hover ? 8f : 10f);
        }
        float spread = cannon ? .01f : .05f;
        int rx = random.NextInt32(-40, 41), ry = random.NextInt32(-40, 41);
        dx = OperatingSystem.IsWindows() ? (float)(dx + rx * (double)spread) : dx + rx * spread;
        dy = OperatingSystem.IsWindows() ? (float)(dy + ry * (double)spread) : dy + ry * spread;
        var type = cannon ? VanillaProjectileIds.SkeletronPrimeBomb : VanillaProjectileIds.RetinazerDeathLaser;
        if (!VanillaDefinitionCatalog.TryGet(type, out var projectile)) return;
        float offset = cannon ? 4f : 8f;
        var intent = new NpcAiProjectileIntent(type, cx + dx * offset - projectile.Width * .5f,
            cy + dy * offset - projectile.Height * .5f, dx, dy, cannon ? 0 : 25, 0f);
        mutations.TrySpawnProjectile(in committed, in intent, out _);
    }

    private static float Threshold(bool cannon, bool hover) => cannon ? (hover ? 140f : 40f) : (hover ? 200f : 80f);

    private static bool TryParent(in NpcSnapshot npc, VanillaNpcBehaviorContext context,
        out NpcSnapshot parent, out VanillaNpcHitboxSize size)
    {
        parent = default; size = default;
        return npc.Ai.Ai1 is > -1f and < 200f && context.TryFindNpcPeer((byte)(int)npc.Ai.Ai1, out parent) &&
            VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, out var definition) &&
            definition.AiStyle == VanillaNpcAiStyles.SkeletronPrime && definition.TryResolveHitbox(parent.Simulation, out size);
    }

    private static void HoverAxis(ref float velocity, float position, float low, float high, float acceleration, float cap)
    {
        if (position > high)
        {
            if (velocity > 0f) velocity *= .96f;
            velocity -= acceleration;
            if (velocity > cap) velocity = cap;
        }
        if (position < low)
        {
            if (velocity < 0f) velocity *= .96f;
            velocity += acceleration;
            if (velocity < -cap) velocity = -cap;
        }
    }

    private static void Pursue(ref float velocity, float desired, float acceleration)
    {
        if (velocity > desired)
        {
            if (velocity > 0f) velocity *= .9f;
            velocity -= acceleration;
        }
        if (velocity < desired)
        {
            if (velocity < 0f) velocity *= .9f;
            velocity += acceleration;
        }
    }

    private static void Aim(ref float dx, ref float dy, float speed)
    {
        float distance = (float)Math.Sqrt(OperatingSystem.IsWindows() ? (double)dx * dx + (double)dy * dy : dx * dx + dy * dy);
        float multiplier = speed / distance;
        dx *= multiplier; dy *= multiplier;
    }
}
