using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcBuffStatus1458
{
    internal static bool SlotsMatch(in RuntimeNpcBuffSlots1458 first, in RuntimeNpcBuffSlots1458 second)
    {
        for (int index = 0; index < Capacity; index++) if (first[index] != second[index]) return false;
        return true;
    }

    internal void PublishSourceWetRemoval(in NpcSnapshot accepted)
    {
        if (npcs.TryGet(accepted.Handle, out var current) && current == accepted)
            buffListChanged?.Invoke(accepted.Handle);
    }

    internal bool IsCurrentCommittedPhase(in RuntimeNpcBuffPlan1458 plan, in NpcSnapshot accepted) =>
        npcs.TryGet(accepted.Handle, out var current) && current == accepted &&
        entries[accepted.Handle.Slot].Handle == accepted.Handle &&
        entries[accepted.Handle.Slot].Revision == plan.StatusRevision + 1;

    // The store preview validates the unchanged placeholder revision and final NPC adoption.
    // Status currentness remains generation- and status-revision-specific across that placeholder.
    internal bool IsCurrentNpcPhase1458(in RuntimeNpcBuffPlan1458 plan) =>
        VanillaNpcPhaseAccepted1458.SupportsIdentity(plan.Expected.TypeIdentity, plan.Expected.NetIdentity) &&
        npcs.TryGet(plan.Expected.Handle, out var current) &&
        current.Type == (plan.TransformedType?.Value ?? plan.Expected.Type) &&
        current.NetId == (plan.TransformedType?.Value ?? plan.Expected.NetId) &&
        entries[current.Handle.Slot].Handle == current.Handle &&
        entries[current.Handle.Slot].Revision == plan.StatusRevision;
}
