using System.Buffers;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal const int MaximumSpawnBatchSize = 8;

    internal readonly record struct SpawnRequest(ProjectileStateUpdate State, int? TimeLeftOverride = null);

    /// <summary>Plans bounded, ordered NewProjectile allocations without exposing an intermediate generation.</summary>
    internal bool TryPrepareVanillaSpawnBatch(ReadOnlySpan<SpawnRequest> requests, out SpawnBatchPreparation? preparation)
    {
        preparation = null;
        if (requests.IsEmpty || requests.Length > MaximumSpawnBatchSize)
            return false;

        var candidate = new SpawnBatchPreparation(this, requests.Length);
        if (!candidate.TryPrepare(requests))
        {
            candidate.Dispose();
            return false;
        }

        preparation = candidate;
        return true;
    }

    internal sealed class SpawnBatchPreparation : IDisposable
    {
        private readonly RuntimeProjectileStore owner;
        private readonly SlotState[] baseline;
        private readonly SlotState[] shadow;
        private readonly ProjectileSnapshot[] births;
        private readonly ProjectileSnapshot[] finalBirths;
        private readonly ushort[] changedSlots;
        private readonly int baselineActiveCount;
        private int finalActiveCount;
        private int changedCount;
        private bool adopted;
        private bool published;
        private bool disposed;

        internal SpawnBatchPreparation(RuntimeProjectileStore owner, int count)
        {
            this.owner = owner;
            // The measured eight-child preparation otherwise allocates two complete 1,001-slot arrays
            // (305,672 bytes per preparation). Reuse value-only scratch; the immutable journal is never pooled.
            baseline = ArrayPool<SlotState>.Shared.Rent(owner.Capacity);
            shadow = ArrayPool<SlotState>.Shared.Rent(owner.Capacity);
            owner._slots.CopyTo(baseline, 0);
            owner._slots.CopyTo(shadow, 0);
            births = new ProjectileSnapshot[count];
            finalBirths = new ProjectileSnapshot[count];
            changedSlots = new ushort[count];
            baselineActiveCount = finalActiveCount = owner._activeCount;
        }

        internal ReadOnlySpan<ProjectileSnapshot> Births => disposed ? [] : births;
        internal ReadOnlySpan<ProjectileSnapshot> FinalBirths => disposed ? [] : finalBirths.AsSpan(0, changedCount);

        internal bool TryPrepare(ReadOnlySpan<SpawnRequest> requests)
        {
            for (int index = 0; index < requests.Length; index++)
            {
                SpawnRequest request = requests[index];
                ProjectileStateUpdate state = request.State;
                if (request.TimeLeftOverride is <= 0 || !IsValidState(in state) ||
                    !TryCreateLifecycle(state.Type, out ProjectileLifecycleState lifecycle) ||
                    !TrySelectVanillaAllocationSlot(shadow.AsSpan(0, owner.Capacity), out ushort slot) || shadow[slot].Generation == ulong.MaxValue)
                    return false;

                if (request.TimeLeftOverride is int timeLeft)
                    lifecycle = lifecycle with { TimeLeft = timeLeft };

                ref SlotState next = ref shadow[slot];
                if (!next.Active) finalActiveCount++;
                next.Generation++;
                InitializeSlot(ref next, in state, in lifecycle);
                births[index] = Capture(slot, in next);
                if (Array.IndexOf(changedSlots, slot, 0, changedCount) < 0)
                    changedSlots[changedCount++] = slot;
            }

            for (int index = 0; index < changedCount; index++)
                finalBirths[index] = Capture(changedSlots[index], in shadow[changedSlots[index]]);
            return true;
        }

        internal bool IsCurrentOwned
        {
            get
            {
                if (disposed || adopted || owner._activeCount != baselineActiveCount)
                    return false;
                // Every physical slot can affect a later child's allocation, including inactive generations
                // and the overflow slot. Comparing only winners would accept a stale replacement sequence.
                for (int index = 0; index < owner.Capacity; index++)
                    if (!SameSlot(in baseline[index], in owner._slots[index])) return false;
                return true;
            }
        }

        internal bool TryCommitUnpublished()
        {
            if (!IsCurrentOwned) return false;
            // This adoption tail is callback-free. All potentially failing increments were checked on shadow.
            for (int index = 0; index < changedCount; index++)
                owner._slots[changedSlots[index]] = shadow[changedSlots[index]];
            owner._activeCount = finalActiveCount;
            adopted = true;
            return true;
        }

        internal bool TryPublishBirthJournal()
        {
            if (disposed || !adopted || published) return false;
            for (int index = 0; index < changedCount; index++)
            {
                ProjectileSnapshot final = finalBirths[index];
                if (!owner.TryGet(final.Handle, out ProjectileSnapshot current) || current != final ||
                    !owner.TryGetLifecycle(final.Handle, out ProjectileLifecycleState lifecycle) ||
                    lifecycle != shadow[final.Handle.Slot].Lifecycle)
                    return false;
            }

            published = true;
            // Source sends each NewProjectile birth, even generations overwritten by a later child.
            // Reentrant sinks may replace live actors; the accepted immutable journal never writes them back.
            foreach (ProjectileSnapshot birth in births)
                owner._commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in birth);
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ArrayPool<SlotState>.Shared.Return(baseline);
            ArrayPool<SlotState>.Shared.Return(shadow);
        }

        private static bool SameSlot(in SlotState before, in SlotState current) =>
            before.Active == current.Active && before.Generation == current.Generation &&
            before.Revision == current.Revision && before.Update == current.Update &&
            before.Lifecycle == current.Lifecycle && before.CombatTrusted == current.CombatTrusted &&
            before.CombatTrustedOwner == current.CombatTrustedOwner && before.SourceNpc == current.SourceNpc;
    }
}
