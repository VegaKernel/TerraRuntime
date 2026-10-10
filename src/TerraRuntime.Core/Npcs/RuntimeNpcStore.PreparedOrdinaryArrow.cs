using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal OrdinaryArrowNpcWorkspace CreateOrdinaryArrowNpcWorkspace() => new(this);

    internal sealed class OrdinaryArrowNpcWorkspace
    {
        private readonly RuntimeNpcStore owner;
        private readonly RuntimeNpcStore projected;
        private readonly NpcSnapshot[] targets;
        private readonly SlotState[] before;
        private readonly SlotState[] accepted;
        private readonly bool[] selected;
        private int count;
        private ulong epoch;
        private ulong serial;
        private bool busy;
        private bool adopted;

        internal OrdinaryArrowNpcWorkspace(RuntimeNpcStore owner)
        {
            this.owner = owner;
            projected = new RuntimeNpcStore(owner.Capacity);
            targets = new NpcSnapshot[owner.Capacity];
            before = new SlotState[owner.Capacity];
            accepted = new SlotState[owner.Capacity];
            selected = new bool[owner.Capacity];
        }

        internal bool TryPrepare(ReadOnlySpan<NpcSnapshot> source, out OrdinaryArrowNpcPreview? preparation)
        {
            preparation = null;
            if (busy || epoch == ulong.MaxValue || source.Length > owner.Capacity ||
                owner.mutationSerialExhausted || owner.mutationSerial == ulong.MaxValue) return false;
            Array.Clear(selected);
            foreach (ref readonly var target in source)
            {
                if (!owner.MatchesSource(in target) || selected[target.Handle.Slot] ||
                    target.TypeIdentity.Value != 3 || target.NetIdentity.Value != 3 ||
                    target.Simulation.Life <= 0 || target.Simulation.Immortal != false ||
                    target.Simulation.LifeRegenCounter is null || target.Simulation.ShimmerTransparency != 0f ||
                    owner._slots[target.Handle.Slot].BirthPending) return false;
                selected[target.Handle.Slot] = true;
            }
            // Only previous touched slots are cleared; no complete-world copy or per-step allocation.
            for (int i = 0; i < count; i++) projected._slots[targets[i].Handle.Slot] = default;
            count = source.Length;
            source.CopyTo(targets);
            for (int i = 0; i < count; i++)
            {
                int slot = targets[i].Handle.Slot;
                before[i] = owner._slots[slot];
                accepted[i] = default;
                projected._slots[slot] = before[i];
            }
            projected._activeCount = count;
            projected.mutationSerial = 0;
            projected.mutationSerialExhausted = false;
            serial = owner.mutationSerial;
            adopted = false;
            busy = true;
            epoch++;
            preparation = new OrdinaryArrowNpcPreview(this, epoch);
            return true;
        }

        private static bool Same(in SlotState left, in SlotState right) =>
            left.Active == right.Active && left.BirthPending == right.BirthPending &&
            left.SpawnProtection == right.SpawnProtection && left.Generation == right.Generation &&
            left.Revision == right.Revision && left.Update == right.Update;

        internal bool Owns(ulong expectedEpoch) => busy && expectedEpoch == epoch;
        internal RuntimeNpcStore GetProjected(ulong expectedEpoch) => Owns(expectedEpoch)
            ? projected : throw new InvalidOperationException("The arrow NPC preparation lease is no longer owned.");

        internal bool IsCurrent(ulong expectedEpoch)
        {
            if (!Owns(expectedEpoch) || adopted || owner.mutationSerialExhausted || owner.mutationSerial != serial)
                return false;
            for (int i = 0; i < count; i++)
                if (!Same(owner._slots[targets[i].Handle.Slot], before[i])) return false;
            return true;
        }

        internal bool CanAdopt(ulong expectedEpoch)
        {
            if (!IsCurrent(expectedEpoch) || projected.mutationSerialExhausted ||
                projected.mutationSerial > (ulong)(2 * owner.Capacity) ||
                projected.mutationSerial >= ulong.MaxValue - serial || projected._activeCount != count) return false;
            for (int i = 0; i < count; i++)
            {
                ref readonly var next = ref projected._slots[targets[i].Handle.Slot];
                if (!next.Active || next.Generation != before[i].Generation ||
                    next.BirthPending != before[i].BirthPending || next.SpawnProtection != before[i].SpawnProtection ||
                    next.Revision < before[i].Revision || next.Update.Type != before[i].Update.Type ||
                    next.Update.NetId != before[i].Update.NetId || next.Update.Simulation.Life <= 0 ||
                    !IsValid(in next.Update)) return false;
            }
            return true;
        }

        internal bool TryAdoptUnpublished(ulong expectedEpoch)
        {
            if (!CanAdopt(expectedEpoch)) return false;
            for (int i = 0; i < count; i++)
            {
                int slot = targets[i].Handle.Slot;
                accepted[i] = projected._slots[slot];
                owner._slots[slot] = accepted[i];
            }
            for (ulong i = 0; i < projected.mutationSerial; i++) owner.MarkSlotMutation();
            adopted = true;
            return true;
        }

        internal bool IsAcceptedTargetCurrent(ulong expectedEpoch, NpcHandle handle)
        {
            if (!Owns(expectedEpoch) || !adopted) return false;
            for (int i = 0; i < count; i++)
                if (targets[i].Handle == handle) return Same(owner._slots[handle.Slot], accepted[i]);
            return false;
        }

        internal void Release(ulong expectedEpoch)
        {
            if (Owns(expectedEpoch)) busy = false;
        }
    }

    internal sealed class OrdinaryArrowNpcPreview : IDisposable
    {
        private readonly OrdinaryArrowNpcWorkspace workspace;
        private readonly ulong epoch;
        private bool disposed;
        internal OrdinaryArrowNpcPreview(OrdinaryArrowNpcWorkspace workspace, ulong epoch)
        { this.workspace = workspace; this.epoch = epoch; }
        internal RuntimeNpcStore Projected => !disposed ? workspace.GetProjected(epoch)
            : throw new ObjectDisposedException(nameof(OrdinaryArrowNpcPreview));
        internal bool IsCurrent => !disposed && workspace.IsCurrent(epoch);
        internal bool CanAdopt => !disposed && workspace.CanAdopt(epoch);
        internal bool TryAdoptUnpublished() => !disposed && workspace.TryAdoptUnpublished(epoch);
        internal bool IsAcceptedTargetCurrent(NpcHandle handle) =>
            !disposed && workspace.IsAcceptedTargetCurrent(epoch, handle);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            workspace.Release(epoch);
        }
    }
}
