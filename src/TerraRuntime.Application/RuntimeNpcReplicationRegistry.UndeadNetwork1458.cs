using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    // These fields belong to the existing replication owner. They are advanced only by the
    // store's explicit accepted vanilla AI publication, never by combat/manual state commits.
    private readonly UndeadNetworkState1458[] undeadNetwork = new UndeadNetworkState1458[MaxNpcSlots];

    private bool IsUndeadNetworkPhaseCurrent(in NpcSnapshot snapshot)
    {
        if (!UsesUndeadNetworkPhase1458(in snapshot))
            return true;
        ref readonly var state = ref undeadNetwork[snapshot.Handle.Slot];
        return !state.Owner.IsAssigned ||
            state.Owner.Generation.Value < snapshot.Handle.Generation.Value ||
            state.Owner == snapshot.Handle && snapshot.Revision.Value > state.LastPhaseRevision.Value &&
            snapshot.Revision.Value >= state.LowPriorityRevision.Value;
    }

    private bool TrySelectUndeadNetworkPhase(NpcStateCommitKind kind, in NpcSnapshot snapshot, out bool send)
    {
        send = false;
        if (!UsesUndeadNetworkPhase1458(in snapshot) ||
            kind is not (NpcStateCommitKind.Spawn or NpcStateCommitKind.Update or NpcStateCommitKind.ForcedUpdate))
            return false;

        ref var state = ref undeadNetwork[snapshot.Handle.Slot];
        if (state.Owner.IsAssigned &&
            (state.Owner.Generation.Value > snapshot.Handle.Generation.Value ||
             state.Owner == snapshot.Handle &&
             (snapshot.Revision.Value <= state.LastPhaseRevision.Value ||
              snapshot.Revision.Value < state.LowPriorityRevision.Value)))
            return true;
        if (state.Owner != snapshot.Handle)
            state = new() { Owner = snapshot.Handle };
        state.LastPhaseRevision = snapshot.Revision;

        // TerrariaServer 1.4.5.8 NPC.UpdateNetworkCode. Nonboss NPCs spend thirty spam
        // ticks per send and admit the offer at <=90 before the per-update decrement.
        bool requested = kind is NpcStateCommitKind.Spawn or NpcStateCommitKind.ForcedUpdate;
        if (requested || state.PendingSpamCooldown || state.PendingFullSpamCooldown && state.Spam == 0)
        {
            if (state.Spam <= 90)
            {
                state.Spam += 30;
                send = true;
                // NetMessage.SendData(23) clears pending requests for spawnNeedsSyncing,
                // not for an ordinary active NPC packet. A pending request is sticky.
                if (kind == NpcStateCommitKind.Spawn || snapshot.Simulation.Life <= 0)
                {
                    state.PendingSpamCooldown = false;
                    state.PendingFullSpamCooldown = false;
                }
            }
            else
            {
                state.PendingSpamCooldown = true;
            }
        }
        if (state.Spam > 0)
            state.Spam--;
        return true;
    }

    /// <summary>
    /// Retains the source StrikeNPC_Inner/NetUpdateLowPriority request without advancing
    /// UpdateNetworkCode or replacing the existing immediate combat publication.
    /// </summary>
    internal bool MarkUndeadLowPriorityNetworkUpdate(in NpcSnapshot snapshot)
    {
        if (!UsesUndeadNetworkPhase1458(in snapshot))
            return false;
        ref var state = ref undeadNetwork[snapshot.Handle.Slot];
        if (state.Owner.IsAssigned && state.Owner.Generation.Value > snapshot.Handle.Generation.Value)
            return false;
        if (state.Owner != snapshot.Handle)
            state = new() { Owner = snapshot.Handle };
        if (snapshot.Revision.Value <= state.LastPhaseRevision.Value ||
            snapshot.Revision.Value <= state.LowPriorityRevision.Value)
            return false;
        state.PendingFullSpamCooldown = true;
        state.LowPriorityRevision = snapshot.Revision;
        return true;
    }

    private static bool UsesUndeadNetworkPhase1458(in NpcSnapshot snapshot) =>
        snapshot.IsActive &&
        (TerraRuntime.Gameplay.Npcs.VanillaNpcGroundRoster1458.IsUndeadRuntimeIdentity(snapshot.TypeIdentity, snapshot.NetIdentity) ||
         VanillaNonresidentGroundCritterCatalog1458.IsSupported(snapshot.Type) && snapshot.Type == snapshot.NetId ||
         VanillaGroundFighterNpcCatalog.IsOrdinaryMeleeCohort1458(snapshot.TypeIdentity) && snapshot.Type == snapshot.NetId);

    private struct UndeadNetworkState1458
    {
        internal NpcHandle Owner;
        internal NpcRevision LastPhaseRevision;
        internal NpcRevision LowPriorityRevision;
        internal int Spam;
        internal bool PendingSpamCooldown;
        internal bool PendingFullSpamCooldown;
    }
}
