using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryStageCaptainGhost(in NpcSnapshot parent, out NpcSnapshot? child)
    {
        child = null;
        if (Capacity != 200 || parent.TypeIdentity != VanillaNpcIds.PirateCaptain ||
            parent.Simulation.Life != 0 || parent.Simulation.CanBeReplacedByOtherNpcs ||
            CapturedDeathSpawnContext is not { IsValid: true } context ||
            context.Difficulty is not (1f or 2f or 3f or 4f) || mutationSerialExhausted ||
            !float.IsFinite(parent.PositionX) || !float.IsFinite(parent.PositionY) ||
            parent.PositionX < int.MinValue || parent.PositionX >= int.MaxValue ||
            parent.PositionY < int.MinValue || parent.PositionY >= int.MaxValue)
            return false;
        // Match the existing allocation owner's free-first/replacement-fallback order.
        // Ignore generation only while identifying the source-selected slot: a saturated
        // chosen slot is refused rather than resampled; later slots do not invalidate it.
        int selected = -1;
        int replacement = -1;
        for (int slot = 1; slot < Capacity; slot++)
        {
            ref readonly SlotState candidate = ref _slots[slot];
            if (!candidate.Active && candidate.SpawnProtection == 0)
            {
                selected = slot;
                break;
            }
            if (replacement < 0 && candidate.Update.Simulation.CanBeReplacedByOtherNpcs)
                replacement = slot;
        }
        if (selected < 0) selected = replacement;
        if (selected >= 0 && _slots[selected].Generation == ulong.MaxValue) return false;
        bool noAvailableSlot = selected < 0;
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.PirateGhost, (int)parent.PositionX,
            (int)parent.PositionY, 0f, 0f, parent.Target) { StartSlot = 1 };
        // NewNPC's Good-world offer precedes even an exhausted allocation. This detached
        // producer accepts that no-birth result; it never retries or changes the selected identity.
        if (!TrySpawnIntent(in intent, out var born)) return noAvailableSlot;
        ref SlotState state = ref _slots[born.Handle.Slot];
        state.Update = state.Update with { Simulation = state.Update.Simulation with
        {
            TimeLeft = checked(VanillaNpcDefinitionCatalog.NewNpcTimeLeft * 20),
            MoneyValue = 0f, ExtraMoneyValue = 0, Midas = false
        } };
        child = Capture(born.Handle.Slot, state);
        return true;
    }

    // Export the one source-selected physical birth from the existing isolated death table.
    // Allocation/protection/Good-world offers have already run there; adoption never allocates again.
    internal bool TryPrepareCaptainDeathBirth(in NpcSnapshot parent, RuntimeNpcStore preview,
        ulong expectedSerial, out DeathBirth? preparation)
    {
        preparation = null;
        if ((parent.TypeIdentity != VanillaNpcIds.PirateCaptain && parent.TypeIdentity != VanillaNpcIds.PirateGhost) || parent.Simulation.Life != 0 ||
            mutationSerialExhausted || mutationSerial != expectedSerial ||
            preview.Capacity != Capacity || preview.CapturedDeathSpawnContext is not { IsValid: true } context ||
            !TryGet(parent.Handle, out var live) || live.Revision != parent.Revision)
            return false;
        if (!preview.TryGet(parent.Handle, out var retainedParent) ||
            retainedParent.Revision.Value != parent.Revision.Value + 1 ||
            retainedParent with { Revision = parent.Revision } != parent)
            return false;

        int childSlot = -1;
        for (int slot = 0; slot < Capacity; slot++)
        {
            if (slot == parent.Handle.Slot) continue;
            if (SameDeathSlot(_slots[slot], preview._slots[slot])) continue;
            if (childSlot >= 0 || !preview._slots[slot].Active ||
                preview._slots[slot].Update.Type != VanillaNpcIds.PirateGhost.Value)
                return false;
            childSlot = slot;
        }
        // HP, an optional child, and terminal removal each advance this existing physical census.
        // Reserve the entire callback-free tail before accepting HP, not after its first write.
        if (parent.TypeIdentity == VanillaNpcIds.PirateGhost && childSlot >= 0) return false;
        ulong requiredWrites = childSlot < 0 ? 2UL : 3UL;
        if (expectedSerial > ulong.MaxValue - requiredWrites) return false;
        preparation = new(this, live, expectedSerial, context, childSlot,
            preview);
        return true;
    }

    private static bool SameDeathSlot(in SlotState left, in SlotState right) =>
        left.Active == right.Active && left.BirthPending == right.BirthPending &&
        left.SpawnProtection == right.SpawnProtection && left.Generation == right.Generation &&
        left.Revision == right.Revision && left.Update == right.Update;

    internal sealed class DeathBirth
    {
        private readonly RuntimeNpcStore owner;
        private readonly NpcSnapshot parent;
        private readonly ulong beforeSerial;
        private readonly VanillaNpcSpawnContext context;
        private readonly int slot;
        private readonly SlotState child;
        private bool adopted;
        private bool published;

        internal NpcSnapshot? Child => slot < 0 ? null : Capture((byte)slot, child);

        internal DeathBirth(RuntimeNpcStore owner, NpcSnapshot parent, ulong beforeSerial,
            VanillaNpcSpawnContext context, int slot, RuntimeNpcStore preview)
        {
            this.owner = owner;
            this.parent = parent;
            this.beforeSerial = beforeSerial;
            this.context = context;
            this.slot = slot;
            this.child = slot < 0 ? default : preview._slots[slot];
        }

        // Providers are sampled by the containing death plan before its final pure guards.
        internal bool ContextIsCurrent => owner.TryCaptureDeathSpawnContext(out var current) && current == context;

        internal bool CanAdopt(in NpcSnapshot acceptedParent) => !adopted &&
            !owner.mutationSerialExhausted && beforeSerial < ulong.MaxValue - 1 &&
            owner.mutationSerial == beforeSerial + 1 && acceptedParent.Handle == parent.Handle &&
            acceptedParent.Revision.Value == parent.Revision.Value + 1 &&
            acceptedParent.TypeIdentity == parent.TypeIdentity && acceptedParent.Simulation.Life == 0 &&
            owner.MatchesSource(in acceptedParent);

        internal bool TryAdoptUnpublished(in NpcSnapshot acceptedParent)
        {
            if (!CanAdopt(in acceptedParent)) return false;
            if (slot >= 0)
            {
                if (!owner._slots[slot].Active) owner._activeCount++;
                owner._slots[slot] = child;
                owner.MarkSlotMutation();
            }
            adopted = true;
            return true;
        }

        internal bool TryPublish()
        {
            if (!adopted || published) return false;
            published = true;
            if (slot < 0 || !SameDeathSlot(owner._slots[slot], child)) return true;
            var born = Capture((byte)slot, child);
            if (owner._commitSink is INpcBirthRetentionSink retention)
                retention.NpcBirthRetained(in born);
            if (SameDeathSlot(owner._slots[slot], child))
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in born);
            return true;
        }
    }
}
