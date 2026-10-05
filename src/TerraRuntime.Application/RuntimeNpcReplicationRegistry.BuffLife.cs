using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    internal void RetainAcceptedBuffLife(in NpcSnapshot before, int dotLife, in NpcSnapshot accepted)
    {
        // GetHurtByDebuff changes life without source netUpdate. Other healing/contact/vital changes
        // keep the normal immediate-life publication path; only this accepted DoT delta is retained.
        if (before.Handle != accepted.Handle || dotLife <= 0 || dotLife >= before.Simulation.Life ||
            accepted.Simulation.Life != dotLife || before.Simulation.LifeMax != accepted.Simulation.LifeMax) return;
        lock (liveFrameGate)
        {
            int slot = accepted.Handle.Slot;
            if (liveFrameOwners[slot] != before.Handle ||
                liveSnapshots[slot].Simulation.Life != before.Simulation.Life ||
                liveSnapshots[slot].Simulation.LifeMax != before.Simulation.LifeMax) return;
            liveSnapshots[slot] = liveSnapshots[slot] with {
                Simulation = liveSnapshots[slot].Simulation with { Life = dotLife } };
        }
        // Joining peers receive the current complete state, independently of motion cadence.
        RefreshBaselineFrames(in accepted);
    }
}
