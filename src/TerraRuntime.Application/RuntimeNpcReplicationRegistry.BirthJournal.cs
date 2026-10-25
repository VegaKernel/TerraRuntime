using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private readonly RetainedBirthJournal?[] retainedBirthJournals = new RetainedBirthJournal?[200];

    internal bool TryPrepareGhostMotionFrames(in NpcSnapshot actor, bool departing,
        out byte[]? historical, out byte[]? baseline, out byte[]? retired)
    {
        historical = baseline = retired = null;
        if (!RuntimeNpcPacketProjection.TryCreate(in actor, RuntimeNpcSyncKind.Spawn, out var spawn) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in spawn, out baseline) ||
            !RuntimeNpcPacketProjection.TryCreate(in actor, RuntimeNpcSyncKind.Despawn, out var death) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in death, out retired)) return false;
        if (!departing) return true;
        return RuntimeNpcPacketProjection.TryCreate(in actor, RuntimeNpcSyncKind.Update, out var update) &&
            TerrariaNpcUpdateEncoder.TryEncode(in update, out historical);
    }

    internal void AdoptGhostMotionBaseline(in NpcSnapshot actor, byte[] baseline, byte[] retired)
    {
        Volatile.Write(ref baselineFrames[actor.Handle.Slot], baseline);
        Volatile.Write(ref despawnFrames[actor.Handle.Slot], retired);
        ClearLiveFrame(actor.Handle);
    }

    internal void PublishRetainedGhostMotion(byte[] encoded) => Broadcast(encoded);

    internal void RetirePreparedDeathBaseline(NpcHandle parent)
    {
        RetireGemBunnyHome(parent);
        // The containing sealed death transaction has already removed this exact generation.
        // No callback occurs between removal and retiring its replay baseline.
        Volatile.Write(ref baselineFrames[parent.Slot], null);
        Volatile.Write(ref despawnFrames[parent.Slot], null);
        ClearLiveFrame(parent);
    }

    internal bool TryPrepareRetainedBirth(in NpcSnapshot birth, out RetainedBirthJournal? journal)
    {
        journal = null;
        if (!RuntimeNpcPacketProjection.TryCreate(in birth, RuntimeNpcSyncKind.Spawn, out var spawn) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in spawn, out var encoded) ||
            !RuntimeNpcPacketProjection.TryCreate(in birth, RuntimeNpcSyncKind.Despawn, out var despawn) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in despawn, out var retired)) return false;
        journal = new(this, birth, encoded, retired);
        return journal.TrySealRecipients();
    }

    internal sealed class RetainedBirthJournal(
        RuntimeNpcReplicationRegistry owner, NpcSnapshot birth, byte[] encoded, byte[] retired)
    {
        private readonly record struct Recipient(GameCommandSourceId Source, Endpoint Endpoint, object Occupation);
        private Recipient[] recipients = [];
        private bool sealedRecipients;
        private bool adopted;
        private bool published;
        private RuntimeNpcStore? acceptedStore;
        internal bool Matches(in NpcSnapshot snapshot) => adopted && snapshot == birth;

        internal bool TrySealRecipients()
        {
            if (adopted || published) return false;
            var captured = new List<Recipient>();
            foreach (var pair in owner.endpoints)
            {
                var occupation = pair.Value.CapturePlayingOccupation();
                if (occupation is not null && pair.Value.IsCurrentPlayingOccupation(occupation))
                    captured.Add(new(pair.Key, pair.Value, occupation));
            }
            recipients = captured.ToArray();
            sealedRecipients = true;
            return true;
        }

        internal bool TryAdoptBaseline(RuntimeNpcStore store)
        {
            if (!sealedRecipients || adopted || published ||
                !store.TryGet(birth.Handle, out var current) || current != birth) return false;
            owner.ObserveCatchSlotOwner(in birth);
            owner.ObserveGemBunnyHome(NpcStateCommitKind.Spawn, in birth);
            Volatile.Write(ref owner.baselineFrames[birth.Handle.Slot], encoded);
            Volatile.Write(ref owner.despawnFrames[birth.Handle.Slot], retired);
            owner.ClearLiveFrame(birth.Handle);
            owner.retainedBirthJournals[birth.Handle.Slot] = this;
            acceptedStore = store;
            adopted = true;
            return true;
        }

        internal bool TryPublish()
        {
            if (!adopted || published) return false;
            published = true;
            if (ReferenceEquals(owner.retainedBirthJournals[birth.Handle.Slot], this))
                owner.retainedBirthJournals[birth.Handle.Slot] = null;
            if (acceptedStore is null || !acceptedStore.TryGet(birth.Handle, out var current) || current != birth)
                return true;
            var frame = new OutboundFrame(encoded);
            foreach (var recipient in recipients)
            {
                if (!owner.endpoints.TryGetValue(recipient.Source, out var endpoint) ||
                    !ReferenceEquals(endpoint, recipient.Endpoint) ||
                    !endpoint.IsCurrentPlayingOccupation(recipient.Occupation)) continue;
                if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                    Interlocked.Increment(ref owner.relayedFrames);
                else Interlocked.Increment(ref owner.rejectedFrames);
            }
            owner.RecordLiveFrame(in birth, encoded);
            return true;
        }
    }
}
