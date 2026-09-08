using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Server-authoritative clean-room state slice for TerrariaServer 1.4.5.8 AI 69/70/71.
/// The root owns intro, phase transitions, hover and dash cadence; Detonating Bubbles own the source homing/lifetime state.
/// Visual dust/rotation branches stay out of NPC authority; Sharkron emergence/charge state is authoritative.
/// </summary>
internal sealed class VanillaDukeFishronNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random;

    public VanillaDukeFishronNpcBehaviorStrategy(IVanillaNpcRandom random) => this.random = random;

    internal static bool TryResolveEnrage(VanillaNpcBehaviorContext context,
        in VanillaNpcTargetCandidate target, out bool enraged)
    {
        enraged = false;
        if (context.WorldWidthPixels <= 0d || !double.IsFinite(context.WorldSurfacePixels))
            return false;
        // AI69 tests player.position (not Center) and uses strict inequalities at all four boundaries.
        float left = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f;
        float top = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f;
        enraged = top < 800f || top > context.WorldSurfacePixels ||
            (left > 6400f && left < context.WorldWidthPixels - 6400d);
        return true;
    }

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

        if (!TryResolveEnrage(context, in target, out bool enraged))
        { next = default; return false; }

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
        // Ordinary Classic/Expert/Master SetDefaults -> ScaleStats_ByDifficulty_Tweaks (type370, factor .7).
        // The contact pipeline consumes DamageOverride directly; it must not receive an unscaled defDamage.
        int baseDamage = context.ExpertMode
            ? (int)Math.Round(definition.Damage * (context.MasterMode ? 3f : 2f) * .7f)
            : definition.Damage;
        int damage = baseDamage;
        int defense = definition.Defense;
        if (phaseThree)
        {
            damage = (int)(baseDamage * 1.1f * (context.ExpertMode ? 1.2f : 1f));
            defense = 0;
        }
        else if (phaseTwo)
        {
            damage = (int)(baseDamage * 1.2f * (context.ExpertMode ? 1.2f : 1f));
            defense = (int)(definition.Defense * .8f);
        }
        if (enraged)
        {
            damage = baseDamage * 2;
            defense = definition.Defense * 2;
        }
        float dashBonus = enraged ? 6f : 0f;

        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float cx = npc.PositionX + definition.Width * .5f;
        float cy = npc.PositionY + definition.Height * .5f;
        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        bool vulnerable = true;

        if (ai.Ai0 is -1f or 0f or 2f or 5f or 10f)
        {
            int facing = Math.Sign(target.CenterX - cx);
            if (facing != 0)
                sim = sim with { DirectionX = facing, SpriteDirection = -facing };
        }

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
                if (enraged) hoverTicks = 10;
                if (ai.Ai2 >= hoverTicks)
                {
                    int attack = (int)ai.Ai3 switch { >= 0 and <= 9 => 1, 10 => 2, 11 => 3, _ => 0 };
                    if (attack is 2 or 3)
                        ai = ai with { Ai3 = attack == 2 ? 1f : 0f };
                    if (enraged && attack == 2) attack = 3;
                    if (phaseTwoLife) attack = 4;
                    if (attack != 0)
                        ai = ai with { Ai0 = attack, Ai1 = 0f, Ai2 = enraged && attack == 3 ? 50f : 0f };
                    if (attack == 1)
                        SetToward(cx, cy, target.CenterX, target.CenterY, (context.ExpertMode ? 17f : 16f) + dashBonus, ref vx, ref vy);
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
                bool phaseTwoDashes = ai.Ai3 < 6f;
                Hover(in target, cx, cy,
                    phaseTwoDashes ? (context.ExpertMode ? 10f : 8f) : (context.ExpertMode ? 8.5f : 7.5f),
                    phaseTwoDashes ? (context.ExpertMode ? .6f : .5f) : (context.ExpertMode ? .55f : .45f), ref ai, ref vx, ref vy);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= (enraged ? 10f : context.ExpertMode ? 40f : phaseTwoDashes ? 20f : 60f))
                {
                    int attack = (int)ai.Ai3 switch { >= 0 and <= 5 => 6, 6 => 7, 7 => 8, _ => 0 };
                    if (attack is 7 or 8)
                        ai = ai with { Ai3 = attack == 7 ? 1f : 0f };
                    if (phaseThreeLife) attack = 9;
                    if (enraged && attack == 7) attack = 8;
                    if (attack != 0)
                        ai = ai with { Ai0 = attack, Ai1 = 0f, Ai2 = 0f };
                    if (attack == 6)
                        SetToward(cx, cy, target.CenterX, target.CenterY, (context.ExpertMode ? 21f : 16f) + dashBonus, ref vx, ref vy);
                    if (attack == 7)
                        SetToward(cx, cy, target.CenterX, target.CenterY, 20f, ref vx, ref vy);
                }
                break;
            case 6:
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= (context.ExpertMode ? 27f : 30f))
                    ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 2f };
                break;
            case 7:
                // AI69 circle motion rotates the incoming velocity, not a hover toward the target.
                RotateCircle(ref vx, ref vy, sim.DirectionX);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 120f) ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f };
                break;
            case 8:
                vx *= .98f; vy *= .98f;
                vy += (0f - vy) * .02f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 90f) ai = ai with { Ai0 = 5f, Ai1 = 0f, Ai2 = 0f };
                break;
            case 9:
                vulnerable = false;
                vx *= .98f; vy *= .98f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 180f) ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = 0f };
                break;
            case 10:
                sim = sim with { Chaseable = false, Alpha = Math.Min(255, sim.Alpha + 25) };
                Hover(in target, cx, cy, 12f, .7f, ref ai, ref vx, ref vy, horizontalOffset: 360f);
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= (enraged ? 10f : 30f))
                {
                    int attack = (int)ai.Ai3 switch
                    {
                        0 or 2 or 3 or 5 or 6 or 7 => 11,
                        1 or 4 or 8 => 12,
                        _ => 0
                    };
                    if (attack != 0)
                    {
                        ai = ai with { Ai0 = attack, Ai1 = 0f, Ai2 = 0f };
                        if (attack == 11) SetToward(cx, cy, target.CenterX, target.CenterY, 27f + dashBonus, ref vx, ref vy);
                    }
                }
                break;
            case 11:
                sim = sim with { Chaseable = true, Alpha = Math.Max(0, sim.Alpha - 25) };
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 25f) ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = ai.Ai3 + 1f };
                break;
            case 12:
                sim = sim with { Chaseable = false, Alpha = Math.Min(255, sim.Alpha + 17) };
                vulnerable = false;
                vx *= .98f; vy *= .98f;
                vy += (0f - vy) * .02f;
                // AI69 teleports at the incoming half-time (15), before incrementing the 30-tick clock.
                if (ai.Ai2 == 15f)
                {
                    if (ai.Ai1 == 0f)
                        ai = ai with { Ai1 = 300f * MathF.Sign(cx - target.CenterX) };
                    cx = target.CenterX - ai.Ai1;
                    cy = target.CenterY - 200f;
                    positionX = cx - definition.Width * .5f;
                    positionY = cy - definition.Height * .5f;
                    int direction = Math.Sign(target.CenterX - cx);
                    if (direction != 0)
                        sim = sim with { DirectionX = direction, SpriteDirection = -direction };
                }
                ai = ai with { Ai2 = ai.Ai2 + 1f };
                if (ai.Ai2 >= 30f)
                {
                    float cycle = ai.Ai3 + 1f;
                    ai = ai with { Ai0 = 10f, Ai1 = 0f, Ai2 = 0f, Ai3 = cycle >= 9f ? 0f : cycle };
                }
                break;
            case 13:
                RotateCircle(ref vx, ref vy, sim.DirectionX);
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
        next = next with { PositionX = positionX, PositionY = positionY };
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

    private bool TryStepDetonatingBubble(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.DetonatingBubble)
        { next = default; return false; }
        // AI70 owns width/height separately from visual scale; preserve the expanded body's center.
        NpcHitboxDimensions body = npc.Simulation.HitboxOverride ?? new NpcHitboxDimensions(36, 36);
        if (!body.IsValid)
        { next = default; return false; }
        ushort targetSlot = npc.Target;
        bool hasTarget = TryTarget(in npc, in definition, context, ref targetSlot, out VanillaNpcTargetCandidate target);
        NpcAiState ai = npc.Ai;
        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float cx = npc.PositionX + body.Width * .5f;
        float cy = npc.PositionY + body.Height * .5f;
        if (hasTarget)
        {
            // Only an incoming unassigned target initializes AI70. An assigned, stationary circle child
            // must accelerate from rest, not receive another artificial launch impulse.
            if (npc.Target == VanillaNpcDefinitionCatalog.DefaultTarget)
            {
                ai = ai with { Ai3 = random.NextInt32(80, 121) / 100f };
                float speed = random.NextInt32(165, 265) / 15f;
                float offsetX = random.NextInt32(-100, 101);
                float offsetY = random.NextInt32(-100, 101);
                SetToward(cx, cy, target.CenterX + offsetX, target.CenterY + offsetY, speed, ref vx, ref vy);
            }
            float dx = target.CenterX - cx, dy = target.CenterY - cy;
            float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
            vx = (vx * 40f + dx / d * 20f) / 41f;
            vy = (vy * 40f + dy / d * 20f) / 41f;
        }
        if (ai.Ai3 <= 0f)
        { next = default; return false; } // Unverified/invalid bootstrap cannot manufacture a scale.
        vx = (vx * 50f + context.WindSpeedCurrent * 2f + random.NextInt32(-10, 11) * .1f) / 51f;
        vy = (vy * 50f - .25f + random.NextInt32(-10, 11) * .2f) / 51f;
        if (vy > 0f) vy -= .04f;
        // getRect is integer-based. Source extends its upper-left by 40 + half the physical body,
        // but adds only 80 to its dimensions: this is intentionally not a centered radius test.
        if (ai.Ai0 == 0f && context.AnyLivingPlayerIntersects(
                (int)npc.PositionX - 40 - body.Width / 2,
                (int)npc.PositionY - 40 - body.Height / 2, body.Width + 80, body.Height + 80))
            ai = ai with { Ai0 = 1f, Ai1 = 4f };
        // AI70 increments lifetime only while not detonating. Incrementing during countdown cancels
        // its subsequent decrement and leaves an invulnerable bubble alive indefinitely.
        if (ai.Ai0 == 0f)
        {
            float timer = ai.Ai1 + 1f;
            ai = timer >= 150f ? ai with { Ai0 = 1f, Ai1 = 4f } : ai with { Ai1 = timer };
        }
        if (ai.Ai0 == 1f) ai = ai with { Ai1 = ai.Ai1 - 1f };
        bool expired = ai.Ai0 == 1f && ai.Ai1 <= 0f;
        bool expand = !expired && (npc.Simulation.JustHit || ai.Ai0 == 1f);
        NpcSimulationState sim = npc.Simulation with
        {
            HitboxOverride = expand ? new NpcHitboxDimensions(100, 100) : body,
            Scale = ai.Ai3,
            NoGravity = true,
            NoTileCollide = true,
            DontTakeDamage = npc.Simulation.DontTakeDamage || expand,
            Alpha = 50,
            Life = expired ? 0 : npc.Simulation.Life,
            // The shared post-AI expiry pass removes this exact generation without combat loot/progression.
            TimeLeft = expired ? 0 : expand ? Math.Min(npc.Simulation.TimeLeft < 0 ? 3 : npc.Simulation.TimeLeft, 3) : npc.Simulation.TimeLeft,
            JustHit = false
        };
        next = Build(in npc, vx, vy, targetSlot, in ai, in sim);
        if (expand)
            next = next with { PositionX = cx - 50f, PositionY = cy - 50f };
        return true;
    }

    private static void Hover(in VanillaNpcTargetCandidate target, float cx, float cy, float speed, float acceleration,
        ref NpcAiState ai, ref float vx, ref float vy, float horizontalOffset = 300f)
    {
        if (ai.Ai1 == 0f)
            ai = ai with { Ai1 = horizontalOffset * MathF.Sign(cx - target.CenterX) };
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
    {
        if (value < target)
        {
            value += amount;
            if (value < 0f && target > 0f) value += amount;
        }
        else if (value > target)
        {
            value -= amount;
            if (value > 0f && target < 0f) value -= amount;
        }
    }
    private static void RotateCircle(ref float vx, ref float vy, int direction)
    {
        float turn = -MathF.Tau / 60f * direction;
        float cosine = MathF.Cos(turn), sine = MathF.Sin(turn);
        (vx, vy) = (vx * cosine - vy * sine, vx * sine + vy * cosine);
    }
    private static void SetToward(float x, float y, float tx, float ty, float speed, ref float vx, ref float vy)
    { float dx = tx - x, dy = ty - y, d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy)); vx = dx / d * speed; vy = dy / d * speed; }
    private static NpcStateUpdate Build(in NpcSnapshot npc, float vx, float vy, ushort target, in NpcAiState ai, in NpcSimulationState sim) =>
        new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
}
