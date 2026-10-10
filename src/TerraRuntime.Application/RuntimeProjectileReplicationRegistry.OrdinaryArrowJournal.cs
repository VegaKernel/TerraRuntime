using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileReplicationRegistry
{
    private OrdinaryArrowBaselinePreparation? ordinaryArrowBaseline;

    internal bool TryPrepareOrdinaryArrowBaseline(RuntimeProjectileStore store,
        in ProjectileSnapshot before, in ProjectileSnapshot final, ProjectileSnapshot[] historical,
        out OrdinaryArrowBaselinePreparation? preparation)
    {
        preparation = null;
        if (ordinaryArrowBaseline is not null || before.Type.Value is not (4 or 5) ||
            before.Handle != final.Handle || before.Type != final.Type || before.Spawner != final.Spawner ||
            !store.TryGet(before.Handle, out var retained) || retained != before ||
            !identities.TryGetWireKey(before.Handle, out var key) ||
            !identities.TryCaptureBindingFacts(in key, before.Handle.Slot, out var binding) ||
            !RuntimeProjectilePacketProjection.TryCreateUpdate(in final, in key, out var update) ||
            !TerrariaProjectileEncoder.TryEncodeUpdate(in update, out byte[] frame)) return false;
        foreach (var snapshot in historical)
            if (snapshot.Handle != final.Handle || snapshot.Type != final.Type || snapshot.Spawner != final.Spawner)
                return false;
        preparation = new(this, store, before, final, historical, key, binding,
            Volatile.Read(ref baselineFrames[before.Handle.Slot]), frame);
        return true;
    }

    private bool IsRetainedOrdinaryArrowJournalSnapshot(in ProjectileSnapshot snapshot) =>
        ordinaryArrowBaseline?.OwnsHistorical(in snapshot) == true;

    private bool IsStaleOrdinaryArrowJournalSnapshot(in ProjectileSnapshot snapshot) =>
        ordinaryArrowBaseline is { } journal && journal.OwnsHistorical(in snapshot) && !journal.IsPublicationCurrent;

    internal sealed class OrdinaryArrowBaselinePreparation(
        RuntimeProjectileReplicationRegistry owner, RuntimeProjectileStore store,
        ProjectileSnapshot before, ProjectileSnapshot final, ProjectileSnapshot[] historical,
        TerrariaProjectileKeyState key, RuntimeProjectileWireIdentityRegistry.BindingFacts binding,
        byte[]? beforeFrame, byte[] finalFrame) : IDisposable
    {
        private bool adopted, disposed;

        internal bool IsCurrent => !adopted && !disposed && owner.ordinaryArrowBaseline is null &&
            store.TryGet(before.Handle, out var current) && current == before && BindingMatches() &&
            ReferenceEquals(Volatile.Read(ref owner.baselineFrames[before.Handle.Slot]), beforeFrame);

        internal bool TryAdoptUnpublished()
        {
            if (adopted || disposed || owner.ordinaryArrowBaseline is not null ||
                !store.TryGet(final.Handle, out var current) || current != final || !BindingMatches() ||
                !ReferenceEquals(Volatile.Read(ref owner.baselineFrames[before.Handle.Slot]), beforeFrame)) return false;
            // A joining observer sees final retained motion; historical packet27 must not rewind this baseline.
            Volatile.Write(ref owner.baselineFrames[final.Handle.Slot], finalFrame);
            owner.ordinaryArrowBaseline = this;
            adopted = true;
            return true;
        }

        internal bool OwnsHistorical(in ProjectileSnapshot snapshot)
        {
            if (!adopted || disposed || !ReferenceEquals(owner.ordinaryArrowBaseline, this)) return false;
            foreach (var expected in historical)
                if (snapshot == expected) return true;
            return false;
        }

        internal bool IsPublicationCurrent => adopted && !disposed &&
            ReferenceEquals(owner.ordinaryArrowBaseline, this) &&
            store.TryGet(final.Handle, out var current) && current == final && BindingMatches();

        private bool BindingMatches() => owner.identities.TryCaptureBindingFacts(in key, before.Handle.Slot,
            out var current) && current == binding;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(owner.ordinaryArrowBaseline, this)) owner.ordinaryArrowBaseline = null;
        }
    }
}
