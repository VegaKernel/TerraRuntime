using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaSlimeStatusOwner1458
{
    bool TryCaptureRevision(in NpcSnapshot before, out ulong revision);
    bool TryPrepare(in NpcSnapshot before, IVanillaNpcRandom random, bool goodWorld,
        out IVanillaSlimeStatusPlan1458 plan);
}

internal interface IVanillaSlimeStatusPlan1458
{
    ulong Revision { get; }
    NpcSimulationState Simulation { get; }
    bool IsCurrent { get; }
    void Commit(in NpcSnapshot completed, bool torch);
}
