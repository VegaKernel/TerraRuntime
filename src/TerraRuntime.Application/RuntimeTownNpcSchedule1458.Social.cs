using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    internal readonly record struct SocialPeerPlan(NpcSnapshot Expected, NpcStateUpdate Update, bool Force);
    private readonly Dictionary<byte, NpcHandle> socialForcedUpdates = [];

    private static NpcStateUpdate ToUpdate(in NpcSnapshot source) => new(source.Type, source.NetId,
        source.PositionX, source.PositionY, source.VelocityX, source.VelocityY, source.Target,
        source.Ai, source.Simulation);

    private void TryPlanSocialMaintenance(in NpcSnapshot source, out NpcStateUpdate update, out bool force)
    {
        NpcAiState ai = source.Ai with { Ai1 = source.Ai.Ai1 - 1f };
        NpcAiState local = source.Simulation.LocalAi;
        force = ai.Ai1 <= 0f;
        if (force)
        {
            ai = ai with { Ai0 = 0f, Ai1 = 60 + random.Next(60), Ai2 = 0f };
            local = local with { Ai3 = 30 + random.Next(60) };
        }
        update = ToUpdate(in source) with { VelocityX = source.VelocityX * .8f,
            Ai = ai, Simulation = source.Simulation with { LocalAi = local, DirectionY = -1 } };
    }
}
