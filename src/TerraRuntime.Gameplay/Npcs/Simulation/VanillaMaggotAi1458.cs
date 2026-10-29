using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>AI066's ground-only idle/walk clock for Maggot, TerrariaServer 1.4.5.8.</summary>
public static class VanillaMaggotAi1458
{
    public static bool TryStep(in NpcSnapshot source, IVanillaNpcRandom random, out NpcSnapshot next, out bool force)
    {
        next = default; force = false;
        if (source.TypeIdentity != VanillaNpcIds.Maggot || source.NetIdentity.Value != source.Type ||
            source.Simulation.DirectionX is < -1 or > 1) return false;
        float vx = source.VelocityX;
        var ai = source.Ai;
        var local = source.Simulation.LocalAi;
        int direction = source.Simulation.DirectionX;
        if (source.VelocityY == 0f)
        {
            if (ai.Ai0 == 1f)
            {
                // The caller performs source TargetClosest only for direction zero before entering this rule.
                if (direction == 0) return false;
                if (source.Simulation.CollideX) direction = -direction;
                vx = .2f * direction;
            }
            else vx = 0f;
            local = local with { Ai1 = local.Ai1 - 1f };
            if (local.Ai1 <= 0f)
            {
                bool wasWalking = ai.Ai0 == 1f;
                ai = ai with { Ai0 = wasWalking ? 0f : 1f };
                local = local with { Ai1 = wasWalking ? random.NextInt32(300, 900) : random.NextInt32(600, 1800) };
                force = true;
            }
        }
        else if (direction == 0) direction = vx < 0f ? -1 : 1;
        next = source with { VelocityX = vx, Ai = ai,
            Simulation = source.Simulation with { DirectionX = direction, LocalAi = local } };
        return next.Simulation.IsValid;
    }
}
