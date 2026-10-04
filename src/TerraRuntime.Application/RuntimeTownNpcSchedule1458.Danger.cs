using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    // Speculative source prelude: caller completes body/combat/physics before a single store mutation.
    private bool TryPlanDangerResponse(in NpcSnapshot before, in RuntimeTownNpcHomeCommit home,
        in RuntimeTownNpcDanger1458 scanned, bool activeTalk, out NpcSnapshot staged,
        out RuntimeTownNpcDanger1458 danger, out bool force)
    {
        staged = before;
        danger = scanned;
        force = false;
        if (!danger.WithinRange || activeTalk) return true;
        NpcAiState ai = before.Ai;
        NpcAiState local = before.Simulation.LocalAi;
        int direction = before.Simulation.DirectionX == 0 ? 1 : before.Simulation.DirectionX;
        int away = -danger.ThreatDirection;
        if (ai.Ai0 == 8f)
        {
            if (direction == away)
            {
                ai = ai with { Ai0 = 1f, Ai1 = 300 + random.Next(300), Ai2 = 0f };
                local = local with { Ai3 = 0f };
                force = true;
            }
        }
        else if (ai.Ai0 is not (10f or 12f or 13f or 14f or 15f))
        {
            int safe = VanillaTownNpcDangerCatalog1458.PrettySafeRange(before.Type);
            if (safe != -1 && safe < danger.NearestHorizontalDistance)
                danger = danger with { WithinRange = false };
            else if (ai.Ai0 != 1f)
            {
                // Paired social states require a two-actor plan; not admitted by this resident-only prelude.
                if (ai.Ai0 is 3f or 4f or 16f or 17f) return false;
                int width = GetWidth(home.NpcType), height = GetHeight(home.NpcType);
                int myX = (int)((before.PositionX + width / 2) / 16f);
                int aheadX = (int)((before.PositionX + width / 2 + 15 * direction) / 16f);
                int feetY = (int)((before.PositionY + height - 16f) / 16f);
                if (!AvoidDryFall(myX, home.HomeTileX, direction, aheadX, feetY))
                {
                    ai = ai with { Ai0 = 1f, Ai1 = 120 + random.Next(120), Ai2 = 0f };
                    local = local with { Ai3 = 0f };
                    direction = away;
                    force = true;
                }
            }
            else if (direction != away)
            {
                direction = away;
                force = true;
            }
        }
        staged = before with { Ai = ai,
            Simulation = before.Simulation with { DirectionX = direction, LocalAi = local } };
        return true;
    }

    private bool TryPlanBlockedFlee(in NpcSnapshot before, in RuntimeTownNpcDanger1458 danger,
        out NpcStateUpdate update, out bool force)
    {
        update = default;
        force = false;
        if (before.Ai.Ai0 != 8f) return false;
        NpcAiState ai = before.Ai with { Ai1 = before.Ai.Ai1 - 1f };
        NpcAiState local = before.Simulation.LocalAi;
        if (ai.Ai1 < 60f && danger.WithinRange)
        {
            ai = ai with { Ai1 = 180f };
            force = true;
        }
        if (ai.Ai1 <= 0f)
        {
            ai = ai with { Ai0 = 0f, Ai1 = 60 + random.Next(60), Ai2 = 0f };
            local = local with { Ai3 = 30 + random.Next(60) };
            force = true;
        }
        update = new(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX * .8f, before.VelocityY, before.Target, ai,
            before.Simulation with { LocalAi = local });
        return true;
    }
}
