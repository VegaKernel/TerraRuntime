using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileReplicationRegistry
{
    private SpawnBirthJournalPreparation? observerBirthJournal;

    private bool IsAcceptedBirthObserver(in ProjectileSnapshot snapshot)
    {
        for (var journal = observerBirthJournal; journal is not null; journal = journal.previousObserverJournal)
            if (journal.ContainsBirth(in snapshot)) return true;
        return false;
    }

    internal bool TryPrepareSpawnBirthJournal(
        ReadOnlySpan<ProjectileSnapshot> births,
        ReadOnlySpan<TerrariaProjectileKeyState> keys,
        GameCommandSourceId excludedSource,
        out SpawnBirthJournalPreparation? journal)
    {
        journal = null;
        if (births.Length is < 1 or > 8 || births.Length != keys.Length)
            return false;
        var retained = births.ToArray();
        var retainedKeys = keys.ToArray();
        var frames = new byte[births.Length][];
        var bindings = new RuntimeProjectileWireIdentityRegistry.BindingFacts[births.Length];
        var finalIndices = new List<int>(births.Length);
        for (int i = 0; i < births.Length; i++)
        {
            ref readonly var birth = ref births[i];
            ref readonly var key = ref keys[i];
            if (!birth.IsActive || !birth.Handle.IsAssigned || key.Spawner != birth.Spawner ||
                !identities.TryCaptureBindingFacts(in key, birth.Handle.Slot, out bindings[i]) ||
                !RuntimeProjectilePacketProjection.TryCreateUpdate(in birth, in key, out var state) ||
                !TerrariaProjectileEncoder.TryEncodeUpdate(in state, out frames[i]))
                return false;
            for (int previous = 0; previous < i; previous++)
                if (keys[previous] == key || births[previous].Handle == birth.Handle ||
                    births[previous].Handle.Slot == birth.Handle.Slot &&
                    births[previous].Handle.Generation.Value >= birth.Handle.Generation.Value)
                    return false;
            ushort slot = birth.Handle.Slot;
            int existing = finalIndices.FindIndex(index => retained[index].Handle.Slot == slot);
            if (existing >= 0) finalIndices[existing] = i;
            else finalIndices.Add(i);
        }
        finalIndices.Sort();
        journal = new(this, retained, retainedKeys, frames, bindings, finalIndices.ToArray(), excludedSource);
        return journal.TrySealRecipients();
    }

    internal sealed class SpawnBirthJournalPreparation(
        RuntimeProjectileReplicationRegistry owner,
        ProjectileSnapshot[] births,
        TerrariaProjectileKeyState[] keys,
        byte[][] frames,
        RuntimeProjectileWireIdentityRegistry.BindingFacts[] beforeBindings,
        int[] finalIndices,
        GameCommandSourceId excludedSource)
    {
        private readonly record struct Recipient(GameCommandSourceId Source, Endpoint Endpoint, object Occupation);
        private Recipient[] recipients = [];
        private bool recipientsSealed;
        private bool adopted;
        private bool published;
        private bool framesPublished;
        internal SpawnBirthJournalPreparation? previousObserverJournal;
        private readonly RuntimeProjectileReplicationRegistry registry = owner;
        private bool observing;
        private readonly RuntimeProjectileWireIdentityRegistry.BindingFacts[] acceptedBindings =
            new RuntimeProjectileWireIdentityRegistry.BindingFacts[finalIndices.Length];

        internal bool TrySealRecipients()
        {
            if (adopted || published)
                return false;
            // Allocate the final recipient census before any producer owner is written.
            // Bot callers reseal after external providers; other callers finish only pure guards.
            var captured = new List<Recipient>();
            foreach (var pair in registry.endpoints)
            {
                if (!excludedSource.IsSystem && pair.Key == excludedSource)
                    continue;
                var occupation = pair.Value.CapturePlayingOccupation();
                if (occupation is not null && pair.Value.IsCurrentPlayingOccupation(occupation))
                    captured.Add(new(pair.Key, pair.Value, occupation));
            }
            recipients = captured.ToArray();
            recipientsSealed = true;
            return true;
        }

        internal bool IsCurrentOwned
        {
            get
            {
                if (adopted || published) return false;
                for (int i = 0; i < births.Length; i++)
                    if (!registry.identities.TryCaptureBindingFacts(in keys[i], births[i].Handle.Slot, out var current) ||
                        current != beforeBindings[i])
                        return false;
                return true;
            }
        }

        internal bool TryAdoptBaselines(RuntimeProjectileStore store)
        {
            if (!recipientsSealed || !IsCurrentOwned) return false;
            foreach (int index in finalIndices)
                if (!store.TryGet(births[index].Handle, out var current) || current != births[index])
                    return false;

            // Keys/slots were validated before this callback-free tail. Historical births must never rebind
            // the last physical generation or overwrite its replay baseline during subsequent publication.
            foreach (int index in finalIndices)
            {
                var birth = births[index];
                if (!registry.identities.TryBind(in keys[index], birth.Handle))
                    throw new InvalidOperationException("Validated projectile birth identity could not be adopted.");
                Volatile.Write(ref registry.netSpam[birth.Handle.Slot], 0);
                Volatile.Write(ref registry.baselineFrames[birth.Handle.Slot], frames[index]);
                registry.UpdateLiveFrame(birth.Handle, frames[index]);
            }
            for (int i = 0; i < finalIndices.Length; i++)
            {
                int index = finalIndices[i];
                registry.identities.TryCaptureBindingFacts(in keys[index], births[index].Handle.Slot, out acceptedBindings[i]);
            }
            adopted = true;
            return true;
        }

        internal bool TryPublish(RuntimeProjectileStore store)
        {
            if (!adopted || published) return false;
            // Consume the token before any publication, including a stale accepted generation.
            published = true;
            for (int i = 0; i < finalIndices.Length; i++)
            {
                int index = finalIndices[i];
                if (!store.TryGet(births[index].Handle, out var current) || current != births[index] ||
                    !registry.identities.TryCaptureBindingFacts(in keys[index], births[index].Handle.Slot, out var binding) ||
                    binding != acceptedBindings[i])
                    return false;
            }
            // New playing occupations already receive the adopted baseline. Only the sealed recipients
            // receive this birth journal, avoiding baseline-plus-birth duplicates during observer reentry.
            foreach (byte[] encoded in frames)
            {
                var frame = new TerraRuntime.Network.OutboundFrame(encoded);
                foreach (var recipient in recipients)
                {
                    if (!registry.endpoints.TryGetValue(recipient.Source, out var current) ||
                        !ReferenceEquals(current, recipient.Endpoint) ||
                        !current.IsCurrentPlayingOccupation(recipient.Occupation))
                        continue;
                    if (current.Outbound.TryEnqueue(frame) == TerraRuntime.Network.OutboundEnqueueResult.Enqueued)
                        Interlocked.Increment(ref registry.relayedFrames);
                    else
                        Interlocked.Increment(ref registry.rejectedFrames);
                }
            }
            framesPublished = true;
            return true;
        }

        internal IDisposable EnterObserverPublication()
        {
            if (!framesPublished || observing)
                throw new InvalidOperationException("Projectile birth frames must be published before notifying observers.");
            observing = true;
            previousObserverJournal = registry.observerBirthJournal;
            registry.observerBirthJournal = this;
            return new ObserverPublication(this);
        }

        internal bool ContainsBirth(in ProjectileSnapshot snapshot)
        {
            foreach (var birth in births)
                if (birth == snapshot) return true;
            return false;
        }

        private sealed class ObserverPublication(SpawnBirthJournalPreparation journal) : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                journal.registry.observerBirthJournal = journal.previousObserverJournal;
                journal.previousObserverJournal = null;
                journal.observing = false;
            }
        }
    }
}
