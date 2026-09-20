using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Authoritative TerrariaServer 1.4.5.8 AI-84 state slice for Lunatic Cultist and its ritual clones.
/// Root/clone synchronization, ritual timing, phase defense, movement and Ancient Vision/Light/Doom children are server-owned;
/// projectile emission is planned separately through source-owned intents.
/// </summary>
internal sealed class VanillaLunaticCultistNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity == VanillaNpcIds.LunaticCultist)
            return TryRoot(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.LunaticCultistClone)
            return TryClone(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.AncientVision)
            return TryAncientVision(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.AncientLight)
            return TryAncientLight(in npc, in definition, out next);
        if (npc.TypeIdentity == VanillaNpcIds.AncientDoom)
            return TryAncientDoom(in npc, in definition, context, out next);
        next = default;
        return false;
    }

    private static bool TryRoot(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        ushort target = npc.Target;
        if (!LateBossMath.TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
        {
            NpcSimulationState gone = npc.Simulation with { TimeLeft = npc.Simulation.TimeLeft is < 0 or > 1 ? 1 : npc.Simulation.TimeLeft };
            NpcAiState goneAi = npc.Ai;
            next = LateBossMath.Build(in npc, npc.VelocityX, npc.VelocityY, target, in goneAi, in gone);
            return true;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
        int life = sim.LifeMax > 0 ? sim.Life : lifeMax;
        bool phaseTwo = life <= lifeMax / 2;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;

        if (local.Ai0 == 0f)
        {
            local = local with { Ai0 = 1f };
            ai = ai with { Ai0 = -1f, Ai1 = 0f };
            sim = sim with { Alpha = 255 };
        }

        if (ai.Ai0 == 5f && ai.Ai1 >= 120f && ai.Ai1 < 420f && sim.JustHit)
        {
            // Source AI84 aborts a live ritual when the real Cultist is struck and advances the attack cycle.
            ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai3 = ai.Ai3 + 1f };
            vx = 0f;
            vy = 0f;
        }
        else if (ai.Ai0 == 5f && HasHitOwnedClone(context, npc.Handle.Slot))
        {
            // A struck ritual clone kills itself and forces its owner into the 120-tick punishment state.
            ai = ai with { Ai0 = 6f, Ai1 = 0f };
            vx *= .95f;
            vy *= .95f;
        }

        bool chaseable = true;
        bool dontTakeDamage = false;
        switch ((int)ai.Ai0)
        {
            case -1:
                chaseable = false;
                dontTakeDamage = true;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 > 300f && ai.Ai1 <= 360f) { vx = 0f; vy = -1f; }
                else if (ai.Ai1 > 360f) { vx *= .95f; vy *= .95f; }
                if (ai.Ai1 >= 420f) ai = ai with { Ai0 = 0f, Ai1 = 0f };
                break;
            case 0:
                vx *= .9f; vy *= .9f;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 40f)
                {
                    int attack = SelectAttack((int)ai.Ai3, phaseTwo);
                    if (attack == 0)
                    {
                        float steps = MathF.Max(1f, MathF.Ceiling(LateBossMath.Distance(cx, cy, player.CenterX, player.CenterY - 100f) / 50f));
                        float dx = player.CenterX - cx;
                        float dy = player.CenterY - 100f - cy;
                        vx = dx / steps; vy = dy / steps;
                        ai = ai with { Ai0 = 1f, Ai1 = steps * 2f };
                    }
                    else
                        ai = ai with { Ai0 = attack, Ai1 = 0f };
                }
                break;
            case 1:
                dontTakeDamage = true;
                ai = ai with { Ai1 = ai.Ai1 - 1f };
                if (((int)ai.Ai1 & 1) != 0 && ai.Ai1 != 1f) { /* source moves every second tick; committed velocity carries it */ }
                if (ai.Ai1 <= 0f) { ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai3 = ai.Ai3 + 1f }; vx = 0f; vy = 0f; }
                break;
            case 2:
            {
                int cadence = context.ExpertMode ? 90 : 120;
                if (context.GoodWorld) cadence -= 30;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 4 + cadence) ResetAttack(ref ai, ref vx, ref vy);
                break;
            }
            case 3:
            {
                int cadence = context.GoodWorld ? 10 : context.ExpertMode ? 12 : 18;
                int shots = context.GoodWorld ? 5 : context.ExpertMode ? 4 : 3;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 4 + cadence * shots) ResetAttack(ref ai, ref vx, ref vy);
                break;
            }
            case 4:
            {
                int cadence = context.ExpertMode ? 40 : 80;
                if (context.GoodWorld) cadence -= 20;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 20 + cadence) ResetAttack(ref ai, ref vx, ref vy);
                break;
            }
            case 5:
                // AI84 tests the incoming ritual clock, before increment/reset; the last ritual tick remains
                // unchaseable even when the committed ai[0] has already returned to idle.
                chaseable = false;
                dontTakeDamage = ai.Ai1 is >= 0f and < 120f;
                vx *= .95f; vy *= .95f;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                int alpha = sim.Alpha;
                if (ai.Ai1 < 30f) alpha = Math.Clamp((int)(ai.Ai1 / 30f * 255f), 0, 255);
                else if (ai.Ai1 < 90f) alpha = 255;
                else if (ai.Ai1 < 120f) alpha = Math.Clamp(255 - (int)((ai.Ai1 - 90f) / 30f * 255f), 0, 255);
                else alpha = 0;
                sim = sim with { Alpha = alpha };
                if (ai.Ai1 >= 420f) ResetAttack(ref ai, ref vx, ref vy);
                break;
            case 6:
                vx *= .95f; vy *= .95f;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 120f) ResetAttack(ref ai, ref vx, ref vy);
                break;
            case 7:
            {
                int cadence = context.ExpertMode ? 30 : 20;
                const int count = 2;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 4 + cadence * count) ResetAttack(ref ai, ref vx, ref vy);
                break;
            }
            case 8:
            {
                const int cadence = 20;
                const int count = 3;
                ai = ai with { Ai1 = ai.Ai1 + 1f };
                if (ai.Ai1 >= 4 + cadence * count) ResetAttack(ref ai, ref vx, ref vy);
                break;
            }
            default:
                ai = ai with { Ai0 = 0f, Ai1 = 0f };
                break;
        }

        sim = sim with
        {
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = local,
            DefenseOverride = phaseTwo ? (int)(definition.Defense * .65f) : definition.Defense,
            Chaseable = chaseable,
            DontTakeDamage = dontTakeDamage,
            JustHit = false
        };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static bool TryClone(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        int rootSlot = (int)npc.Ai.Ai3;
        if (rootSlot < 0 || rootSlot > byte.MaxValue || !context.TryFindNpcPeer((byte)rootSlot, out NpcSnapshot root) ||
            root.TypeIdentity != VanillaNpcIds.LunaticCultist)
        { next = default; return false; }

        NpcAiState ai = npc.Ai with { Ai0 = root.Ai.Ai0, Ai1 = root.Ai.Ai1 };
        if (ai.Ai0 == 5f && npc.Simulation.JustHit)
        {
            NpcSimulationState dead = npc.Simulation with
            {
                Life = 0,
                TimeLeft = 0,
                NoGravity = true,
                NoTileCollide = true,
                DontTakeDamage = true,
                JustHit = false
            };
            next = LateBossMath.Build(in npc, 0f, 0f, root.Target, in ai, in dead);
            return true;
        }
        float vx = npc.VelocityX, vy = npc.VelocityY;
        if (ai.Ai0 == 1f) { vx = root.VelocityX; vy = root.VelocityY; }
        else { vx *= .9f; vy *= .9f; }
        int lifeMax = root.Simulation.LifeMax > 0 ? root.Simulation.LifeMax : 1;
        int life = root.Simulation.LifeMax > 0 ? root.Simulation.Life : lifeMax;
        NpcSimulationState sim = npc.Simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            DefenseOverride = life <= lifeMax / 2 ? (int)(definition.Defense * .65f) : definition.Defense,
            DontTakeDamage = ai.Ai0 != 5f,
            Alpha = root.Simulation.Alpha,
            JustHit = false
        };
        next = LateBossMath.Build(in npc, vx, vy, root.Target, in ai, in sim);
        return true;
    }

    private static bool HasHitOwnedClone(VanillaNpcBehaviorContext context, byte ownerSlot)
    {
        Span<NpcSnapshot> clones = stackalloc NpcSnapshot[6];
        int count = context.CopyOwnedNpcPeers(VanillaNpcIds.LunaticCultistClone, ownerSlot, clones);
        for (int i = 0; i < count; i++)
        {
            if (clones[i].Simulation.JustHit)
                return true;
        }
        return false;
    }


    private static bool TryAncientVision(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.AncientVision)
        { next = default; return false; }
        ushort target = npc.Target;
        if (!LateBossMath.TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcAiState local = npc.Simulation.LocalAi;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        if (local.Ai0 < 120f) local = local with { Ai0 = local.Ai0 + 1f };
        switch ((int)ai.Ai0)
        {
            case 0:
                ai = ai with { Ai0 = 1f, Ai1 = player.CenterX >= npc.PositionX + 30f ? 1f : -1f };
                break;
            case 1:
            {
                float direction = ai.Ai1 == 0f ? 1f : MathF.Sign(ai.Ai1);
                vx = Math.Clamp(vx + direction * .7f, -14f, 14f);
                float deltaY = Math.Clamp(player.CenterY - (npc.PositionY + 30f), -6f, 6f);
                vy = (vy * 2f + deltaY) / 3f;
                if ((direction > 0f && player.CenterX - (npc.PositionX + 30f) < -500f) ||
                    (direction < 0f && player.CenterX - (npc.PositionX + 30f) > 500f))
                    ai = ai with { Ai0 = 2f, Ai1 = player.CenterY < npc.PositionY + 50f ? -1f : 1f };
                break;
            }
            case 2:
                vy += MathF.Sign(ai.Ai1) * .3f;
                if (MathF.Sqrt(vx * vx + vy * vy) > 7f) { vx *= .9f; vy *= .9f; }
                if (vx > -1f && vx < 1f)
                    ai = ai with { Ai0 = 3f, Ai1 = player.CenterX >= npc.PositionX + 30f ? 1f : -1f };
                break;
            case 3:
                vx += MathF.Sign(ai.Ai1) * .6f;
                vy += player.CenterY < npc.PositionY + 30f ? -.3f : .3f;
                if (MathF.Sqrt(vx * vx + vy * vy) > 7f) { vx *= .9f; vy *= .9f; }
                if (vy > -1f && vy < 1f) ai = ai with { Ai0 = 0f };
                break;
            default:
                ai = ai with { Ai0 = 0f, Ai1 = 0f };
                break;
        }
        NpcSimulationState sim = npc.Simulation with { LocalAi = local, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static bool TryAncientLight(in NpcSnapshot npc, in VanillaNpcDefinition definition, out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.AncientLight)
        { next = default; return false; }
        NpcAiState ai = npc.Ai;
        NpcAiState local = npc.Simulation.LocalAi;
        NpcSimulationState sim = npc.Simulation;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        if (vy == 0f && ai.Ai0 >= 0f)
            ai = ai with { Ai0 = -1f, Ai1 = 0f };
        if (ai.Ai0 == -1f)
        {
            vx = 0f; vy = 0f;
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            if (ai.Ai1 >= 5f) sim = sim with { Life = 0, TimeLeft = 0 };
        }
        else
        {
            if (local.Ai0 == 0f)
            {
                local = local with { Ai0 = 1f };
                vx = ai.Ai2; vy = ai.Ai3;
            }
            ai = ai with { Ai0 = ai.Ai0 + 1f };
            if (ai.Ai0 > 60f)
            {
                float c = MathF.Cos(ai.Ai1), sn = MathF.Sin(ai.Ai1);
                (vx, vy) = (vx * c - vy * sn, vx * sn + vy * c);
            }
            if (ai.Ai0 > 120f) { vx *= .98f; vy *= .98f; }
            if (MathF.Sqrt(vx * vx + vy * vy) < .2f) { vx = 0f; vy = 0f; }
        }
        sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, npc.Target, in ai, in sim);
        return true;
    }

    private static bool TryAncientDoom(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.AncientDoom)
        { next = default; return false; }
        int rootSlot = (int)npc.Ai.Ai0;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState ai = npc.Ai;
        if (rootSlot < 0 || rootSlot > byte.MaxValue || ai.Ai1 < 0f ||
            !context.TryFindNpcPeer((byte)rootSlot, out NpcSnapshot root) || root.TypeIdentity != VanillaNpcIds.LunaticCultist)
        {
            sim = sim with { Life = 0, TimeLeft = 0, JustHit = false };
            next = LateBossMath.Build(in npc, 0f, 0f, npc.Target, in ai, in sim);
            return true;
        }
        int rootLifeMax = Math.Max(1, root.Simulation.LifeMax);
        int rate = root.Simulation.Life < rootLifeMax / 4 ? 3 : root.Simulation.Life < rootLifeMax / 2 ? 2 : 1;
        ai = ai with { Ai1 = ai.Ai1 + rate };
        if (ai.Ai1 >= 420f) sim = sim with { Life = 0, TimeLeft = 0 };
        sim = sim with { NoGravity = true, NoTileCollide = true, JustHit = false };
        next = LateBossMath.Build(in npc, npc.VelocityX, npc.VelocityY, root.Target, in ai, in sim);
        return true;
    }

    private static int SelectAttack(int cycle, bool phaseTwo)
    {
        if (phaseTwo)
        {
            int[] map = [0, 1, 0, 5, 0, 3, 0, 5, 0, 2, 0, 3, 0, 4];
            return map[Math.Abs(cycle) % map.Length];
        }
        int[] first = [0, 1, 0, 2, 0, 3, 0, 1, 0, 2, 0, 4];
        return first[Math.Abs(cycle) % first.Length];
    }

    private static void ResetAttack(ref NpcAiState ai, ref float vx, ref float vy)
    { ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai3 = ai.Ai3 + 1f }; vx = 0f; vy = 0f; }
}

/// <summary>
/// Authoritative state/motion slice for TerrariaServer 1.4.5.8 AI-120 Empress of Light. Attack projectile
/// patterns are intentionally separate from this NPC state owner; this keeps phase selection and server movement
/// truthful without pretending that unimplemented special projectile styles are ordinary arrows.
/// </summary>
internal sealed class VanillaEmpressOfLightNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity != VanillaNpcIds.EmpressOfLight || definition.AiStyle != VanillaNpcAiStyles.EmpressOfLight)
        { next = default; return false; }
        ushort target = npc.Target;
        bool hasTarget = LateBossMath.TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player);
        // Source AI_120 lets its retreat state fade over its normal 20+ ticks even after TargetClosest finds no player.
        // Other states retain the existing authoritative target-loss handoff until their source branches are modeled.
        if (!hasTarget && npc.Ai.Ai0 != 13f)
        {
            NpcAiState despawnAi = npc.Ai with { Ai0 = 13f, Ai1 = 0f };
            NpcSimulationState despawn = npc.Simulation with { TimeLeft = npc.Simulation.TimeLeft is < 0 or > 10 ? 10 : npc.Simulation.TimeLeft };
            next = LateBossMath.Build(in npc, npc.VelocityX, npc.VelocityY - .2f, target, in despawnAi, in despawn);
            return true;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        int lifeMax = sim.LifeMax > 0 ? sim.LifeMax : definition.LifeMax;
        int life = sim.LifeMax > 0 ? sim.Life : lifeMax;
        // AI_120's phase is owned by ai[3], and only state 10 promotes it at its tick 90 relocation.
        // Health at or below half requests that transition from the phase-one attack selector; it is not phase two itself.
        bool phaseTwo = ai.Ai3 is 1f or 3f;
        bool rageCondition = context.ShouldEmpressBeEnraged(in npc);
        if (life == lifeMax && rageCondition && ai.Ai3 is not 2f and not 3f)
            ai = ai with { Ai3 = ai.Ai3 + 2f };
        bool enraged = rageCondition || ai.Ai3 is 2f or 3f;
        bool expertCadence = context.ExpertMode || rageCondition;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        float cx = npc.PositionX + 50f, cy = npc.PositionY + 50f;
        int state = (int)ai.Ai0;
        int sourceState = state;
        float timer = ai.Ai1;
        bool vulnerable = state != 0 && state != 10;
        float contactDamageMultiplier = 1f;

        if (state == 0)
        {
            if (timer == 0f) { vx = 0f; vy = 5f; }
            // AI_120 exposes Opacity = ai[1] / 180 before advancing this timer.
            sim = sim with { Alpha = Math.Clamp(255 - (int)(timer / 180f * 255f), 0, 255) };
            vx *= .95f; vy *= .95f; timer += 1f;
            if (timer >= 180f) { state = 1; timer = 0f; }
        }
        else if (state == 1)
        {
            float prep = phaseTwo ? 20f : 45f;
            if (context.GoodWorld) prep *= .5f;
            if (timer <= 10f) LateBossMath.DashToward(cx, cy, player.CenterX, player.CenterY, 24f, ref vx, ref vy);
            else { vx *= .92f; vy *= .92f; }
            timer += 1f;
            if (timer >= prep)
            {
                float dx = player.CenterX - cx;
                float dy = player.CenterY - cy;
                bool targetTooFar = dx * dx + dy * dy > 6_400f * 6_400f;
                // Source selects state 13 when a genuinely enraged Empress reaches night or 53,400 daytime ticks.
                // The phase-one selector requests state 10 at half health; state 10 alone changes ai[3] at tick 90.
                state = targetTooFar || context.ShouldEmpressRetreat(in ai)
                    ? 13
                    : !phaseTwo && (float)life / lifeMax <= .5f
                        ? 10
                        : SelectEmpressAttack((int)ai.Ai2, phaseTwo, expertCadence);
                timer = 0f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
            }
        }
        else if (state == 10)
        {
            float transitionTail = 20f - (expertCadence ? 5f : 0f);
            vulnerable = timer < 30f || timer > 170f;
            vx *= .95f;
            vy *= .95f;
            if (timer == 90f)
            {
                if (ai.Ai3 == 0f) ai = ai with { Ai3 = 1f };
                else if (ai.Ai3 == 2f) ai = ai with { Ai3 = 3f };
                // NPC.Center is set after the state flip in the source, so convert its 100x100 body center to position.
                next = new NpcStateUpdate(npc.Type, npc.NetId, player.CenterX - 50f, player.CenterY - 300f, vx, vy, target,
                    ai with { Ai0 = 10f, Ai1 = timer + 1f }, sim with
                    {
                        NoGravity = true,
                        NoTileCollide = true,
                        DontTakeDamage = !vulnerable,
                        DamageOverride = enraged ? 9999 : definition.Damage,
                        DefenseOverride = definition.Defense,
                        Alpha = Math.Max(0, sim.Alpha - 5),
                        JustHit = false
                    });
                return true;
            }
            timer += 1f;
            if (timer >= 180f + transitionTail)
            {
                state = 1;
                timer = 0f;
                ai = ai with { Ai2 = 0f };
            }
        }
        else if (state == 13)
        {
            if (timer == 0f) { vx = 0f; vy = -7f; }
            vx *= .95f;
            vy *= .95f;
            bool targetTooFar = !hasTarget;
            if (hasTarget)
            {
                float dx = player.CenterX - cx;
                float dy = player.CenterY - cy;
                targetTooFar = dx * dx + dy * dy > 6_400f * 6_400f;
            }
            bool retreat = targetTooFar || context.ShouldEmpressRetreat(in ai);
            int alpha = Math.Clamp(sim.Alpha + (retreat ? 5 : -5), 0, 255);
            sim = sim with { Alpha = alpha };
            timer += 1f;
            if (timer >= 20f && (alpha == 0 || alpha == 255))
            {
                if (alpha == 255)
                    sim = sim with { TimeLeft = 0 };
                else
                {
                    state = 1;
                    timer = 0f;
                }
            }
        }
        else
        {
            // AI_120 states 8/9 expose the dash body only for ticks 6..40, then apply num16 = 1.5 through tick 90.
            if (state is 8 or 9)
            {
                vulnerable = timer is >= 6f and <= 40f;
                if (timer is > 40f and <= 90f)
                    contactDamageMultiplier = 1.5f;
            }
            float accel = state is 8 or 9 ? 1f : .5f;
            float speed = state is 8 or 9 ? 20f : 12f;
            float offsetX = state == 8 ? 550f : state == 9 ? -550f : state == 4 ? 150f : state == 2 ? -150f : 0f;
            float offsetY = state is 4 or 2 ? -250f : state is 5 or 6 or 7 or 11 ? -350f : -250f;
            if (state is not 10 and not 13)
                LateBossMath.FlyToward(cx, cy, player.CenterX + offsetX, player.CenterY + offsetY, speed, accel, ref vx, ref vy);
            if (state == 10) { vx *= .95f; vy *= .95f; }
            if (state == 13) { vx *= .95f; vy -= .05f; }
            timer += 1f;
            float duration = StateDuration(state, phaseTwo, expertCadence);
            if (timer >= duration) { state = state == 13 ? 13 : 1; timer = 0f; }
        }

        ai = ai with { Ai0 = state, Ai1 = timer };
        if (sourceState is not 0 and not 13)
            sim = sim with { Alpha = Math.Max(0, sim.Alpha - 5) };
        sim = sim with
        {
            NoGravity = true,
            NoTileCollide = true,
            DontTakeDamage = !vulnerable,
            DamageOverride = enraged ? 9999 : (int)(definition.Damage * contactDamageMultiplier),
            DefenseOverride = phaseTwo ? (int)(definition.Defense * 1.2f) : definition.Defense,
            JustHit = false
        };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static int SelectEmpressAttack(int cycle, bool phaseTwo, bool expert)
    {
        if (!phaseTwo)
        {
            int[] map = [2, 8, 6, 8, 5, 2, 8, 4, 8, 5];
            return map[Math.Abs(cycle) % map.Length];
        }
        int[] phase = expert ? [7, 2, 8, 11, 5, 2, 6, 4, 8, 12] : [7, 2, 8, 5, 2, 6, 4, 8, 12];
        return phase[Math.Abs(cycle) % phase.Length];
    }

    private static float StateDuration(int state, bool phaseTwo, bool expert)
    {
        int bonus = (phaseTwo ? 15 : 0) + (expert ? 5 : 0);
        return state switch
        {
            2 => 150 - bonus,
            3 => 120,
            4 => 100 - bonus,
            5 => 120 - bonus,
            6 => 120 - bonus,
            7 => (expert ? 240 : 240) + (20 - bonus),
            8 or 9 => 90 + (20 - bonus),
            10 => 180 + (20 - bonus),
            11 => 100 + (20 - bonus),
            12 => 150 - bonus,
            13 => 120,
            _ => 90
        };
    }
}

/// <summary>
/// Server-owned linkage/state slice for TerrariaServer 1.4.5.8 Moon Lord AI 77/78/79/81. The core creates and
/// owns its hands/head, transitions vulnerable after the linked shell is gone, and True Eyes remain bound to the
/// same root. Hand/head/True-Eye attack clocks follow the source sequence so the authoritative projectile planner
/// can reproduce the Phantasmal attack families without pushing presentation-only effects into the simulation.
/// </summary>
internal sealed class VanillaMoonLordNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random;

    public VanillaMoonLordNpcBehaviorStrategy(IVanillaNpcRandom random) => this.random = random;

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordLeechBlob) return VanillaMoonLordLeechBehavior.TryStep(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordCore) return TryCore(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordHand) return TryPart(in npc, in definition, context, isHead: false, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordHead) return TryPart(in npc, in definition, context, isHead: true, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordFreeEye) return TryEye(in npc, in definition, context, out next);
        next = default; return false;
    }

    private bool TryCore(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        // Even on a dedicated server, the original sound decision advances the shared RNG.
        // It precedes initialization and excludes only incoming intro/death states.
        if (npc.Ai.Ai0 is not (-1f or 2f) && random.NextInt32(0, 200) == 0)
            _ = random.NextInt32(93, 100);
        NpcSnapshot initialized = npc;
        if (npc.Simulation.LocalAi.Ai3 == 0f)
            initialized = npc with
            {
                Ai = npc.Ai with { Ai0 = -1f },
                Simulation = npc.Simulation with { LocalAi = npc.Simulation.LocalAi with { Ai3 = 1f } }
            };
        return TryInitializedCore(in initialized, in definition, context, out next);
    }

    private bool TryInitializedCore(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        // NPC.AI_077_MoonLordCore (1.4.5.8): death drama is independent of target availability.
        // The first lethal strike was intercepted by checkDead; only tick 600 makes the core terminal.
        if (npc.Ai.Ai0 == 2f)
        {
            NpcAiState deathAi = npc.Ai with { Ai1 = npc.Ai.Ai1 + 1f };
            NpcSimulationState death = npc.Simulation with
            {
                Life = deathAi.Ai1 >= 600f ? 0 : npc.Simulation.Life,
                NoGravity = true,
                NoTileCollide = true,
                DontTakeDamage = true,
                JustHit = false
            };
            float deathVx = npc.VelocityX + (0f - npc.VelocityX) * .98f;
            float deathVy = npc.VelocityY + (-.5f - npc.VelocityY) * .98f;
            next = LateBossMath.Build(in npc, deathVx, deathVy, npc.Target, in deathAi, in death);
            return true;
        }

        // AI_077 state 3 is departure, not combat death. It must progress without a target,
        // preserve life, and leave cleanup/event effects to the exact-generation commit boundary.
        if (npc.Ai.Ai0 == 3f)
        {
            NpcAiState fleeAi = npc.Ai with { Ai1 = npc.Ai.Ai1 + 1f };
            NpcSimulationState flee = npc.Simulation with
            {
                NoGravity = true, NoTileCollide = true, DontTakeDamage = true, JustHit = false,
                TimeLeft = fleeAi.Ai1 >= 60f ? 0 : npc.Simulation.TimeLeft
            };
            float fleeVx = npc.VelocityX + (npc.Simulation.DirectionX - npc.VelocityX) * .98f;
            float fleeVy = npc.VelocityY + (-.5f - npc.VelocityY) * .98f;
            next = LateBossMath.Build(in npc, fleeVx, fleeVy, npc.Target, in fleeAi, in flee);
            return true;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        bool allocateShell = false;
        if (ai.Ai0 is -1f or -2f)
        {
            bool introduction = ai.Ai0 == -1f;
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            sim = sim with { NoGravity = true, NoTileCollide = true, DontTakeDamage = true, JustHit = false };
            if (ai.Ai1 != 60f)
            {
                next = LateBossMath.Build(in npc, npc.VelocityX, npc.VelocityY, npc.Target, in ai, in sim);
                return true;
            }
            if (!introduction) _ = random.NextInt32(0, 3); // Original immediately overwrites the roll with zero.
            ai = ai with { Ai0 = 0f, Ai1 = 0f, Ai2 = 0f };
            allocateShell = introduction;
            if (allocateShell)
                local = local with { Ai0 = -1f, Ai1 = -1f, Ai2 = -1f };
        }

        // The protected core checks the three slots retained at shell allocation, not a scan for
        // replacement parts. AI_077 removes a broken shell directly, without checkDead/loot/progression.
        if (ai.Ai0 == 0f && !allocateShell && !TryShell(context, local, out _))
            return RetireOrphan(in npc, out next);

        ushort target = npc.Target;
        // AI_077 calls TargetClosest(false) every protected/exposed pursuit tick.
        if (ai.Ai0 is 0f or 1f && context.TrySelectClosestTarget(in npc, in definition, out var closest) && closest.HasTarget)
            target = closest.Target;
        if (!LateBossMath.TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
        {
            if (ai.Ai0 is 0f or 1f)
            {
                // TargetClosest retains its previous slot when no living target exists. AI_077
                // still performs the final pursuit step before deciding to depart. A disconnected
                // slot has the fresh Player geometry installed by RemoteClient.Reset.
                target = npc.Target < byte.MaxValue ? npc.Target : (ushort)0;
                if (!context.TryFindCandidate((byte)target, out player))
                    player = new VanillaNpcTargetCandidate((byte)target,
                        VanillaPlayerHitboxFacts.BaseWidth * .5f, VanillaPlayerHitboxFacts.BaseHeight * .5f,
                        0, false, false, false, false);
            }
            else
            {
                next = default;
                return false;
            }
        }
        float vx = npc.VelocityX, vy = npc.VelocityY;
        bool invulnerable = true;
        if (ai.Ai0 == 0f || ai.Ai0 == 1f)
        {
            float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;
            MoveCoreToward(cx, cy, player.CenterX, player.CenterY + 130f, ref vx, ref vy);
            if (ai.Ai0 == 0f && !allocateShell && TryShell(context, local, out bool retired) && retired)
                ai = ai with { Ai0 = 1f };
            invulnerable = ai.Ai0 != 1f;
        }
        if (ai.Ai0 is 0f or 1f && !context.HasLivingPlayer)
            ai = ai with { Ai0 = 3f, Ai1 = 0f };
        sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, DontTakeDamage = invulnerable, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        if (ai.Ai0 is 0f or 1f)
        {
            float dx = player.CenterX - (npc.PositionX + 23f);
            float dy = player.CenterY - (npc.PositionY + 33f);
            if (MathF.Sqrt(dx * dx + dy * dy) > 2400f)
                next = next with
                {
                    PositionX = npc.PositionX + dx,
                    PositionY = npc.PositionY + ((player.CenterY - 150f) - (npc.PositionY + 33f)),
                    Ai = ai with { Ai0 = -2f }
                };
        }
        return true;
    }

    internal static void ApplyTeleportToParts(in NpcSnapshot before, in NpcSnapshot committed,
        VanillaNpcBehaviorContext context, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.MoonLordCore || committed.Ai.Ai0 != -2f ||
            (before.Ai.Ai0 == -2f && before.Ai.Ai1 + 1f != 60f) ||
            committed.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate player))
            return;
        // AI_077 translates after NewNPC, but before the core's outer world motion.
        // Derive the source-space delta rather than including that subsequent motion in peer offsets.
        float dx = player.CenterX - (before.PositionX + 23f);
        float dy = (player.CenterY - 150f) - (before.PositionY + 33f);
        // The source marks core and moved parts netUpdate, bypassing ordinary motion cadence.
        mutations.TryTranslate(committed.Handle, 0f, 0f, out _);
        NpcAiState slots = committed.Simulation.LocalAi;
        TranslateSlot(slots.Ai0, dx, dy, mutations);
        TranslateSlot(slots.Ai1, dx, dy, mutations);
        TranslateSlot(slots.Ai2, dx, dy, mutations);
        for (int slot = 0; slot < RuntimeNpcStore.MaximumAddressableCapacity; slot++)
            if (mutations.TryGetActive((byte)slot, out NpcSnapshot peer) && peer.TypeIdentity == VanillaNpcIds.MoonLordFreeEye)
                mutations.TryTranslate(peer.Handle, dx, dy, out _);
    }

    private static void TranslateSlot(float slot, float dx, float dy, INpcAiCommittedNpcMutationSink mutations)
    {
        // Runtime rejects invalid internal links; valid repeated slots retain the source's repeated translation.
        if (float.IsFinite(slot) && slot >= 0f && slot < RuntimeNpcStore.MaximumAddressableCapacity &&
            mutations.TryGetActive((byte)slot, out NpcSnapshot peer))
            mutations.TryTranslate(peer.Handle, dx, dy, out _);
    }

    private static void MoveCoreToward(float x, float y, float targetX, float targetY, ref float vx, ref float vy)
    {
        // AI_077 steers toward (displacement - velocity), runs SimpleFlyMovement at 0.5,
        // then blends equally with the previous velocity. Reversal accelerates twice; there is no clamp.
        float dx = targetX - x, dy = targetY - y;
        if (MathF.Sqrt(dx * dx + dy * dy) <= 20f)
            return;
        float steeringX = dx - vx, steeringY = dy - vy;
        float length = MathF.Sqrt(steeringX * steeringX + steeringY * steeringY);
        if (length == 0f)
            return; // Preserve the finite simulation invariant for a degenerate steering vector.
        float inverseLength = 1f / length;
        float desiredX = steeringX * inverseLength * 8f;
        float desiredY = steeringY * inverseLength * 8f;
        vx = BlendCoreAxis(vx, desiredX);
        vy = BlendCoreAxis(vy, desiredY);
    }

    private static float BlendCoreAxis(float velocity, float desired)
    {
        float delta = desired > velocity ? .5f : desired < velocity ? -.5f : 0f;
        float accelerated = velocity + delta;
        if ((accelerated < 0f && desired > 0f) || (accelerated > 0f && desired < 0f))
            accelerated += delta;
        return accelerated + (velocity - accelerated) * .5f;
    }

    private bool TryPart(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, bool isHead, out NpcStateUpdate next)
    {
        if (!TryRoot(in npc, context, out NpcSnapshot root)) return RetireOrphan(in npc, out next);
        return isHead
            ? VanillaMoonLordHeadBehavior.TryStep(in npc, in definition, in root, context, random, out next)
            : VanillaMoonLordHandBehavior.TryStep(in npc, in definition, in root, context, out next);
    }

    private static bool TryShell(VanillaNpcBehaviorContext context, NpcAiState slots, out bool retired)
    {
        retired = false;
        if (!TryShellPart(context, slots.Ai0, VanillaNpcIds.MoonLordHand, out NpcSnapshot left) ||
            !TryShellPart(context, slots.Ai1, VanillaNpcIds.MoonLordHand, out NpcSnapshot right) ||
            !TryShellPart(context, slots.Ai2, VanillaNpcIds.MoonLordHead, out NpcSnapshot head))
            return false;
        retired = left.Ai.Ai0 == -2f && right.Ai.Ai0 == -2f && head.Ai.Ai0 == -2f;
        return true;
    }

    private static bool TryShellPart(VanillaNpcBehaviorContext context, float slot, NpcTypeId type,
        out NpcSnapshot part)
    {
        part = default;
        return float.IsFinite(slot) && slot >= 0f && slot <= byte.MaxValue && slot == MathF.Truncate(slot) &&
            context.TryFindNpcPeer((byte)slot, out part) && part.TypeIdentity == type;
    }

    private static bool TryEye(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!TryRoot(in npc, context, out NpcSnapshot root))
            return RetireOrphan(in npc, out next);
        ushort target = root.Target;
        if (target >= byte.MaxValue || !context.TryFindCandidate((byte)target, out VanillaNpcTargetCandidate player))
        { next = default; return false; }
        NpcAiState ai = AdvanceEyeAttackClock(npc.Ai);
        float angle = (npc.Handle.Slot % 3) * 2.0943952f + ai.Ai1 * .012f;
        float desiredX = player.CenterX + MathF.Cos(angle) * 320f;
        float desiredY = player.CenterY - 180f + MathF.Sin(angle) * 180f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        LateBossMath.FlyToward(npc.PositionX + 30f, npc.PositionY + 30f, desiredX, desiredY, 12f, .45f, ref vx, ref vy);
        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true, DontTakeDamage = true, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static NpcAiState AdvanceEyeAttackClock(in NpcAiState before)
    {
        float timer = before.Ai1 + 1f;
        if (!float.IsFinite(timer) || timer >= 1200f || timer < 0f)
            timer = 0f;
        int state = ResolveEyeAttackState((int)timer);
        return before with { Ai0 = state, Ai1 = timer };
    }

    private static int ResolveEyeAttackState(int timer)
    {
        ReadOnlySpan<int> states = [0, 1, 0, 2, 0, 3, 0, 4, 0, 2];
        ReadOnlySpan<int> durations = [53, 90, 53, 135, 53, 200, 53, 375, 53, 135];
        int cursor = 0;
        for (int i = 0; i < states.Length; i++)
        {
            cursor += durations[i];
            if (timer < cursor)
                return states[i];
        }
        return 0;
    }

    private static bool RetireOrphan(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        // NPC.AI_078/079/081 (1.4.5.8) deactivate a part whose ai[3] owner is absent or not a core.
        // TimeLeft=0 carries that removal through the authoritative store after the state commit.
        NpcSimulationState terminal = npc.Simulation with
        {
            Life = 0, TimeLeft = 0, DontTakeDamage = true, DamageOverride = 0, JustHit = false
        };
        NpcAiState ai = npc.Ai;
        next = LateBossMath.Build(in npc, 0f, 0f, npc.Target, in ai, in terminal);
        return true;
    }

    private static bool TryRoot(in NpcSnapshot child, VanillaNpcBehaviorContext context, out NpcSnapshot root)
    {
        root = default;
        float slot = child.Ai.Ai3;
        return float.IsFinite(slot) && slot >= 0f && slot < byte.MaxValue && slot == MathF.Truncate(slot) &&
               context.TryFindNpcPeer((byte)slot, out root) && root.TypeIdentity == VanillaNpcIds.MoonLordCore;
    }
}

internal static class LateBossMath
{
    public static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        ref ushort targetSlot, out VanillaNpcTargetCandidate target)
    {
        if (targetSlot < byte.MaxValue && context.TryFindCandidate((byte)targetSlot, out target) && target.Active && !target.Dead && !target.Ghost)
            return true;
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh refresh) && refresh.HasTarget &&
            refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out target) && target.Active && !target.Dead && !target.Ghost)
        { targetSlot = refresh.Target; return true; }
        target = default; return false;
    }

    public static void FlyToward(float x, float y, float tx, float ty, float speed, float accel, ref float vx, ref float vy)
    {
        float dx = tx - x, dy = ty - y, d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        Approach(ref vx, dx / d * speed, accel); Approach(ref vy, dy / d * speed, accel);
    }
    public static void DashToward(float x, float y, float tx, float ty, float speed, ref float vx, ref float vy)
    { float dx = tx - x, dy = ty - y, d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy)); vx = dx / d * speed; vy = dy / d * speed; }
    public static float Distance(float x, float y, float tx, float ty) { float dx = tx - x, dy = ty - y; return MathF.Sqrt(dx * dx + dy * dy); }
    private static void Approach(ref float value, float target, float amount)
    { if (value < target) value = MathF.Min(value + amount, target); else if (value > target) value = MathF.Max(value - amount, target); }
    public static NpcStateUpdate Build(in NpcSnapshot npc, float vx, float vy, ushort target, in NpcAiState ai, in NpcSimulationState sim) =>
        new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, vx, vy, target, ai, sim);
}
