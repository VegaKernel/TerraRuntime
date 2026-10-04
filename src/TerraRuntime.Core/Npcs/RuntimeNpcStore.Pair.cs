using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    // Town conversations change both actors in one source AI/frame phase. Validate both revisions
    // before either write; the caller adopts its RNG plan before publishing the resulting actors.
    internal bool TryUpdatePairUnpublished(
        in NpcSnapshot expectedA, in NpcStateUpdate updateA,
        in NpcSnapshot expectedB, in NpcStateUpdate updateB,
        out NpcSnapshot committedA, out NpcSnapshot committedB)
    {
        committedA = committedB = default;
        if (expectedA.Handle.Slot == expectedB.Handle.Slot ||
            !IsCurrentHandleCandidate(expectedA.Handle) || !IsCurrentHandleCandidate(expectedB.Handle) ||
            !IsValid(in updateA) || !IsValid(in updateB)) return false;

        ref SlotState a = ref _slots[expectedA.Handle.Slot];
        ref SlotState b = ref _slots[expectedB.Handle.Slot];
        if (!a.Active || !b.Active || a.Generation != expectedA.Handle.Generation.Value ||
            b.Generation != expectedB.Handle.Generation.Value || a.Revision != expectedA.Revision.Value ||
            b.Revision != expectedB.Revision.Value) return false;

        ulong nextA = a.Revision, nextB = b.Revision;
        if (!TryAdvance(ref nextA) || !TryAdvance(ref nextB)) return false;
        NpcStateUpdate normalizedA = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(in updateA, in a.Update);
        NpcStateUpdate normalizedB = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(in updateB, in b.Update);
        a.Update = normalizedA;
        b.Update = normalizedB;
        a.Revision = nextA;
        b.Revision = nextB;
        MarkSlotMutation();
        committedA = Capture(expectedA.Handle.Slot, in a);
        committedB = Capture(expectedB.Handle.Slot, in b);
        return true;
    }
}
