using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcBuffStatus1458
{
    // The store preview validates the unchanged placeholder revision and final NPC adoption.
    // Status currentness remains generation- and status-revision-specific across that placeholder.
    internal bool IsCurrentOrdinaryMeleePhase1458(in RuntimeNpcBuffPlan1458 plan) =>
        VanillaGroundFighterNpcCatalog.IsOrdinaryMeleeCohort1458(plan.Expected.TypeIdentity) &&
        npcs.TryGet(plan.Expected.Handle, out var current) && current.Type == plan.Expected.Type &&
        current.NetId == plan.Expected.NetId && entries[current.Handle.Slot].Handle == current.Handle &&
        entries[current.Handle.Slot].Revision == plan.StatusRevision;
}
