using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

/// <summary>Terraria 1.4.5.8 AI33/34 movement and attack cycles for Prime's melee arms.</summary>
internal sealed class VanillaSkeletronPrimeLimbNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    /// <summary>
    /// AI_033/034 write <c>NPC.netUpdate</c> for their timer and charge phase handoffs.
    /// Movement-only phase returns intentionally remain on the ordinary cadence.
    /// </summary>
    internal static bool RequiresImmediateSync(in NpcSnapshot before, in NpcStateUpdate proposed)
    {
        if (proposed.Type != before.Type) return false;
        bool saw = before.TypeIdentity == VanillaNpcIds.PrimeSaw;
        if (!saw && before.TypeIdentity != VanillaNpcIds.PrimeVice) return false;

        float phase = before.Ai.Ai2;
        if (saw)
        {
            return ((phase == 0f || phase == 3f) &&
                    ((before.Ai.Ai3 >= 599f && proposed.Ai.Ai2 == 0f && proposed.Ai.Ai3 == 0f) ||
                     (before.Ai.Ai3 >= 299f && proposed.Ai.Ai2 == phase + 1f && proposed.Ai.Ai3 == 0f))) ||
                (phase == 1f && proposed.Ai.Ai2 == 2f) ||
                (phase == 4f && before.Ai.Ai3 >= 599f && proposed.Ai.Ai2 == 0f && proposed.Ai.Ai3 == 0f);
        }

        return ((phase == 0f || phase == 3f) && before.Ai.Ai3 >= 599f && proposed.Ai.Ai3 == 0f &&
                (proposed.Ai.Ai2 == 0f || proposed.Ai.Ai2 == phase + 1f)) ||
            (phase == 1f && proposed.Ai.Ai2 == 2f) ||
            (phase == 4f && proposed.Ai.Ai2 == 5f);
    }

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        next = default;
        bool vice = npc.TypeIdentity == VanillaNpcIds.PrimeVice;
        if ((!vice && npc.TypeIdentity != VanillaNpcIds.PrimeSaw) || !definition.TryResolveHitbox(npc.Simulation, out var size)) return false;
        var ai = npc.Ai;
        var sim = npc.Simulation with { SpriteDirection = -(int)ai.Ai0 };
        float vx = npc.VelocityX, vy = npc.VelocityY;
        ushort target = npc.Target;
        if (ai.Ai1 is <= -1f or >= 200f || !context.TryFindNpcSlot((byte)(int)ai.Ai1, out var parent) ||
            !VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, out var parentDefinition) ||
            !parentDefinition.TryResolveHitbox(parent.Simulation, out var parentSize))
        {
            ai = ai with { Ai2 = ai.Ai2 + 10f };
            if (ai.Ai2 > 50f) sim = sim with { Life = 0, TimeLeft = 0 };
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
            return true;
        }
        float cx = npc.PositionX + size.Width * .5f, cy = npc.PositionY + size.Height * .5f;
        float px = parent.PositionX + parentSize.Width / 2;
        float dx = px - 200f * ai.Ai0 - cx, dy = parent.PositionY + 230f - cy;
        float distance = Length(dx, dy);
        // The return transition precedes dispatch: the newly selected phase runs this same call.
        if (ai.Ai2 != 99f) { if (distance > 800f) ai = ai with { Ai2 = 99f }; }
        else if (distance < 400f) ai = ai with { Ai2 = 0f };
        if (!parent.IsActive || parentDefinition.AiStyle != VanillaNpcAiStyles.SkeletronPrime)
        {
            ai = ai with { Ai2 = ai.Ai2 + 10f };
            // Dedicated-server AI keeps the arm active until the invalid-parent clock passes 50.
            if (ai.Ai2 > 50f) sim = sim with { Life = 0, TimeLeft = 0 };
        }

        float phase = ai.Ai2;
        if (phase == 99f)
        {
            Axis(ref vy, npc.PositionY, parent.PositionY, parent.PositionY, .1f, .1f, 8f, 8f, .96f);
            Axis(ref vx, npc.PositionX + size.Width / 2, px, px, .5f, .5f, 12f, 12f, .96f);
        }
        else if (phase == 0f || phase == 3f)
        {
            if (parent.Ai.Ai1 == 3f && (sim.TimeLeft < 0 || sim.TimeLeft > 10)) sim = sim with { TimeLeft = 10 };
            if (parent.Ai.Ai1 != 0f)
            {
                Refresh(in npc, in definition, context, size, ref target, ref sim);
                if (TryTarget(context, target, out var player))
                {
                    if (player.Dead) vy = MathF.Min(vy + .1f, 16f);
                    else
                    {
                        float tx = player.CenterX - cx, ty = player.CenterY - cy;
                        Aim(ref tx, ref ty, vice ? 12f : 7f);
                        if (vice)
                        {
                            if (MathF.Abs(vx) + MathF.Abs(vy) < 2f) { vx = tx; vy = ty; }
                            else { vx *= .97f; vy *= .97f; }
                        }
                        else { Pursue(ref vx, tx); Pursue(ref vy, ty); }
                    }
                    if (!vice || !player.Dead) Advance(ref ai, 600f, 0f);
                }
            }
            else
            {
                Advance(ref ai, vice ? 600f : 300f, ai.Ai2 + 1f);
                Axis(ref vy, npc.PositionY, parent.PositionY + (vice ? 230f : 260f), parent.PositionY + (vice ? 300f : 320f),
                    vice ? .1f : .04f, vice ? .1f : .04f, 3f, 3f, .96f);
                Axis(ref vx, npc.PositionX + size.Width / 2, vice ? px : px - 250f, vice ? px + 250f : px,
                    vice ? .2f : .3f, .3f, vice ? 8f : 12f, vice ? 9f : 12f, vice ? .94f : .96f);
            }
            sim = sim with { Rotation = Rotation(dx, dy) };
        }
        else if (phase == 1f)
        {
            sim = sim with { Rotation = Rotation(vice ? px - 280f * ai.Ai0 - cx : dx, dy) };
            if (vice)
            {
                if (vy > 0f) vy *= .9f;
                vx = (vx * 5f + parent.VelocityX) / 6f;
                vx += .5f;
                vy = MathF.Max(vy - .5f, -9f);
            }
            else { vx *= .95f; vy = MathF.Max(vy - .1f, -8f); }
            if (npc.PositionY < parent.PositionY - (vice ? 280f : 200f))
            {
                Refresh(in npc, in definition, context, size, ref target, ref sim);
                ai = ai with { Ai2 = 2f };
                if (TryTarget(context, target, out var player))
                { vx = player.CenterX - cx; vy = player.CenterY - cy; Aim(ref vx, ref vy, vice ? 20f : 22f); }
            }
        }
        else if (phase == 2f)
        {
            // AI33/34 compare the arm top edge against Player.position.Y, whose live dimensions may be
            // altered by a mount. Target candidates carry that physical height alongside their center.
            if ((TryTarget(context, target, out var player) && npc.PositionY > player.CenterY - player.Height * .5f) || vy < 0f)
            {
                if (!vice) ai = ai with { Ai2 = 3f };
                else ai = ai.Ai3 >= 4f ? ai with { Ai2 = 3f, Ai3 = 0f } : ai with { Ai2 = 1f, Ai3 = ai.Ai3 + 1f };
            }
        }
        else if (phase == 4f)
        {
            sim = sim with { Rotation = Rotation(dx, dy) };
            if (vice)
            {
                vy = (vy * 5f + parent.VelocityY) / 6f;
                vx = MathF.Min(vx + .5f, 12f);
                if (npc.PositionX + size.Width / 2 < px - 500f || npc.PositionX + size.Width / 2 > px + 500f)
                {
                    Refresh(in npc, in definition, context, size, ref target, ref sim);
                    ai = ai with { Ai2 = 5f };
                    if (TryTarget(context, target, out var player))
                    { vx = player.CenterX - cx; vy = player.CenterY - cy; Aim(ref vx, ref vy, 17f); }
                }
            }
            else
            {
                Refresh(in npc, in definition, context, size, ref target, ref sim);
                if (TryTarget(context, target, out var player))
                {
                    float tx = player.CenterX - cx, ty = player.CenterY - cy;
                    Aim(ref tx, ref ty, 7f); Pursue(ref vx, tx); Pursue(ref vy, ty);
                }
                Advance(ref ai, 600f, 0f);
            }
        }
        else if (phase == 5f && TryTarget(context, target, out var player))
        {
            float x = npc.PositionX + size.Width / 2;
            if (vice)
            {
                if (x < player.CenterX - 100f)
                    ai = ai.Ai3 >= 4f ? ai with { Ai2 = 0f, Ai3 = 0f } : ai with { Ai2 = 4f, Ai3 = ai.Ai3 + 1f };
            }
            else if ((vx > 0f && x > player.CenterX) || (vx < 0f && x < player.CenterX)) ai = ai with { Ai2 = 0f };
        }
        next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
        return true;
    }

    public static bool Deactivates(in NpcSnapshot before, in NpcStateUpdate proposed) =>
        (before.TypeIdentity == VanillaNpcIds.PrimeSaw || before.TypeIdentity == VanillaNpcIds.PrimeVice) &&
        proposed.Type == before.Type && proposed.Simulation.Life == 0 &&
        proposed.Simulation.TimeLeft == 0 && proposed.Ai.Ai2 > 50f;

    public static void ApplyEffects(in NpcSnapshot before, in NpcSnapshot committed,
        IVanillaNpcRandom random, INpcAiCommittedNpcMutationSink mutations)
    {
        if (committed.Simulation.Life != 0 || committed.Simulation.TimeLeft != 0 || committed.Ai.Ai2 <= 50f ||
            !mutations.TryGetActive(committed.Handle.Slot, out var current) || current.Handle != committed.Handle ||
            current.Revision != committed.Revision) return;
        // NPC.HitEffect (1.4.5.8) draws these even when dedicated-server Gore.NewGore is inert.
        for (int i = 0; i < 4; i++) random.NextInt32(61, 64);
    }

    private static void Advance(ref NpcAiState ai, float limit, float nextPhase)
    {
        ai = ai with { Ai3 = ai.Ai3 + 1f };
        if (ai.Ai3 >= limit) ai = ai with { Ai2 = nextPhase, Ai3 = 0f };
    }

    private static bool TryTarget(VanillaNpcBehaviorContext context, ushort slot, out VanillaNpcTargetCandidate player)
    { player = default; return slot < byte.MaxValue && context.TryFindCandidate((byte)slot, out player); }

    private static void Refresh(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        VanillaNpcHitboxSize size, ref ushort target, ref NpcSimulationState sim)
    {
        if (!context.TrySelectClosestTarget(in npc, in definition, out var refresh) || !refresh.HasTarget ||
            !TryTarget(context, refresh.Target, out var player)) return;
        target = refresh.Target;
        int x = sim.DirectionX, y = sim.DirectionY;
        if (!player.Dead && (!player.NoAggro || x == 0)) x = player.CenterX < npc.PositionX + size.Width / 2 ? -1 : 1;
        if (!player.Dead && (!player.NoAggro || y == 0)) y = player.CenterY < npc.PositionY + size.Height / 2 ? -1 : 1;
        sim = sim with { DirectionX = x, DirectionY = y };
    }

    private static float Rotation(float x, float y) => (float)Math.Atan2(y, x) + 1.57f;
    private static float Length(float x, float y) => (float)Math.Sqrt(OperatingSystem.IsWindows() ? (double)x * x + (double)y * y : x * x + y * y);
    private static void Aim(ref float x, ref float y, float speed)
    { float multiplier = speed / Length(x, y); x *= multiplier; y *= multiplier; }
    private static void Pursue(ref float velocity, float desired)
    {
        if (velocity > desired) { if (velocity > 0f) velocity *= .97f; velocity -= .05f; }
        if (velocity < desired) { if (velocity < 0f) velocity *= .97f; velocity += .05f; }
    }
    private static void Axis(ref float velocity, float position, float low, float high, float positive, float negative, float negativeCap, float positiveCap, float damping)
    {
        if (position > high) { if (velocity > 0f) velocity *= damping; velocity -= negative; if (velocity > positiveCap) velocity = positiveCap; }
        if (position < low) { if (velocity < 0f) velocity *= damping; velocity += positive; if (velocity < -negativeCap) velocity = -negativeCap; }
    }
}
