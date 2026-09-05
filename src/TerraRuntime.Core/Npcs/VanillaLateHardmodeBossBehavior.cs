using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

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

        switch ((int)ai.Ai0)
        {
            case -1:
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
        if (!LateBossMath.TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
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
        bool phaseTwo = life <= lifeMax / 2;
        bool rageCondition = context.DayTime;
        if (life == lifeMax && rageCondition && ai.Ai3 is not 2f and not 3f)
            ai = ai with { Ai3 = ai.Ai3 + 2f };
        if (phaseTwo && ai.Ai3 == 0f) ai = ai with { Ai3 = 1f };
        if (phaseTwo && ai.Ai3 == 2f) ai = ai with { Ai3 = 3f };
        bool enraged = rageCondition || ai.Ai3 is 2f or 3f;
        bool expertCadence = context.ExpertMode || rageCondition;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        float cx = npc.PositionX + 50f, cy = npc.PositionY + 50f;
        int state = (int)ai.Ai0;
        float timer = ai.Ai1;
        bool vulnerable = state != 0 && state != 10;

        if (state == 0)
        {
            if (timer == 0f) { vx = 0f; vy = 5f; }
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
                state = SelectEmpressAttack((int)ai.Ai2, phaseTwo, expertCadence);
                timer = 0f;
                ai = ai with { Ai2 = ai.Ai2 + 1f };
            }
        }
        else
        {
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
        sim = sim with
        {
            NoGravity = true,
            NoTileCollide = true,
            DontTakeDamage = !vulnerable,
            DamageOverride = enraged ? 9999 : definition.Damage,
            DefenseOverride = definition.Defense,
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
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordCore) return TryCore(in npc, in definition, context, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordHand) return TryPart(in npc, in definition, context, isHead: false, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordHead) return TryPart(in npc, in definition, context, isHead: true, out next);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordFreeEye) return TryEye(in npc, in definition, context, out next);
        next = default; return false;
    }

    private static bool TryCore(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
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

        ushort target = npc.Target;
        if (!LateBossMath.TryTarget(in npc, in definition, context, ref target, out VanillaNpcTargetCandidate player))
        { next = default; return false; }
        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        if (local.Ai3 == 0f) { local = local with { Ai3 = 1f }; ai = ai with { Ai0 = -1f, Ai1 = 0f }; }
        float vx = npc.VelocityX, vy = npc.VelocityY;
        bool invulnerable = true;
        if (ai.Ai0 is -1f or -2f)
        {
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            vx *= .98f; vy *= .98f;
            if (ai.Ai1 >= 60f) ai = ai with { Ai0 = 0f, Ai1 = 0f };
        }
        else if (ai.Ai0 == 0f || ai.Ai0 == 1f)
        {
            float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;
            LateBossMath.FlyToward(cx, cy, player.CenterX, player.CenterY + 130f, 8f, .25f, ref vx, ref vy);
            if (ai.Ai0 == 0f && HasRetiredShell(context, npc.Handle.Slot))
                ai = ai with { Ai0 = 1f };
            invulnerable = ai.Ai0 != 1f;
        }
        sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, DontTakeDamage = invulnerable, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static bool TryPart(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, bool isHead, out NpcStateUpdate next)
    {
        if (!TryRoot(in npc, context, out NpcSnapshot root))
            return RetireOrphan(in npc, out next);
        if (npc.Ai.Ai0 is -2f or -3f)
            return TryRetiredPart(in npc, in definition, in root, isHead, out next);

        float rootCx = root.PositionX + 23f, rootCy = root.PositionY + 33f;
        float offsetX = isHead ? 0f : (npc.Ai.Ai2 <= 0f ? -400f : 400f);
        float offsetY = isHead ? -400f : -100f;
        float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        LateBossMath.FlyToward(cx, cy, rootCx + offsetX, rootCy + offsetY, 18f, 1.2f, ref vx, ref vy);
        NpcAiState ai = AdvancePartAttackClock(npc.Ai, isHead);
        NpcSimulationState sim = npc.Simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            DontTakeDamage = false,
            JustHit = false
        };
        next = LateBossMath.Build(in npc, vx, vy, root.Target, in ai, in sim);
        return true;
    }

    private static bool TryRetiredPart(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in NpcSnapshot root,
        bool isHead,
        out NpcStateUpdate next)
    {
        float rootCx = root.PositionX + 23f;
        float rootCy = root.PositionY + 33f;
        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            DontTakeDamage = true,
            DamageOverride = 0,
            JustHit = false
        };

        if (isHead)
        {
            float headX = rootCx - definition.Width * .5f;
            float headY = rootCy - 400f - definition.Height * .5f;
            float timer = ai.Ai1 + 1f;
            if (!float.IsFinite(timer) || timer >= 1200f || timer < 0f)
                timer = 0f;
            ai = ai with { Ai1 = timer };

            if (ai.Ai0 == -2f && root.Ai.Ai0 == 2f)
            {
                ai = ai with { Ai0 = -3f };
            }
            else
            {
                float deathFrame = ai.Ai2 + 1f;
                if (!float.IsFinite(deathFrame) || deathFrame >= 32f || deathFrame < 0f)
                    deathFrame = 0f;
                ai = ai with { Ai2 = deathFrame };
                if (ai.Ai0 == -3f && sim.LocalAi.Ai2 < 14f)
                    sim = sim with { LocalAi = sim.LocalAi with { Ai2 = sim.LocalAi.Ai2 + 1f } };
            }

            next = new NpcStateUpdate(
                npc.Type, npc.NetId, headX, headY, 0f, 0f, root.Target, ai, sim);
            return true;
        }

        float side = ai.Ai2 == 0f ? -1f : 1f;
        float handCx = npc.PositionX + definition.Width * .5f;
        float handCy = npc.PositionY + definition.Height * .5f;
        float vx = npc.VelocityX;
        float vy = npc.VelocityY;
        float desiredX = rootCx + 350f * side;
        float desiredY = rootCy - 100f;
        float dx = desiredX - handCx;
        float dy = desiredY - handCy;
        if (MathF.Sqrt(dx * dx + dy * dy) > 20f)
        {
            float beforeX = vx;
            float beforeY = vy;
            LateBossMath.FlyToward(handCx, handCy, desiredX, desiredY, 6f, .3f, ref vx, ref vy);
            vx = (beforeX + vx) * .5f;
            vy = (beforeY + vy) * .5f;
        }

        float handTimer = ai.Ai1 + 1f;
        if (!float.IsFinite(handTimer) || handTimer >= 32f || handTimer < 0f)
            handTimer = 0f;
        ai = ai with { Ai1 = handTimer };
        next = LateBossMath.Build(in npc, vx, vy, root.Target, in ai, in sim);
        return true;
    }

    private static bool HasRetiredShell(VanillaNpcBehaviorContext context, byte rootSlot)
    {
        Span<NpcSnapshot> hands = stackalloc NpcSnapshot[2];
        Span<NpcSnapshot> heads = stackalloc NpcSnapshot[1];
        int handCount = context.CopyOwnedNpcPeers(VanillaNpcIds.MoonLordHand, rootSlot, hands);
        int headCount = context.CopyOwnedNpcPeers(VanillaNpcIds.MoonLordHead, rootSlot, heads);
        return handCount == 2 && headCount == 1 &&
               hands[0].Ai.Ai0 == -2f && hands[1].Ai.Ai0 == -2f && heads[0].Ai.Ai0 == -2f;
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

    private static NpcAiState AdvancePartAttackClock(in NpcAiState before, bool isHead)
    {
        int row = isHead ? 2 : before.Ai2 <= 0f ? 0 : 1;
        float timer = before.Ai1 + 1f;
        int total = row == 2 ? 1200 : 600;
        if (!float.IsFinite(timer) || timer >= total || timer < 0f)
            timer = 0f;

        int state = ResolvePartAttackState((int)timer, row);
        return before with { Ai0 = state, Ai1 = timer };
    }

    private static int ResolvePartAttackState(int timer, int row)
    {
        ReadOnlySpan<int> states = row switch
        {
            0 => [0, 1, 2, 0, 3],
            1 => [1, 0, 3, 0, 2],
            _ => [3, 0, 2, 3, 1]
        };
        ReadOnlySpan<int> durations = row switch
        {
            0 => [50, 70, 330, 60, 90],
            1 => [70, 50, 90, 60, 330],
            _ => [180, 30, 435, 180, 375]
        };
        int cursor = 0;
        for (int i = 0; i < states.Length; i++)
        {
            cursor += durations[i];
            if (timer < cursor)
                return states[i];
        }
        return states[0];
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
