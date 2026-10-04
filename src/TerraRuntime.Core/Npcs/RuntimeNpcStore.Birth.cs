using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>Observes an accepted birth for joining-player baselines without broadcasting it yet.</summary>
internal interface INpcBirthRetentionSink
{
    void NpcBirthRetained(in NpcSnapshot snapshot);
}

public sealed partial class RuntimeNpcStore
{
    internal bool HasPendingBirth(NpcHandle handle) =>
        TryGet(handle, out _) && _slots[handle.Slot].BirthPending;

    // AI001 NewNPC sets spawnNeedsSyncing without immediately sending packet 23. The first
    // outer update, slot replacement or StrikeNPC publication must announce that generation.
    internal bool TryRetainPendingBirth(in NpcSnapshot expected)
    {
        if (!MatchesSource(in expected))
            return false;

        if (!_slots[expected.Handle.Slot].BirthPending)
        {
            _slots[expected.Handle.Slot].BirthPending = true;
            MarkSlotMutation();
        }
        if (_commitSink is INpcBirthRetentionSink retained)
            retained.NpcBirthRetained(in expected);
        return true;
    }

    internal bool TryPublishPendingBirth(NpcHandle handle)
    {
        if (!TryGet(handle, out var current))
            return false;

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.BirthPending)
            return true;

        // Clear before invoking the observer: reentrant observers cannot announce it twice.
        state.BirthPending = false;
        MarkSlotMutation();
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in current);
        return true;
    }

    internal bool TryPublishPendingBirthBeforeStrike(in NpcSnapshot before, in NpcSnapshot committed)
    {
        if (before.Handle != committed.Handle || committed.Revision.Value != before.Revision.Value + 1 ||
            !MatchesSource(in committed))
            return false;

        ref SlotState state = ref _slots[committed.Handle.Slot];
        if (!state.BirthPending)
            return true;

        // Nonclient StrikeNPC broadcasts 28 before StrikeNPC_Inner. Admission commits first in
        // this runtime, but its pending 23 must still expose the exact retained pre-strike state.
        state.BirthPending = false;
        MarkSlotMutation();
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in before);
        return true;
    }

    internal void PublishPendingBirths()
    {
        // NPC.Spawner.SyncNewlySpawnedNPCs walks physical slots in ascending order.
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (state.Active && state.BirthPending)
                TryPublishPendingBirth(new((byte)slot, new(state.Generation)));
        }
    }

    private void PublishCurrentUpdate(ref SlotState state, in NpcSnapshot snapshot, bool forceSync)
    {
        NpcStateCommitKind kind = state.BirthPending
            ? NpcStateCommitKind.Spawn
            : forceSync ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update;
        if (state.BirthPending)
        {
            state.BirthPending = false;
            MarkSlotMutation();
        }
        _commitSink?.NpcStateCommitted(kind, in snapshot);
    }
}
