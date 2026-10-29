using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    // These fields belong to the existing replication owner. They are advanced only by the
    // store's explicit accepted vanilla AI publication, never by combat/manual state commits.
    private readonly UndeadNetworkState1458[] undeadNetwork = new UndeadNetworkState1458[MaxNpcSlots];

    internal int CaptureNpcPhaseSpam(NpcHandle owner) =>
        undeadNetwork[owner.Slot].Owner == owner ? undeadNetwork[owner.Slot].Spam : 0;

    internal bool IsNpcPhaseSpamCurrent(NpcHandle owner, int expected) =>
        (!undeadNetwork[owner.Slot].Owner.IsAssigned || undeadNetwork[owner.Slot].Owner == owner ||
         undeadNetwork[owner.Slot].Owner.Generation.Value < owner.Generation.Value) &&
        CaptureNpcPhaseSpam(owner) == expected;

    internal bool TryAdoptNpcPhaseSpamAdjustment(in NpcSnapshot accepted, int expected, int adjustment)
    {
        if (adjustment is not (0 or -30) || !IsNpcPhaseSpamCurrent(accepted.Handle, expected))
            return false;
        if (adjustment == 0) return true;
        if (expected < 0) return false;
        ref var state = ref undeadNetwork[accepted.Handle.Slot];
        if (state.Owner != accepted.Handle) state = new() { Owner = accepted.Handle };
        // NetUpdateIgnoreSpamLimit makes this debit before UpdateNetworkCode;
        // the following accepted phase still owns its usual send/decrement.
        state.Spam += adjustment;
        return true;
    }

    internal bool TryAdoptNpcPhaseAiRetirement(in NpcSnapshot accepted, int expected)
    {
        if (accepted.Simulation.Life != 0 || !IsNpcPhaseSpamCurrent(accepted.Handle, expected) ||
            !IsUndeadNetworkPhaseCurrent(in accepted) || !UsesUndeadNetworkPhase1458(in accepted)) return false;
        ref var state = ref undeadNetwork[accepted.Handle.Slot];
        if (state.Owner != accepted.Handle) state = new() { Owner = accepted.Handle };
        // NPC.UpdateNetworkCode resets an already inactive actor's spam before
        // its mandatory offer. Reuse the same accepted phase and pending-request owner;
        // its one inactive packet is published later by the terminal-removal owner.
        state.Spam = 0;
        return TrySelectUndeadNetworkPhase(NpcStateCommitKind.ForcedUpdate, in accepted, out bool send) && send;
    }

    internal void ObserveMaterializedTransform(in NpcSnapshot accepted) => ClearSourceNetworkRequests(in accepted);

    private void ClearSourceNetworkRequests(in NpcSnapshot accepted)
    {
        ref var state = ref undeadNetwork[accepted.Handle.Slot];
        if (state.Owner != accepted.Handle) return;
        // NPC.SetDefaults (1.4.5.8) clears both pending requests, while netSpam
        // survives a same-generation Transform. This is not UpdateNetworkCode:
        // neither its spam decrement nor its phase revision advances here.
        state.PendingSpamCooldown = false;
        state.PendingFullSpamCooldown = false;
    }

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
                // The same SendData23 flag4 clears both pending requests and all stream state.
                // Ordinary active non-netAlways actors retain a pending request across sends.
                if (SourcePacket23ResetsAll(in snapshot, kind == NpcStateCommitKind.Spawn))
                    ClearSourceNetworkRequests(in snapshot);
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
         VanillaNpcPhaseAccepted1458.SupportsIdentity(snapshot.TypeIdentity, snapshot.NetIdentity));

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
