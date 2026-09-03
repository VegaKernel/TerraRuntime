using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core;

/// <summary>
/// Server-authoritative clean-room state slice for TerrariaServer 1.4.5.8 AI 69/70/71.
/// The root owns intro, phase transitions, hover and dash cadence; Detonating Bubbles own the source homing/lifetime state.
/// Visual dust/rotation branches stay out of NPC authority; Sharkron emergence/charge state is authoritative.
/// </summary>
internal sealed class VanillaDukeFishronNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity == VanillaNpcIds.DukeFishron)
            return TryStepRoot(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.DetonatingBubble)
            return TryStepDetonatingBubble(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.Sharkron || npc.TypeIdentity == VanillaNpcIds.Sharkron2)
            return TryStepSharkron(in npc, in definition, context, out next);
        next = default;
        return false;
    }

    private static bool TryStepRoot(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.DukeFishron)
        { next = default; return false; }

        ushort targetSlot = npc.Target;
        if (!TryTarget(in npc, in definition, context, ref targetSlot, out VanillaNpcTargetCandidate target))
        {
            NpcAiState retreatAi = npc.Ai with { Ai0 = npc.Ai.Ai0 > 4f ? 5f : 0f, Ai2 = 0f };
            NpcSimulationState retreatSim = npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = true,
                TimeLeft = npc.Simulation.TimeLeft is < 0 or > 10 ? 10 : npc.Simulation.TimeLeft
            };
            next = Build(in npc, npc.VelocityX, npc.VelocityY - .4f, targetSlot, in retreatAi, in retreatSim);
            return true;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
        int life = sim.LifeMax > 0 ? sim.Life : lifeMax;
        bool phaseTwoLife = life <= lifeMax * .5f;
        bool phaseThreeLife = context.ExpertMode && life <= lifeMax * .15f;

        if (local.Ai0 == 0f)
        {
            local = local with { Ai0 = 1f };
            ai = ai with { Ai0 = -1f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
            sim = sim with { Alpha = 255 };
        }

        bool phaseTwo = ai.Ai0 > 4f;
        bool phaseThree = ai.Ai0 > 9f;
        int damage = definition.Damage;
        int defense = definition.Defense;
        if (phaseThree)
        {
            damage = (int)(definition.Damage * 1.1f * (context.ExpertMode ? 1.2f : 1f));
            defense = 0;
        }
        else if (phaseTwo)
        {
            damage = (int)(definition.Damage * 1.2f * (context.ExpertMode ? 1.2f : 1f));
            defense = (int)(definition.Defense * .8f);
        }

        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float cx = npc.PositionX + definition.Width * .5f;
        float cy = npc.PositionY + definition.Height * .5f;
        bool vulnerable = true;

        switch ((int)ai.Ai0)
        {
            case -1:
                vulnerable = false;
                vx *= .98f;
                vy *= .98f;
                if (ai.Ai2 > 20f) vy = -2f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 75f)
                    ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                break;
            case 0:
                Hover(in target, cx, cy, phaseTwo ? (context.ExpertMode ? 10f : 8f) : (context.ExpertMode ? 8.5f : 7.5f),
                    phaseTwo ? (context.ExpertMode ? .6f : .5f) : (context.ExpertMode ? .55f : .45f), ref ai, ref vx, ref vy);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                int hoverTicks = phaseTwo ? (context.ExpertMode ? 40 : 20) : (context.ExpertMode ? 40 : 60);
                if (ai.Ai3 < (phaseTwo ? 6f : 10f)) hoverTicks = 30;
                if (ai.Ai2 >= hoverTicks)
                {
                    if (phaseThreeLife)
                        ai = ai with { Ai0 = 9f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                    else if (phaseTwoLife)
                        ai = ai with { Ai0 = 4f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                    else
                    {
                        int attack = ai.Ai3 >= 11f ? 3 : ai.Ai3 >= 10f ? 2 : 1;
                        ai = ai with { Ai0 = attack, Ai1 = 0f, Ai2 = 0f };
                        if (attack == 1)
                            SetToward(cx, cy, target.CenterX, target.CenterY, context.ExpertMode ? 17f : 16f, ref vx, ref vy);
                    }
                }
                break;
            case 1:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= (context.ExpertMode ? 28f : 30f))
                    ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 2f };
                break;
            case 2:
                Hover(in target, cx, cy, 5f, .3f, ref ai, ref vx, ref vy);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 80f) ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f };
                break;
            case 3:
                vx *= .98f; vy *= .98f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 90f) ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f };
                break;
            case 4:
                vulnerable = false;
                vx *= .98f; vy *= .98f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 180f) ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                break;
            case 5:
                Hover(in target, cx, cy, context.ExpertMode ? 10f : 8f, context.ExpertMode ? .6f : .5f, ref ai, ref vx, ref vy);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (phaseThreeLife)
                    ai = ai with { Ai0 = 9f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                else if (ai.Ai2 >= (context.ExpertMode ? 40f : 20f))
                {
                    int attack = ai.Ai3 >= 7f ? 8 : ai.Ai3 >= 5f ? 7 : 6;
                    ai = ai with { Ai0 = attack, Ai1 = 0f, Ai2 = 0f };
                    if (attack == 6)
                        SetToward(cx, cy, target.CenterX, target.CenterY, context.ExpertMode ? 21f : 16f, ref vx, ref vy);
                }
                break;
            case 6:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= (context.ExpertMode ? 27f : 30f))
                    ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 2f };
                break;
            case 7:
                Hover(in target, cx, cy, 6f, .3f, ref ai, ref vx, ref vy);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 120f) ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 2f };
                break;
            case 8:
                vx *= .98f; vy *= .98f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 90f) ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 2f };
                break;
            case 9:
                vulnerable = false;
                vx *= .98f; vy *= .98f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 180f) ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                break;
            case 10:
                Hover(in target, cx, cy, 12f, .7f, ref ai, ref vx, ref vy);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 30f)
                {
                    bool teleport = ((int)ai.Ai3 % 4) == 3;
                    ai = ai with { Ai0 = teleport ? 12f : 11f, Ai1 = 0f, Ai2 = 0f };
                    if (!teleport) SetToward(cx, cy, target.CenterX, target.CenterY, 27f, ref vx, ref vy);
                }
                break;
            case 11:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 25f) ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 1f };
                break;
            case 12:
                vulnerable = false;
                vx *= .9f; vy *= .9f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 30f) ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 1f };
                break;
            case 13:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 120f) ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 1f };
                break;
            default:
                ai = ai with { Ai0 = phaseThree ? 10f : phaseTwo ? 5f : 0f, Ai1 = 0f, Ai2 = 0f };
                break;
        }

        int alpha = sim.Alpha;
        if (ai.Ai0 != -1f && ai.Ai0 < 9f)
            alpha = Math.Clamp(alpha + (sim.SolidCollision ? 15 : -15), 0, 150);
        sim = sim with
        {
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = local,
            DontTakeDamage = !vulnerable,
            DamageOverride = damage,
            DefenseOverride = defense,
            Alpha = alpha,
            JustHit = false
        };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }


    private static bool TryStepSharkron(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Sharkron)
        { next = default; return false; }

        ushort targetSlot = npc.Target;
        if (!TryTarget(in npc, in definition, context, ref targetSlot, out VanillaNpcTargetCandidate target))
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcAiState local = npc.Simulation.LocalAi;
        NpcSimulationState sim = npc.Simulation;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        bool vulnerable = false;
        bool noGravity = true;

        if (ai.Ai0 == 0f)
        {
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            vy = ai.Ai3;
            if (npc.TypeIdentity == VanillaNpcIds.Sharkron2)
            {
                // Source AI71 stores the horizontal emergence amplitude in ai[2] and advances a 60-tick cosine phase in localAI[1].
                vx = (MathF.Cos(MathF.PI / 30f * local.Ai1) - .5f) * ai.Ai2;
                local = local with { Ai1 = local.Ai1 + 1f };
            }
            if (ai.Ai1 >= 90f)
            {
                ai = ai with { Ai0 = 1f, Ai1 = sim.SolidCollision ? 0f : 1f };
                SetToward(npc.PositionX + definition.Width * .5f, npc.PositionY + definition.Height * .5f,
                    target.CenterX, target.CenterY, 16f, ref vx, ref vy);
            }
        }
        else
        {
            if (!sim.SolidCollision && ai.Ai1 < 1f)
                ai = ai with { Ai1 = 1f };

            if (ai.Ai1 >= 1f)
            {
                vulnerable = true;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (sim.SolidCollision)
                    sim = sim with { Life = 0, TimeLeft = 0 };
                if (ai.Ai1 >= 60f)
                    noGravity = false;
            }
        }

        sim = sim with
        {
            NoGravity = noGravity,
            NoTileCollide = true,
            DontTakeDamage = !vulnerable,
            LocalAi = local,
            JustHit = false
        };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }

    private static bool TryStepDetonatingBubble(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.DetonatingBubble)
        { next = default; return false; }
        ushort targetSlot = npc.Target;
        if (!TryTarget(in npc, in definition, context, ref targetSlot, out VanillaNpcTargetCandidate target))
        { next = default; return false; }
        NpcAiState ai = npc.Ai;
        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float cx = npc.PositionX + 18f;
        float cy = npc.PositionY + 18f;
        if (targetSlot == VanillaNpcDefinitionCatalog.DefaultTarget || (vx == 0f && vy == 0f))
            SetToward(cx, cy, target.CenterX, target.CenterY, 17f, ref vx, ref vy);
        float dx = target.CenterX - cx, dy = target.CenterY - cy;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        float tx = dx / d * 20f, ty = dy / d * 20f;
        vx = (vx * 40f + tx) / 41f;
        vy = (vy * 40f + ty) / 41f;
        float timer = ai.Ai1 + 1f;
        if (timer >= 150f) ai = ai with { Ai0 = 1f, Ai1 = 4f };
        else ai = ai with { Ai1 = timer };
        if (ai.Ai0 == 1f) ai = ai with { Ai1 = ai.Ai1 - 1f };
        NpcSimulationState sim = npc.Simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            DontTakeDamage = npc.Simulation.JustHit || ai.Ai0 == 1f,
            Alpha = 50,
            TimeLeft = ai.Ai0 == 1f ? Math.Min(npc.Simulation.TimeLeft < 0 ? 3 : npc.Simulation.TimeLeft, 3) : npc.Simulation.TimeLeft,
            JustHit = false
        };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        return true;
    }

    private static void Hover(in VanillaNpcTargetCandidate target, float cx, float cy, float speed, float acceleration,
        ref NpcAiState ai, ref float vx, ref float vy)
    {
        if (ai.Ai1 == 0f)
            ai = ai with { Ai1 = 300f * MathF.Sign(cx - target.CenterX) };
        float dx = target.CenterX + ai.Ai1 - cx - vx;
        float dy = target.CenterY - 200f - cy - vy;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        Approach(ref vx, dx / d * speed, acceleration);
        Approach(ref vy, dy / d * speed, acceleration);
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

    private static void Approach(ref float value, float target, float amount)
    { if (value < target) value = MathF.Min(value + amount, target); else if (value > target) value = MathF.Max(value - amount, target); }
    private static void SetToward(float x, float y, float tx, float ty, float speed, ref float vx, ref float vy)
    { float dx = tx - x, dy = ty - y, d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy)); vx = dx / d * speed; vy = dy / d * speed; }
    private static NpcStateUpdate Build(in NpcSnapshot npc, float vx, float vy, ushort target, in NpcAiState ai, in NpcSimulationState sim) =>
        new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
}
