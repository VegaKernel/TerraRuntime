using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Server-authoritative gameplay slice of TerrariaServer 1.4.5.8 AI_121_QueenSlime.
/// Cosmetic dust/light/sound/rotation are intentionally omitted; target loss, phase transition,
/// teleport, jump/smash/burst timing and movement flags remain authoritative. Projectile/minion
/// side effects are planned by VanillaNpcTargetingAiStepper after the state commit succeeds.
/// </summary>
internal sealed class VanillaQueenSlimeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom _random;
    private IVanillaKingSlimeEnvironment? _environment;

    public VanillaQueenSlimeNpcBehaviorStrategy(IVanillaNpcRandom random, IVanillaKingSlimeEnvironment? environment)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _environment = environment;
    }

    public void SetEnvironment(IVanillaKingSlimeEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.QueenSlime || npc.TypeIdentity != VanillaNpcIds.QueenSlime ||
            _environment is null || !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        NpcAiState local = simulation.LocalAi;
        float x = npc.PositionX;
        float y = npc.PositionY;
        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        ushort targetSlot = npc.Target;
        int lifeMax = simulation.LifeMax > 0 ? simulation.LifeMax : definition.LifeMax;
        int life = simulation.LifeMax > 0 ? simulation.Life : lifeMax;
        bool phaseTwo = life <= lifeMax / 2;

        if (local.Ai0 == 0f)
        {
            ai = ai with { Ai1 = -100f };
            local = local with { Ai0 = lifeMax };
            TryRefresh(in npc, in definition, context, ref targetSlot, out _);
        }

        bool hasTarget = TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target);
        float centerX = x + hitbox.Width * 0.5f;
        float centerY = y + hitbox.Height * 0.5f;
        if (!hasTarget || MathF.Abs(centerX - target.CenterX) / 16f > 500f)
        {
            TryRefresh(in npc, in definition, context, ref targetSlot, out target);
            hasTarget = TryGetTarget(targetSlot, context, out target);
            if (!hasTarget || MathF.Abs(centerX - target.CenterX) / 16f > 500f)
            {
                int timeLeft = simulation.TimeLeft;
                if (timeLeft < 0 || timeLeft > 10)
                    simulation = simulation with { TimeLeft = 10 };
                if (hasTarget)
                    simulation = simulation with { DirectionX = target.CenterX < centerX ? 1 : -1 };
            }
        }

        if (hasTarget && simulation.TimeLeft > 10 && !phaseTwo && ai.Ai3 >= 300f && ai.Ai0 == 0f && vy == 0f)
        {
            ai = ai with { Ai0 = 2f, Ai1 = 0f };
            bool antiCheese = ai.Ai3 >= 360f || Distance(centerX, centerY, target.CenterX, target.CenterY) > 2000f;
            if (antiCheese && ai.Ai3 > 360f)
                ai = ai with { Ai3 = 360f };
            if (_environment.TryResolveTeleport(in npc, in definition, in target, antiCheese,
                    out VanillaKingSlimeTeleportDestination destination) && destination.IsFinite)
                local = local with { Ai1 = destination.BottomX, Ai2 = destination.BottomY };
            else
                ai = ai with { Ai3 = 0f };
        }

        if (hasTarget && !phaseTwo &&
            (!_environment.CanHitLine(centerX, centerY, target.CenterX, target.CenterY) ||
             MathF.Abs(y - (target.CenterY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f)) > 320f))
            ai = ai with { Ai3 = ai.Ai3 + 1.5f };
        else
            ai = ai with { Ai3 = MathF.Max(0f, ai.Ai3 - 1f) };

        if (simulation.TimeLeft <= 10 && ((phaseTwo && ai.Ai0 != 0f) || (!phaseTwo && ai.Ai0 != 3f)))
            ai = new NpcAiState(phaseTwo ? 0f : 3f, 0f, 0f, 0f);

        bool noGravity = false;
        bool noTileCollide = false;
        if (phaseTwo)
        {
            float frameTimer = local.Ai3 + 1f;
            if (frameTimer >= 24f) frameTimer = 0f;
            if (ai.Ai0 == 4f && ai.Ai2 == 1f) frameTimer = 6f;
            if (ai.Ai0 == 5f && ai.Ai2 != 1f) frameTimer = 7f;
            local = local with { Ai3 = frameTimer };
        }

        switch ((int)ai.Ai0)
        {
            case 0:
                if (phaseTwo)
                {
                    noGravity = true;
                    noTileCollide = true;
                    if (hasTarget)
                        SimpleFly(centerX, centerY, in target, simulation.TimeLeft, simulation.DirectionX, ref vx, ref vy);
                }
                else if (vy == 0f)
                {
                    vx *= 0.8f;
                    if (MathF.Abs(vx) < 0.1f) vx = 0f;
                }

                if (simulation.TimeLeft > 10 && (phaseTwo || vy == 0f))
                {
                    float timer = ai.Ai1 + 1f;
                    int threshold = phaseTwo ? 120 : 60;
                    if (timer > threshold)
                    {
                        timer = 0f;
                        if (phaseTwo)
                        {
                            int choice = _random.NextInt32(0, 2);
                            float state = choice == 0 ? 4f : 5f;
                            float sub = state == 4f ? 1f : 0f;
                            if (state == 4f && hasTarget &&
                                (target.CenterY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f < y + hitbox.Height ||
                                 MathF.Abs(target.CenterX - centerX) > 250f))
                            { state = 5f; sub = 0f; }
                            ai = ai with { Ai0 = state, Ai2 = sub };
                        }
                        else
                            ai = ai with { Ai0 = 3f + _random.NextInt32(0, 3) };
                    }
                    ai = ai with { Ai1 = timer };
                }
                break;

            case 1:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 30f)
                {
                    ai = ai with { Ai0 = 0f, Ai1 = 0f };
                    TryRefresh(in npc, in definition, context, ref targetSlot, out _);
                }
                break;

            case 2:
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 60f)
                {
                    x = local.Ai1 - hitbox.Width * 0.5f;
                    y = local.Ai2 - hitbox.Height;
                    ai = ai with { Ai0 = 1f, Ai1 = 0f };
                }
                break;

            case 3:
                if (vy == 0f)
                {
                    vx *= 0.8f;
                    if (MathF.Abs(vx) < 0.1f) vx = 0f;
                    float timer = ai.Ai1 + 4f + (life < lifeMax * 0.66f ? 4f : 0f) + (life < lifeMax * 0.33f ? 4f : 0f);
                    ai = ai with { Ai1 = timer };
                    if (timer >= 0f)
                    {
                        TryRefresh(in npc, in definition, context, ref targetSlot, out target);
                        int direction = hasTarget && target.CenterX < centerX ? -1 : 1;
                        simulation = simulation with { DirectionX = direction };
                        if (ai.Ai2 == 3f)
                        {
                            vy = -13f; vx += 3.5f * direction;
                            ai = ai with { Ai1 = simulation.TimeLeft > 10 ? 0f : -60f, Ai2 = 0f, Ai0 = simulation.TimeLeft > 10 ? 0f : 3f };
                        }
                        else if (ai.Ai2 == 2f)
                        { vy = -6f; vx += 4.5f * direction; ai = ai with { Ai1 = -40f, Ai2 = 3f }; }
                        else
                        { vy = -8f; vx += 4f * direction; ai = ai with { Ai1 = -40f, Ai2 = ai.Ai2 + 1f }; }
                    }
                }
                else
                {
                    int direction = simulation.DirectionX == 0 ? 1 : simulation.DirectionX;
                    float max = context.GoodWorld ? 7f : 3f;
                    if ((direction == 1 && vx < max) || (direction == -1 && vx > -max))
                    {
                        if ((direction == -1 && vx < 0.1f) || (direction == 1 && vx > -0.1f)) vx += 0.2f * direction;
                        else vx *= 0.93f;
                    }
                }
                break;

            case 4:
                noGravity = true;
                noTileCollide = true;
                if (ai.Ai2 == 1f)
                {
                    noGravity = false;
                    noTileCollide = false;
                    int delay = context.GoodWorld ? 0 : phaseTwo ? 10 : 30;
                    if (vy == 0f)
                    { ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f }; }
                    else
                    {
                        float old = ai.Ai1;
                        float timer = old + 1f;
                        ai = ai with { Ai1 = timer };
                        vx *= 0.8f;
                        if (timer >= delay)
                        {
                            if (phaseTwo && timer > delay + 120)
                            { ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f }; vy *= 0.8f; }
                            else
                            { vy += context.GoodWorld ? 2f : 1f; if (vy == 0f) vy = 0.01f; vy = MathF.Min(vy, context.GoodWorld ? 15.99f : 14f); }
                        }
                        else vy *= 0.8f;
                    }
                }
                else
                {
                    float timer = ai.Ai1 + 1f;
                    if (timer >= 60f)
                    { timer = 0f; ai = ai with { Ai2 = 1f }; vy = -3f; }
                    ai = ai with { Ai1 = timer };
                    if (ai.Ai2 != 1f && timer >= 30f && hasTarget && vy == 0f)
                    {
                        float tx = target.CenterX;
                        float ty = target.CenterY - 384f;
                        SetVelocityToward(centerX, centerY, tx, ty, 20f, ref vx, ref vy);
                    }
                    else if (vy != 0f) vy *= 0.95f;
                }
                break;

            case 5:
                noGravity = true;
                noTileCollide = true;
                if (phaseTwo) ai = ai with { Ai3 = 0f };
                if (ai.Ai2 == 1f)
                {
                    float timer = ai.Ai1 + 1f;
                    if (timer >= 10f) ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f };
                    else ai = ai with { Ai1 = timer };
                }
                else
                {
                    float timer = ai.Ai1 + 1f;
                    if (timer >= 50f) { timer = 0f; ai = ai with { Ai2 = 1f }; }
                    ai = ai with { Ai1 = timer };
                }
                break;
        }

        // Terraria uses localAI[0] as the HP anchor for Queen Slime minion threshold spawns.
        // Advance it exactly when this tick crosses the current phase threshold, so the spawn planner
        // emits one batch instead of re-emitting minions on every subsequent tick.
        float minionThreshold = lifeMax * (phaseTwo ? 0.015f : 0.02f);
        if (life + minionThreshold < local.Ai0)
            local = local with { Ai0 = life };

        simulation = simulation with { NoGravity = noGravity, NoTileCollide = noTileCollide, LocalAi = local, JustHit = false };
        next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, simulation);
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
        ref ushort targetSlot, out VanillaNpcTargetCandidate target)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh refresh) && refresh.HasTarget &&
            refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out target) && target.Active && !target.Dead && !target.Ghost)
        { targetSlot = refresh.Target; return true; }
        target = default; return false;
    }

    private static void SimpleFly(float centerX, float centerY, in VanillaNpcTargetCandidate target, int timeLeft, int direction,
        ref float vx, ref float vy)
    {
        float dx = timeLeft > 10 ? target.CenterX - centerX : 500f * (direction == 0 ? 1 : direction);
        float dy = timeLeft > 10 ? target.CenterY - 250f - centerY : -250f;
        if (MathF.Abs(dx) < 40f) dx = vx;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float accel = distance > 100f && ((vx < -10f && dx > 0f) || (vx > 10f && dx < 0f)) ? 0.17f : 0.085f;
        float desiredX = vx, desiredY = vy;
        if (distance >= 40f)
        {
            float speed = distance < 80f ? 7.8f : 12f;
            float inv = speed / MathF.Max(distance, 0.001f);
            desiredX = dx * inv; desiredY = dy * inv;
        }
        Approach(ref vx, desiredX, accel); Approach(ref vy, desiredY, accel);
    }

    private static void Approach(ref float value, float desired, float accel)
    { if (value < desired) value = MathF.Min(value + accel, desired); else if (value > desired) value = MathF.Max(value - accel, desired); }

    private static void SetVelocityToward(float x, float y, float tx, float ty, float speed, ref float vx, ref float vy)
    { float dx = tx - x, dy = ty - y; float d = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy)); vx = dx / d * speed; vy = dy / d * speed; }

    private static float Distance(float x1, float y1, float x2, float y2)
    { float dx = x2 - x1, dy = y2 - y1; return MathF.Sqrt(dx * dx + dy * dy); }
}
