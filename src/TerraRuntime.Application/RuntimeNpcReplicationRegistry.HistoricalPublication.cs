using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private RuntimeNpcStore? historicalStateSource;
    private RetainedHistoricalPublication? activeHistoricalPublication;

    internal void BindHistoricalStateSource(RuntimeNpcStore source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (historicalStateSource is not null && !ReferenceEquals(historicalStateSource, source))
            throw new InvalidOperationException("Historical NPC publication is already bound to another store.");
        historicalStateSource = source;
    }

    public void NpcHistoricalStateCommitted(NpcStateCommitKind kind, in NpcSnapshot checkpoint, in NpcSnapshot committed)
    {
        if (activeHistoricalPublication is { } prepared && prepared.Matches(kind, in checkpoint, in committed))
        {
            prepared.TryPublishHead();
            return;
        }
        if (historicalStateSource is not { } source || !source.TryGet(committed.Handle, out var current) || current != committed)
            return;
        if (!RuntimeNpcPacketProjection.TryCreate(in checkpoint,
                kind == NpcStateCommitKind.Spawn ? RuntimeNpcSyncKind.Spawn : RuntimeNpcSyncKind.Update, out var state) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in state, out var frame))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }
        AdoptHistoricalBaseline(in committed);
        ResetSourcePacket23Streams(in checkpoint, kind == NpcStateCommitKind.Spawn, retired: false);
        // Unprepared trusted callers still keep current baselines. Multi-frame source interactions
        // use the prepared journal below, whose recipients were sealed before adoption.
        var expected = committed;
        foreach (var pair in endpoints.ToArray())
        {
            if (!source.TryGet(expected.Handle, out current) || current != expected) break;
            var occupation = pair.Value.CapturePlayingOccupation();
            if (occupation is null || !endpoints.TryGetValue(pair.Key, out var endpoint) ||
                !ReferenceEquals(endpoint, pair.Value) || !endpoint.IsCurrentPlayingOccupation(occupation)) continue;
            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(frame)) == OutboundEnqueueResult.Enqueued)
            {
                endpoint.ResetProximityStream(checkpoint.Handle);
                Interlocked.Increment(ref relayedFrames);
            }
            else Interlocked.Increment(ref rejectedFrames);
        }
    }

    private void AdoptHistoricalBaseline(in NpcSnapshot committed)
    {
        ObserveCatchSlotOwner(in committed);
        ObserveGemBunnyHome(NpcStateCommitKind.Update, in committed);
        RefreshBaselineFrames(in committed);
        retainedBirthJournals[committed.Handle.Slot] = null;
        ClearLiveFrame(committed.Handle);
    }

    private static bool SourcePacket23ResetsAll(in NpcSnapshot snapshot, bool spawnNeedsSyncing, bool retired = false) =>
        spawnNeedsSyncing || retired || snapshot.Simulation.TownNpc == true ||
        snapshot.Simulation.LifeMax > 0 && snapshot.Simulation.Life <= 0 ||
        snapshot.TypeIdentity == VanillaNpcIds.DemonTaxCollector && snapshot.NetId == snapshot.Type ||
        VanillaNpcDefinitionCatalog.TryGet(snapshot.TypeIdentity, snapshot.NetIdentity, out var definition) && definition.IsBoss;

    private void ResetSourcePacket23Streams(in NpcSnapshot checkpoint, bool spawnNeedsSyncing, bool retired)
    {
        // NetMessage.SendData23 clears the global stream and all per-player credits for
        // births, town/boss/inactive actors and the admitted netAlways Demon Tax Collector.
        // Ordinary active heads preserve the global clock and clear only actual recipients.
        if (!SourcePacket23ResetsAll(in checkpoint, spawnNeedsSyncing, retired)) return;
        ClearSourceNetworkRequests(in checkpoint);
        ResetSourceProximity(in checkpoint);
        foreach (var endpoint in endpoints.Values) endpoint.ResetProximityStream(checkpoint.Handle);
    }

    internal bool TryPrepareHistoricalCheckpoint(in NpcSnapshot checkpoint, in NpcStateUpdate final,
        bool spawnNeedsSyncing, ReadOnlySpan<byte[]> tailFrames, out RetainedHistoricalPublication? publication) =>
        TryPrepareHistoricalCheckpoint(in checkpoint, in final, spawnNeedsSyncing, [], tailFrames, out publication);

    internal bool TryPrepareHistoricalCheckpoint(in NpcSnapshot checkpoint, in NpcStateUpdate final,
        bool spawnNeedsSyncing, ReadOnlySpan<byte[]> prefixFrames, ReadOnlySpan<byte[]> tailFrames,
        out RetainedHistoricalPublication? publication)
    {
        publication = null;
        if (prefixFrames.Length + tailFrames.Length > 4 || !RuntimeNpcPacketProjection.TryCreate(in checkpoint,
                spawnNeedsSyncing ? RuntimeNpcSyncKind.Spawn : RuntimeNpcSyncKind.Update, out var historyState) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in historyState, out var historical)) return false;
        var projection = checkpoint with { Type = final.Type, NetId = final.NetId, PositionX = final.PositionX,
            PositionY = final.PositionY, VelocityX = final.VelocityX, VelocityY = final.VelocityY,
            Target = final.Target, Ai = final.Ai, Simulation = final.Simulation };
        if (!RuntimeNpcPacketProjection.TryCreate(in projection, RuntimeNpcSyncKind.Spawn, out var baselineState) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in baselineState, out var baseline) ||
            !RuntimeNpcPacketProjection.TryCreate(in projection, RuntimeNpcSyncKind.Despawn, out var retiredState) ||
            !TerrariaNpcUpdateEncoder.TryEncode(in retiredState, out var retired)) return false;
        var tails = new byte[tailFrames.Length][];
        for (int index = 0; index < tails.Length; index++)
        {
            var frame = tailFrames[index];
            if (frame is null || frame.Length is < 3 or > ushort.MaxValue ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length) return false;
            tails[index] = (byte[])frame.Clone();
        }
        var prefixes = new byte[prefixFrames.Length][];
        for (int index = 0; index < prefixes.Length; index++)
        {
            var frame = prefixFrames[index];
            if (frame is null || frame.Length is < 3 or > ushort.MaxValue ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length) return false;
            prefixes[index] = (byte[])frame.Clone();
        }
        publication = new(this, checkpoint, projection, spawnNeedsSyncing, historical, baseline, retired, tails, prefixes);
        return true;
    }

    internal bool CanAdoptClientHomeBaseline(in NpcSnapshot expected) =>
        historicalStateSource is { } source && source.TryGet(expected.Handle, out var current) && current == expected &&
        expected.Handle.Slot < gemBunnyHomes.Length && gemBunnyHomes[expected.Handle.Slot].Owner == expected.Handle &&
        !gemBunnyHomes[expected.Handle.Slot].Retired &&
        gemBunnyHomes[expected.Handle.Slot].Revision.Value <= expected.Revision.Value;

    internal bool TryAdoptClientHomeBaseline(in NpcSnapshot committed, byte[] homeFrame)
    {
        if (historicalStateSource is not { } source || !source.TryGet(committed.Handle, out var current) ||
            current != committed || !TryDecodeSourceHome(homeFrame, out var home) ||
            home.NpcSlot != committed.Handle.Slot || home.HomeTileX != committed.Simulation.HomeTileX ||
            home.HomeTileY != committed.Simulation.HomeTileY || !home.TryGetStatus(out var status) ||
            (status == TerrariaNpcHomeStatus.Homeless) != committed.Simulation.Homeless) return false;
        // Advance the existing generation/revision stamp without acknowledging old-home fields.
        ObserveGemBunnyHome(NpcStateCommitKind.Update, in committed);
        Volatile.Write(ref townHomeBaselineFrames[committed.Handle.Slot], homeFrame);
        return true;
    }

    internal bool TryAdoptTransformedTownBaselines(in NpcSnapshot committed, byte[] homeFrame, byte[] identityFrame)
    {
        if (historicalStateSource is not { } source || !source.TryGet(committed.Handle, out var current) ||
            current != committed || homeFrame.Length < 3 || homeFrame[2] != (byte)TerrariaMessageId.UpdateNpcHome ||
            identityFrame.Length < 3 || identityFrame[2] != (byte)TerrariaMessageId.UniqueTownNpcInfoSyncRequest) return false;
        Volatile.Write(ref townHomeBaselineFrames[committed.Handle.Slot], homeFrame);
        Volatile.Write(ref townIdentityBaselineFrames[committed.Handle.Slot], identityFrame);
        return true;
    }

    internal bool TryPrepareCatchPublication(RuntimeNpcStore.TerminalRemoval removal,
        ReadOnlySpan<byte[]> tailFrames, out RetainedHistoricalPublication? publication)
    {
        publication = null;
        var caught = removal.Expected;
        bool pending = removal.TryCapturePendingBirth(out _);
        if (tailFrames.Length > 4 ||
            !RuntimeNpcPacketProjection.TryCreate(in caught, RuntimeNpcSyncKind.Despawn, out var state) ||
            !TerrariaNpcUpdateEncoder.TryEncode(state with { SpawnNeedsSyncing = pending }, out var head)) return false;
        var tails = new byte[tailFrames.Length][];
        for (int index = 0; index < tails.Length; index++)
        {
            var frame = tailFrames[index];
            if (frame is null || frame.Length is < 3 or > ushort.MaxValue ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length) return false;
            tails[index] = (byte[])frame.Clone();
        }
        publication = new(this, caught, caught, pending, head, [], [], tails);
        return true;
    }

    internal sealed class RetainedHistoricalPublication(RuntimeNpcReplicationRegistry owner,
        NpcSnapshot checkpoint, NpcSnapshot projection, bool spawnNeedsSyncing,
        byte[] head, byte[] baseline, byte[] retired, byte[][] tails, byte[][]? prefixes = null)
    {
        private readonly record struct Recipient(GameCommandSourceId Source, Endpoint Endpoint, object Occupation);
        private Recipient[] recipients = [];
        private RuntimeNpcStore? store;
        private RuntimeNpcStore.TerminalRemoval? retiredSource;
        private NpcSnapshot accepted;
        private bool sealedRecipients;
        private bool adopted;
        private bool published;
        private bool headAttempted;
        private Func<bool>? tailCurrent;

        internal bool TrySealRecipients()
        {
            if (sealedRecipients || adopted || published) return false;
            var captured = new List<Recipient>();
            foreach (var pair in owner.endpoints)
            {
                var occupation = pair.Value.CapturePlayingOccupation();
                if (occupation is not null && pair.Value.IsCurrentPlayingOccupation(occupation))
                    captured.Add(new(pair.Key, pair.Value, occupation));
            }
            recipients = captured.ToArray(); sealedRecipients = true;
            return true;
        }

        internal bool TryAdoptBaseline(RuntimeNpcStore source, in NpcSnapshot committed)
        {
            if (!sealedRecipients || adopted || published || committed.Handle != projection.Handle ||
                (committed with { Revision = projection.Revision }) != projection ||
                !source.TryGet(committed.Handle, out var current) || current != committed) return false;
            owner.BindHistoricalStateSource(source);
            owner.ObserveCatchSlotOwner(in committed);
            owner.ObserveGemBunnyHome(NpcStateCommitKind.Update, in committed);
            Volatile.Write(ref owner.baselineFrames[committed.Handle.Slot], baseline);
            Volatile.Write(ref owner.despawnFrames[committed.Handle.Slot], retired);
            owner.retainedBirthJournals[committed.Handle.Slot] = null;
            owner.ClearLiveFrame(committed.Handle);
            store = source; accepted = committed; adopted = true;
            return true;
        }

        internal bool TryAdoptRetiredBaseline(RuntimeNpcStore source, RuntimeNpcStore.TerminalRemoval removal)
        {
            if (!sealedRecipients || adopted || published || removal.Expected != checkpoint ||
                !removal.IsAcceptedCurrent) return false;
            owner.BindHistoricalStateSource(source);
            owner.ObserveCatchSlotOwner(in checkpoint);
            owner.RetireGemBunnyHome(checkpoint.Handle);
            Volatile.Write(ref owner.baselineFrames[checkpoint.Handle.Slot], null);
            Volatile.Write(ref owner.despawnFrames[checkpoint.Handle.Slot], null);
            owner.retainedBirthJournals[checkpoint.Handle.Slot] = null;
            owner.ClearLiveFrame(checkpoint.Handle);
            store = source; accepted = checkpoint; retiredSource = removal; adopted = true;
            return true;
        }

        private bool IsCurrent() => adopted && (retiredSource is not null
            ? retiredSource.IsAcceptedCurrent : store is not null &&
                store.TryGet(accepted.Handle, out var current) && current == accepted);

        internal bool MatchesCatch(in NpcSnapshot caught, bool pending) => adopted && retiredSource is not null &&
            caught == checkpoint && pending == spawnNeedsSyncing;


        internal bool Matches(NpcStateCommitKind kind, in NpcSnapshot historical, in NpcSnapshot committed) =>
            adopted && retiredSource is null && historical == checkpoint && committed == accepted &&
            kind == (spawnNeedsSyncing ? NpcStateCommitKind.Spawn : NpcStateCommitKind.ForcedUpdate);

        internal bool MatchesLegacy(NpcStateCommitKind kind, in NpcSnapshot snapshot) => adopted &&
            (retiredSource is not null
                ? snapshot == checkpoint && kind is NpcStateCommitKind.Spawn or NpcStateCommitKind.Despawn
                : snapshot == accepted && kind == (spawnNeedsSyncing ? NpcStateCommitKind.Spawn : NpcStateCommitKind.ForcedUpdate));

        internal bool TryPublishHead()
        {
            if (!adopted || headAttempted) return false;
            headAttempted = true;
            foreach (var prefix in prefixes ?? []) PublishFrame(prefix, checkTail: true);
            if (IsCurrent()) owner.ResetSourcePacket23Streams(in checkpoint, spawnNeedsSyncing, retiredSource is not null);
            PublishFrame(head, checkTail: false, resetRecipientStream: true);
            return true;
        }

        private void PublishFrame(byte[] encoded, bool checkTail, bool resetRecipientStream = false)
        {
            var frame = new OutboundFrame(encoded);
            foreach (var recipient in recipients)
            {
                if (!IsCurrent() || checkTail && tailCurrent is not null && !tailCurrent()) break;
                if (!owner.endpoints.TryGetValue(recipient.Source, out var endpoint) ||
                    !ReferenceEquals(endpoint, recipient.Endpoint) ||
                    !endpoint.IsCurrentPlayingOccupation(recipient.Occupation)) continue;
                if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                {
                    if (resetRecipientStream) endpoint.ResetProximityStream(checkpoint.Handle);
                    Interlocked.Increment(ref owner.relayedFrames);
                }
                else Interlocked.Increment(ref owner.rejectedFrames);
            }
        }

        internal bool Publish(Func<bool> publishCheckpoint, Func<bool>? ownedTailCurrent = null)
        {
            ArgumentNullException.ThrowIfNull(publishCheckpoint);
            if (!adopted || published) return false;
            published = true; tailCurrent = ownedTailCurrent;
            var previous = owner.activeHistoricalPublication;
            owner.activeHistoricalPublication = this;
            ExceptionDispatchInfo? failure = null;
            bool checkpointAccepted = false;
            try { checkpointAccepted = publishCheckpoint(); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            finally { owner.activeHistoricalPublication = previous; }
            if (!checkpointAccepted && failure is null) return false;
            if (!headAttempted)
            {
                try { TryPublishHead(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            foreach (var tail in tails)
            {
                try { PublishFrame(tail, checkTail: true); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            failure?.Throw();
            return true;
        }
    }
}
