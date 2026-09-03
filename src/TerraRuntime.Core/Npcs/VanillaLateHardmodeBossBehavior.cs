using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core;

/// <summary>
/// Authoritative TerrariaServer 1.4.5.8 AI-84 state slice for Lunatic Cultist and its ritual clones.
/// Root/clone state synchronization, ritual timing, phase defense and movement are server-owned; specialized
/// projectile AI and Ancient Vision/Dragon attack children remain separately tracked side-effect work.
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
                int count = 2;
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

    private static int SelectAttack(int cycle, bool phaseTwo)
    {
        if (phaseTwo)
        {
            int[] map = [0, 3, 0, 5, 0, 4, 0, 5, 0, 2, 0, 4, 0, 5];
            return map[Math.Abs(cycle) % map.Length];
        }
        int[] first = [0, 3, 0, 2, 0, 4, 0, 3, 0, 2, 0, 5];
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
        if (phaseTwo && ai.Ai3 == 0f) ai = ai with { Ai3 = 1f };
        if (phaseTwo && ai.Ai3 == 2f) ai = ai with { Ai3 = 3f };
        bool enraged = ai.Ai3 is 2f or 3f;
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
                state = SelectEmpressAttack((int)ai.Ai2, phaseTwo, context.ExpertMode);
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
            float duration = StateDuration(state, phaseTwo, context.ExpertMode);
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
/// same root. Specialized Phantasmal projectile patterns and the source's 600-tick presentation death drama are
/// deliberately not forged into generic projectile behavior.
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
            bool hands = context.HasOwnedNpcPeer(VanillaNpcIds.MoonLordHand, npc.Handle.Slot);
            bool head = context.HasOwnedNpcPeer(VanillaNpcIds.MoonLordHead, npc.Handle.Slot);
            if (!hands && !head)
            {
                ai = ai with { Ai0 = 1f };
                if (local.Ai2 == 0f) local = local with { Ai2 = 1f };
            }
            invulnerable = ai.Ai0 != 1f;
        }
        else if (ai.Ai0 == 2f)
        {
            vx = vx * .98f; vy = vy * .98f - .01f;
            ai = ai with { Ai1 = ai.Ai1 + 1f };
            invulnerable = true;
        }
        sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, DontTakeDamage = invulnerable, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static bool TryPart(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, bool isHead, out NpcStateUpdate next)
    {
        if (!TryRoot(in npc, context, out NpcSnapshot root)) { next = default; return false; }
        float rootCx = root.PositionX + 23f, rootCy = root.PositionY + 33f;
        float offsetX = isHead ? 0f : (npc.Ai.Ai2 <= 0f ? -400f : 400f);
        float offsetY = isHead ? -400f : -100f;
        float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        LateBossMath.FlyToward(cx, cy, rootCx + offsetX, rootCy + offsetY, 18f, 1.2f, ref vx, ref vy);
        NpcAiState ai = npc.Ai;
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

    private static bool TryEye(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        if (!TryRoot(in npc, context, out NpcSnapshot root)) { next = default; return false; }
        ushort target = root.Target;
        if (target >= byte.MaxValue || !context.TryFindCandidate((byte)target, out VanillaNpcTargetCandidate player))
        { next = default; return false; }
        NpcAiState ai = npc.Ai with { Ai0 = npc.Ai.Ai0 + 1f };
        float angle = (npc.Handle.Slot % 3) * 2.0943952f + ai.Ai0 * .012f;
        float desiredX = player.CenterX + MathF.Cos(angle) * 320f;
        float desiredY = player.CenterY - 180f + MathF.Sin(angle) * 180f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        LateBossMath.FlyToward(npc.PositionX + 30f, npc.PositionY + 30f, desiredX, desiredY, 12f, .45f, ref vx, ref vy);
        NpcSimulationState sim = npc.Simulation with { NoGravity = true, NoTileCollide = true, DontTakeDamage = true, JustHit = false };
        next = LateBossMath.Build(in npc, vx, vy, target, in ai, in sim);
        return true;
    }

    private static bool TryRoot(in NpcSnapshot child, VanillaNpcBehaviorContext context, out NpcSnapshot root)
    {
        int encodedLocal = (int)child.Simulation.LocalAi.Ai3 - 1;
        if (encodedLocal >= 0 && encodedLocal <= byte.MaxValue && context.TryFindNpcPeer((byte)encodedLocal, out root) && root.TypeIdentity == VanillaNpcIds.MoonLordCore)
            return true;
        int raw = (int)child.Ai.Ai3;
        if (raw >= 0 && raw <= byte.MaxValue && context.TryFindNpcPeer((byte)raw, out root) && root.TypeIdentity == VanillaNpcIds.MoonLordCore)
            return true;
        return context.TryFindFirstNpcPeer(VanillaNpcIds.MoonLordCore, out root);
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
