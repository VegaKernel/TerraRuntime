using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core.Worlds;

public sealed partial class RuntimeWorldItemStore
{
    private readonly WorldItemStackTransfer1458[] sourceTransfers = new WorldItemStackTransfer1458[VanillaCapacity];
    private int sourceTransferCount;
    private IWorldItemOwnerFactsProvider1458? ownerFactsProvider;
    private SlotState[]? deathSourceSlots;
    private WorldItemStackTransfer1458[]? deathSourceTransfers;
    private int deathSourceTransferCount;

    // Same scalar allocation/protection/transfer state, independent slots and no publication sink.
    // Whole-operation admission uses this fork; its mutations never lease or publish live items.
    internal RuntimeWorldItemStore CreateDeathPreview()
    {
        var preview = new RuntimeWorldItemStore();
        _slots.CopyTo(preview._slots, 0);
        preview._activeCount = _activeCount;
        sourceTransfers.CopyTo(preview.sourceTransfers, 0);
        preview.sourceTransferCount = sourceTransferCount;
        preview.ownerFactsProvider = ownerFactsProvider;
        preview.deathSourceSlots = (SlotState[])_slots.Clone();
        preview.deathSourceTransfers = (WorldItemStackTransfer1458[])sourceTransfers.Clone();
        preview.deathSourceTransferCount = sourceTransferCount;
        return preview;
    }

    internal bool IsDeathPreviewSourceCurrent(RuntimeWorldItemStore preview) =>
        preview.deathSourceSlots is not null && preview.deathSourceTransfers is not null &&
        ReferenceEquals(ownerFactsProvider, preview.ownerFactsProvider) &&
        sourceTransferCount == preview.deathSourceTransferCount &&
        _slots.AsSpan().SequenceEqual(preview.deathSourceSlots) &&
        sourceTransfers.AsSpan(0, sourceTransferCount).SequenceEqual(preview.deathSourceTransfers.AsSpan(0, sourceTransferCount));

    /// <summary>One-time binding before the world exposes commands; a reused store cannot retain another world's players.</summary>
    public void AttachOwnerFactsProvider(IWorldItemOwnerFactsProvider1458 provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (ownerFactsProvider is not null) throw new InvalidOperationException("World item owner facts are already bound.");
        ownerFactsProvider = provider;
    }

    public bool TryGetSourceOwnerMetadata(WorldItemHandle handle, out int age, out bool releaseRequested)
    {
        age = 0; releaseRequested = false;
        if (!handle.IsAssigned || !IsValidSlot(handle.Slot)) return false;
        SpinWait spin = default;
        while (true)
        {
            int version = ReadStableVersion(ref spin); var state = _slots[handle.Slot];
            if (version != ReadVersion()) continue;
            if (!state.Active || state.Generation != handle.Generation.Value) return false;
            age = state.SourceOwnerAge; releaseRequested = state.SourceReleaseRequested; return true;
        }
    }

    public bool TryRequestSourceOwnerRelease(WorldItemHandle handle)
    {
        WorldItemSnapshot snapshot;
        if (!handle.IsAssigned || !IsValidSlot(handle.Slot)) return false;
        BeginWrite();
        try
        {
            ref var state = ref _slots[handle.Slot];
            if (!state.Active || state.Claimed || state.Generation != handle.Generation.Value ||
                state.Update.OwnerPlayerId == byte.MaxValue || state.SourceReleaseRequested || !TryAdvance(ref state.Revision)) return false;
            state.SourceReleaseRequested = true; state.SourceOwnerAge = -1;
            snapshot = Capture(handle.Slot, state);
        }
        finally { EndWrite(); }
        Publish(WorldItemStateCommitKind.OwnershipReleaseRequested, snapshot);
        return true;
    }

    public bool HasPendingSourceTransfer(WorldItemHandle handle)
    {
        if (!handle.IsAssigned) return false;
        for (int index = 0; index < sourceTransferCount; index++)
        {
            var transfer = sourceTransfers[index];
            if ((transfer.Source == handle.Slot && transfer.SourceGeneration == handle.Generation.Value) ||
                (transfer.Destination == handle.Slot && transfer.DestinationGeneration == handle.Generation.Value)) return true;
        }
        return false;
    }

    public bool TryProcessPendingSourceTransfers()
    {
        if (sourceTransferCount == 0) return true;
        using var preview = CreateAllocationPreview();
        return preview.TryProcessPendingTransfers() && preview.TryClaim() && preview.TryCommitNext(out _, out _);
    }

    private static int InitialSourceAge(int type) =>
        VanillaWorldItemAllocationCatalog1458.TryGet(type, out var facts) ? facts.InitialAge : 0;

    public AllocationPreview CreateAllocationPreview(ReadOnlySpan<WorldItemAllocationPlayer1458> players = default)
        => new(this, players);

    public bool TryAllocateSourceDrop(in WorldItemDropStateUpdate drop, ReadOnlySpan<WorldItemAllocationPlayer1458> players,
        out WorldItemSnapshot snapshot, out short selectedSlot, byte? sourceLocalPlayerId = null,
        WorldItemCreationSource1458 creationSource = WorldItemCreationSource1458.NewItem)
    {
        snapshot = default; selectedSlot = -1;
        using var preview = CreateAllocationPreview(players);
        if (!preview.TrySpawnSource(in drop, 0, out _, sourceLocalPlayerId, creationSource) || !preview.TryClaim() ||
            !preview.TryCommitNext(out selectedSlot, out _)) return false;
        return selectedSlot == VanillaCapacity || TryGetActive(selectedSlot, out snapshot);
    }

    public bool TryGetAllocationMetadata(short slot, out int age, out int reuseTicks)
    {
        age = reuseTicks = 0;
        if (!IsValidSlot(slot)) return false;
        SpinWait spin = default;
        while (true)
        {
            int version = ReadStableVersion(ref spin); var state = _slots[slot];
            if (version != ReadVersion()) continue;
            age = state.SourceAge; reuseTicks = state.SourceReuseTicks; return true;
        }
    }

    internal bool TrySetLeaseCooldown(in WorldItemDropReservation reservation, int ticks)
    {
        if (ticks <= 0 || !HasDropReservation(in reservation)) return false;
        BeginWrite();
        try { ref var state = ref _slots[reservation.Slot]; state.SourceLease = true; state.SourceReuseTicks = ticks; state.SourceAge = InitialSourceAge(state.Update.ItemNetId); return true; }
        finally { EndWrite(); }
    }

    internal bool IsAllocationClaimed(short slot) => IsValidSlot(slot) && _slots[slot].Claimed;

    internal void UpdateLeaseCooldown(in WorldItemDropReservation reservation, int ticks)
    {
        if (!HasDropReservation(in reservation)) return;
        BeginWrite();
        try { _slots[reservation.Slot].SourceReuseTicks = Math.Max(0, ticks); }
        finally { EndWrite(); }
    }

    /// <summary>
    /// Owns a detached, bounded sequential Item.NewItem allocation plan. Source physical slots remain visible
    /// until the caller accepts its NPC/world mutation. Unknown temporary reservations are never source eviction candidates.
    /// </summary>
    public sealed class AllocationPreview : IDisposable
    {
        // Runtime admission policy: a source plan exceeding this operation budget is rejected before live mutation.
        public const int MaximumOperations = 4096;
        private readonly RuntimeWorldItemStore owner;
        private readonly SlotState[] baseline;
        private readonly SlotState[] working;
        private readonly SlotState[] expected;
        private readonly WorldItemAllocationPlayer1458[] players;
        private readonly IWorldItemOwnerFactsProvider1458? capturedProvider;
        private readonly IWorldItemOwnerFactsSnapshot1458? ownerFacts;
        private readonly WorldItemStackTransfer1458[] pending;
        private readonly bool[] touched = new bool[VanillaCapacity];
        private readonly List<Operation> operations = new();
        private readonly List<Step> steps = new();
        private int pendingCount;
        private int baselinePending;
        private int expectedPending;
        private readonly WorldItemStackTransfer1458[] expectedTransfers;
        private int committed;
        private bool claimed, failed, disposed;

        internal AllocationPreview(RuntimeWorldItemStore owner, ReadOnlySpan<WorldItemAllocationPlayer1458> players)
        {
            this.owner = owner; this.players = players.ToArray();
            capturedProvider = owner.ownerFactsProvider;
            ownerFacts = capturedProvider?.Capture(); // Outside store locks, on the authoritative world thread.
            if (players.Length > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(players));
            foreach (var player in players)
                if (!float.IsFinite(player.X) || !float.IsFinite(player.Y) || player.Width <= 0 || player.Height <= 0 ||
                    (double)player.X + player.Width * 0.5 < int.MinValue + 1160.0 ||
                    (double)player.X + player.Width * 0.5 > int.MaxValue - 1160.0 ||
                    (double)player.Y + player.Height * 0.5 < int.MinValue + 800.0 ||
                    (double)player.Y + player.Height * 0.5 > int.MaxValue - 800.0) failed = true;
            baseline = (SlotState[])owner._slots.Clone(); working = (SlotState[])baseline.Clone(); expected = (SlotState[])baseline.Clone();
            expectedTransfers = (WorldItemStackTransfer1458[])owner.sourceTransfers.Clone();
            pending = (WorldItemStackTransfer1458[])owner.sourceTransfers.Clone(); expectedPending = baselinePending = pendingCount = owner.sourceTransferCount;
        }

        public int Count => steps.Count;

        // Source WorldItem.VoodooDollLavaDeath turns the whole item to air BEFORE NPC loot allocates.
        // Retain that exact-generation removal in the same claim as the following NewItem operations.
        internal bool TryRemoveSource(in WorldItemSnapshot item)
        {
            if (disposed || claimed || failed || steps.Count != 0 || !item.Handle.IsAssigned ||
                !IsValidSlot(item.Handle.Slot)) return Fail();
            short slot = item.Handle.Slot;
            ref var state = ref working[slot];
            if (!state.Active || state.Claimed || state.Reserved || Capture(slot, in state) != item) return Fail();
            int start = operations.Count;
            state.Active = false;
            state.Update = default;
            // Ordinary TryRemove preserves the slot's generation/revision until its next allocation.
            if (!Record(slot, WorldItemStateCommitKind.Remove)) return Fail();
            steps.Add(new(start, operations.Count, slot, 0, pending[..pendingCount].ToArray(), null));
            return true;
        }
        public bool IsCurrent => ValidateOwnerFacts() && IsCurrentOwned;

        // External owner queries finish before the callback-free final store guard/adoption.
        internal bool ValidateOwnerFacts() => !disposed && !failed &&
            ReferenceEquals(owner.ownerFactsProvider, capturedProvider) && ownerFacts?.IsCurrent != false;

        internal bool IsCurrentOwned
        {
            get
            {
                if (disposed || failed || owner.sourceTransferCount != expectedPending ||
                    !ReferenceEquals(owner.ownerFactsProvider, capturedProvider)) return false;
                if (!owner.sourceTransfers.AsSpan(0, expectedPending).SequenceEqual(expectedTransfers.AsSpan(0, expectedPending))) return false;
                for (int i = 0; i < baseline.Length; i++)
                {
                    var actual = owner._slots[i]; actual.Claimed = false;
                    if (actual != (claimed ? expected[i] : baseline[i]) || owner._slots[i].Claimed != (claimed && touched[i])) return false;
                }
                return true;
            }
        }

        public bool TrySpawnSource(in WorldItemDropStateUpdate drop, int leaseTicks, out short slot, byte? sourceLocalPlayerId = null,
            WorldItemCreationSource1458 creationSource = WorldItemCreationSource1458.NewItem)
        {
            slot = -1;
            if (disposed || claimed || failed || steps.Count == VanillaCapacity || leaseTicks < 0 ||
                creationSource is not (WorldItemCreationSource1458.NewItem or WorldItemCreationSource1458.ClientSynchronization) || !IsValidDrop(in drop) ||
                !VanillaWorldItemAllocationCatalog1458.TryGet(drop.ItemNetId, out var facts)) return Fail();
            var publicationDrop = creationSource == WorldItemCreationSource1458.ClientSynchronization
                ? drop with { Ownership = WorldItemOwnershipMode.None } : drop;
            foreach (var retained in working)
                if (retained.Reserved && !retained.SourceLease) return Fail();
            int start = operations.Count;
            Span<WorldItemAllocationState1458> items = stackalloc WorldItemAllocationState1458[VanillaCapacity];
            CaptureItems(items);
            int selected = VanillaWorldItemAllocation1458.FindFreeOrPickup(items, out bool emergency);
            if (emergency)
            {
                if (!VanillaWorldItemAllocation1458.TryRankTransfers(items, players, pending, pendingCount, out int ranked)) return Fail();
                pendingCount = ranked;
                if (!ProcessTransfers(true, out int freed)) return Fail();
                if (freed < VanillaCapacity) selected = freed;
            }
            if (selected == VanillaCapacity)
            { CaptureItems(items); selected = VanillaWorldItemAllocation1458.FindOldest(items); }
            slot = (short)selected;
            // Source MakeInstanced on sentinel400 needs a separate client-local wire/expiry contract.
            // It is not an addressable authoritative slot, so that branch remains precommit closed.
            if (selected == VanillaCapacity && leaseTicks != 0) return Fail();
            WorldItemSentinelCommit1458? transient = null;
            if (selected == VanillaCapacity && owner._commitSink is IWorldItemSentinelCommitSink1458)
            {
                WorldItemOwnerStateUpdate? sentinelOwner = null;
                var transientDrop = publicationDrop;
                if (drop.Ownership == WorldItemOwnershipMode.ReserveForLocalPlayer)
                {
                    if (sourceLocalPlayerId is not { } local || local == byte.MaxValue) return Fail();
                    bool active = false; foreach (var player in players) if (player.Slot == local) { active = true; break; }
                    if (!active) return Fail();
                    sentinelOwner = new(local, 100, 0, 0, drop.PositionX, drop.PositionY);
                }
                else if (drop.Ownership == WorldItemOwnershipMode.None || drop.Ownership == WorldItemOwnershipMode.GrabDelayForLocalPlayer)
                {
                    int delay = 0; byte delayPlayer = 0;
                    if (drop.Ownership == WorldItemOwnershipMode.GrabDelayForLocalPlayer)
                    {
                        if (sourceLocalPlayerId is not { } local || local == byte.MaxValue) return Fail();
                        delay = 100; delayPlayer = local;
                    }
                    if (ownerFacts is not null)
                    {
                        if (!ownerFacts.TrySelectOwner(in drop, delay, delayPlayer, out byte selectedOwner)) return Fail();
                        if (selectedOwner != byte.MaxValue)
                            sentinelOwner = new(selectedOwner, 15, delayPlayer, delay, drop.PositionX, drop.PositionY);
                    }
                    else if (players.Length != 0 || delay != 0) return Fail();
                }
                transient = new(transientDrop, sentinelOwner);
            }
            if (selected < VanillaCapacity)
            {
                ref var state = ref working[selected];
                if (state.Generation == ulong.MaxValue || state.Claimed || (state.Reserved && !state.SourceLease)) return Fail();
                // The server removes an active item before assigning the new generation; inactive instanced copies
                // are replaced by the new packet21/90, without an invented preceding removal.
                if (state.Active)
                {
                    state.Active = false;
                    if (!RecordSync(slot, WorldItemStateCommitKind.Remove)) return Fail();
                }
                int kept = 0;
                for (int index = 0; index < pendingCount; index++)
                    if (pending[index].Source != slot && pending[index].Destination != slot) pending[kept++] = pending[index];
                pendingCount = kept;
                state.Generation++; state.Revision = leaseTicks == 0 ? 1UL : 0UL;
                state.Active = leaseTicks == 0; state.Reserved = leaseTicks != 0; state.SourceLease = leaseTicks != 0;
                state.SourceReuseTicks = leaseTicks; state.SourceAge = facts.InitialAge; state.SourceOwnerAge = 0; state.SourceReleaseRequested = false; state.Update = CreateInitial(in publicationDrop);
                if (leaseTicks == 0) state.Update = state.Update with { Color = null };
                if (leaseTicks == 0 && ownerFacts is not null)
                    state.Update = state.Update with { GrabDelayPlayer = 0 };
                if (drop.Ownership == WorldItemOwnershipMode.GrabDelayForAllPlayers)
                    state.Update = state.Update with { GrabDelayTime = 100, GrabDelayPlayer = byte.MaxValue };
                if (leaseTicks == 0 && drop.Ownership == WorldItemOwnershipMode.GrabDelayForLocalPlayer)
                {
                    if (sourceLocalPlayerId is not { } local || local == byte.MaxValue) return Fail();
                    state.Update = state.Update with { GrabDelayTime = 100, GrabDelayPlayer = local };
                }
                if (!Record(slot, leaseTicks == 0 ? WorldItemStateCommitKind.Drop : null)) return Fail();
                // Client MessageBuffer21 applies the requested reservation after its source21.
                // Retain it in this claimed plan; an external owner update cannot mutate a claimed slot.
                if (leaseTicks == 0 && creationSource == WorldItemCreationSource1458.ClientSynchronization &&
                    drop.Ownership == WorldItemOwnershipMode.ReserveForLocalPlayer)
                {
                    if (sourceLocalPlayerId is not { } local || local == byte.MaxValue) return Fail();
                    state.Update = state.Update with { OwnerPlayerId = local, TimeToKeepReservation = 100, GrabDelayPlayer = 0, GrabDelayTime = 0 };
                    if (!RecordSync(slot, WorldItemStateCommitKind.Owner)) return Fail();
                }
                // Dedicated NewItem(noBroadcast:true) skips ApplySpawnOwnership before MakeInstanced90.
                // Ordinary source NewItem publishes21, then FindOwner may publish22.
                if (leaseTicks == 0 && ownerFacts is not null &&
                    drop.Ownership is WorldItemOwnershipMode.None or WorldItemOwnershipMode.GrabDelayForLocalPlayer)
                {
                    if (!ownerFacts.TrySelectOwner(in drop, state.Update.GrabDelayTime, state.Update.GrabDelayPlayer, out byte selectedOwner)) return Fail();
                    if (selectedOwner != byte.MaxValue)
                    {
                        state.Update = state.Update with { OwnerPlayerId = selectedOwner, TimeToKeepReservation = 15 };
                        if (!RecordSync(slot, WorldItemStateCommitKind.Owner)) return Fail();
                    }
                }
            }
            // CommonCode modifies item tint after Item.NewItem has published its drop and owner.
            if (slot < VanillaCapacity && leaseTicks == 0 && publicationDrop.Color is { } tint)
            {
                working[slot].Update = working[slot].Update with { Color = tint };
                if (!RecordSync(slot, WorldItemStateCommitKind.Color)) return Fail();
            }
            steps.Add(new(start, operations.Count, slot, leaseTicks, pending[..pendingCount].ToArray(), transient));
            return true;
        }

        public bool TryClaim()
        {
            if (disposed || failed || claimed || !IsCurrent) return false;
            owner.BeginWrite();
            try { for (int i = 0; i < touched.Length; i++) if (touched[i]) owner._slots[i].Claimed = true; }
            finally { owner.EndWrite(); }
            claimed = true; return true;
        }

        /// <summary>Commits one already staged source allocation, without defaults or random draws.</summary>
        public bool TryCommitNext(out short slot, out WorldItemDropReservation lease)
        {
            slot = -1; lease = default;
            if (!claimed || disposed || failed || committed >= steps.Count || !IsCurrent) return false;
            var step = steps[committed];
            for (int i = step.Start; i < step.End; i++)
            {
                var operation = operations[i]; var before = owner._slots[operation.Slot]; var after = operation.After;
                after.Claimed = true;
                owner.BeginWrite();
                try
                {
                    owner._slots[operation.Slot] = after;
                    owner._activeCount += (after.Active ? 1 : 0) - (before.Active ? 1 : 0);
                    expected[operation.Slot] = operation.After;
                }
                finally { owner.EndWrite(); }
                if (operation.Kind is {} kind)
                {
                    var snapshot = kind == WorldItemStateCommitKind.Remove ? Capture(operation.Slot, before) : Capture(operation.Slot, after);
                    owner.Publish(kind, in snapshot);
                }
            }
            owner.sourceTransferCount = step.Pending.Length;
            step.Pending.CopyTo(owner.sourceTransfers, 0);
            // IsCurrent compares the pending owner only at commit checkpoints, not the final detached preview count.
            expectedPending = step.Pending.Length;
            step.Pending.CopyTo(expectedTransfers, 0);
            slot = step.Slot;
            if (step.LeaseTicks != 0 && slot < VanillaCapacity)
                lease = new(slot, new(owner._slots[slot].Generation));
            if (step.Transient is { } transient && owner._commitSink is IWorldItemSentinelCommitSink1458 sink)
                sink.WorldItemSentinelCommitted(in transient);
            committed++;
            return true;
        }

        // The application validates external owners before its final pure NPC/player/projectile guards.
        // This adoption never invokes a provider or sink and releases claims before publication.
        internal bool TryAdoptUnpublished(out AllocationPublication? publication)
        {
            publication = null;
            if (!claimed || committed != 0 || !IsCurrentOwned)
                return false;

            var retained = new AllocationPublication(this);
            owner.BeginWrite();
            try
            {
                foreach (var operation in operations)
                {
                    var before = owner._slots[operation.Slot];
                    owner._slots[operation.Slot] = operation.After;
                    owner._activeCount += (operation.After.Active ? 1 : 0) - (before.Active ? 1 : 0);
                    expected[operation.Slot] = operation.After;
                }
                if (steps.Count != 0)
                {
                    var finalStep = steps[^1];
                    owner.sourceTransferCount = finalStep.Pending.Length;
                    finalStep.Pending.CopyTo(owner.sourceTransfers, 0);
                    expectedPending = finalStep.Pending.Length;
                    finalStep.Pending.CopyTo(expectedTransfers, 0);
                }
                for (int index = 0; index < touched.Length; index++)
                    if (touched[index]) owner._slots[index].Claimed = false;
            }
            finally
            {
                owner.EndWrite();
            }
            committed = steps.Count;
            claimed = false;
            disposed = true;
            publication = retained;
            return true;
        }

        internal sealed class AllocationPublication
        {
            private readonly RuntimeWorldItemStore owner;
            private readonly SlotState[] final;
            private readonly PublicationOperation[] operations;
            private readonly PublicationStep[] steps;
            private int next;
            private bool publishing;

            internal AllocationPublication(AllocationPreview preview)
            {
                owner = preview.owner;
                final = (SlotState[])preview.working.Clone();
                operations = new PublicationOperation[preview.operations.Count];
                var previous = (SlotState[])preview.baseline.Clone();
                for (int index = 0; index < operations.Length; index++)
                {
                    var operation = preview.operations[index];
                    var snapshot = operation.Kind is null ? default : operation.Kind == WorldItemStateCommitKind.Remove
                        ? Capture(operation.Slot, in previous[operation.Slot])
                        : Capture(operation.Slot, operation.After);
                    operations[index] = new(operation.Slot, operation.Kind, snapshot);
                    previous[operation.Slot] = operation.After;
                }
                steps = new PublicationStep[preview.steps.Count];
                for (int index = 0; index < steps.Length; index++)
                {
                    var step = preview.steps[index];
                    var lease = step.LeaseTicks == 0 || step.Slot == VanillaCapacity
                        ? default : new WorldItemDropReservation(step.Slot, new(previous[step.Slot].Generation));
                    // A source lease prevents subsequent selection of this slot within the batch.
                    steps[index] = new(step.Start, step.End, step.Slot, lease, step.Transient);
                }
            }

            internal int Count => steps.Length;

            internal bool TryGetLease(int index, out WorldItemDropReservation lease)
            {
                lease = default;
                if ((uint)index >= (uint)steps.Length) return false;
                lease = steps[index].Lease;
                return true;
            }

            internal bool TryPublishNext(out short slot)
            {
                slot = -1;
                if (publishing || next == steps.Length) return false;
                // Consume the entire step before a reentrant sink can ask for another publication.
                var step = steps[next++];
                slot = step.Slot;
                publishing = true;
                try
                {
                    for (int index = step.Start; index < step.End; index++)
                    {
                        var operation = operations[index];
                        if (operation.Kind is { } kind && owner._slots[operation.Slot] == final[operation.Slot])
                            owner.Publish(kind, operation.Snapshot);
                    }
                    if (step.Transient is { } transient && owner._commitSink is IWorldItemSentinelCommitSink1458 sink)
                        sink.WorldItemSentinelCommitted(in transient);
                }
                finally
                {
                    publishing = false;
                }
                return true;
            }

            private readonly record struct PublicationOperation(short Slot, WorldItemStateCommitKind? Kind, WorldItemSnapshot Snapshot);
            private readonly record struct PublicationStep(int Start, int End, short Slot, WorldItemDropReservation Lease, WorldItemSentinelCommit1458? Transient);
        }

        public bool TryProcessPendingTransfers()
        {
            if (disposed || claimed || failed || steps.Count == VanillaCapacity) return false;
            int start = operations.Count;
            if (!ProcessTransfers(false, out _)) return Fail();
            steps.Add(new(start, operations.Count, VanillaCapacity, 0, pending[..pendingCount].ToArray(), null));
            return true;
        }

        private bool ProcessTransfers(bool requestRelease, out int freed)
        {
            freed = VanillaCapacity;
                for (int index = 0; index < pendingCount; index++)
                {
                    var transfer = pending[index];
                    if (working[transfer.Source].Generation != transfer.SourceGeneration ||
                        working[transfer.Destination].Generation != transfer.DestinationGeneration) continue;
                    int target = transfer.Destination;
                    for (int previous = index - 1; !working[target].Active && previous >= 0; previous--)
                        if (pending[previous].Source == target) target = pending[previous].Destination;
                    transfer = transfer with { Destination = (short)target, DestinationGeneration = working[target].Generation };
                    pending[index] = transfer;
                    ref var source = ref working[transfer.Source]; ref var destination = ref working[target];
                    if (CanTransfer(transfer) && source.Update.OwnerPlayerId == byte.MaxValue && destination.Update.OwnerPlayerId == byte.MaxValue)
                    {
                        VanillaWorldItemAllocationCatalog1458.TryGet(destination.Update.ItemNetId, out var targetFacts);
                        int amount = Math.Min(source.Update.Stack, targetFacts.MaximumStack - destination.Update.Stack);
                        if (amount > 0)
                        {
                            source.Update = source.Update with { Stack = (short)(source.Update.Stack - amount) };
                            destination.Update = destination.Update with { Stack = (short)(destination.Update.Stack + amount) };
                            if (source.Update.Stack == 0) source.Active = false;
                            if (destination.Update.Stack == targetFacts.MaximumStack && destination.Update.ItemNetId is 71 or 72 or 73)
                                destination.Update = destination.Update with { ItemNetId = (short)(destination.Update.ItemNetId + 1), Stack = 1, Prefix = 0 };
                            if (!RecordSync((short)target, WorldItemStateCommitKind.Drop) ||
                                !RecordSync(transfer.Source, source.Active ? WorldItemStateCommitKind.Drop : WorldItemStateCommitKind.Remove)) return Fail();
                        }
                    }
                    if (!source.Active || source.Update.Stack <= 0) freed = Math.Min(freed, transfer.Source);
                }
                int retained = 0;
                for (int i = 0; i < pendingCount; i++)
                    if (CanTransfer(pending[i])) pending[retained++] = pending[i];
                pendingCount = retained;
                if (requestRelease)
                    for (int index = 0; index < retained; index++)
                        if (!RequestRelease(pending[index].Source) || !RequestRelease(pending[index].Destination)) return false;
            return true;
        }

        private bool RequestRelease(short slot)
        {
            ref var state = ref working[slot];
            if (!state.Active || state.Update.OwnerPlayerId == byte.MaxValue) return true;
            state.Update = state.Update with { TimeToKeepReservation = 0 };
            bool activeOwner = false;
            foreach (var player in players) if (player.Slot == state.Update.OwnerPlayerId) { activeOwner = true; break; }
            if (activeOwner)
            {
                if (state.SourceReleaseRequested) return true;
                state.SourceReleaseRequested = true;
                state.SourceOwnerAge = -1;
                return RecordSync(slot, WorldItemStateCommitKind.OwnershipReleaseRequested);
            }
            state.Update = state.Update with { OwnerPlayerId = byte.MaxValue };
            state.SourceReleaseRequested = false;
            state.SourceOwnerAge = 0;
            return RecordSync(slot, WorldItemStateCommitKind.Owner);
        }

        private bool CanTransfer(in WorldItemStackTransfer1458 transfer)
        {
            var source = working[transfer.Source]; var target = working[transfer.Destination];
            return source.Generation == transfer.SourceGeneration && target.Generation == transfer.DestinationGeneration && source.Active && target.Active &&
                source.Update.ItemNetId == target.Update.ItemNetId && source.Update.Prefix == target.Update.Prefix &&
                VanillaWorldItemAllocationCatalog1458.TryGet(target.Update.ItemNetId, out var facts) && source.Update.Stack > 0 && target.Update.Stack < facts.MaximumStack;
        }
        private void CaptureItems(Span<WorldItemAllocationState1458> items)
        {
            for (int i = 0; i < working.Length; i++)
            {
                var state = working[i]; var update = state.Update;
                items[i] = new(state.Active, state.Claimed || (state.Reserved && !state.SourceLease), state.Active ? update.ItemNetId : 0,
                    state.Active ? update.Stack : 0, update.Prefix, update.PositionX, update.PositionY, state.SourceAge,
                    state.SourceReuseTicks, update.OwnerPlayerId, update.ShimmerTime, state.Generation);
            }
        }
        private bool RecordSync(short slot, WorldItemStateCommitKind kind)
        {
            if (working[slot].Revision == ulong.MaxValue) return false;
            working[slot].Revision++; return Record(slot, kind);
        }
        private bool Record(short slot, WorldItemStateCommitKind? kind)
        {
            if (operations.Count == MaximumOperations) return false;
            touched[slot] = true; operations.Add(new(slot, working[slot], kind)); return true;
        }
        private bool Fail() { failed = true; return false; }
        public void Dispose()
        {
            if (disposed) return;
            if (claimed)
            {
                owner.BeginWrite();
                try { for (int i = 0; i < touched.Length; i++) if (touched[i]) owner._slots[i].Claimed = false; }
                finally { owner.EndWrite(); }
            }
            disposed = true;
        }
        private readonly record struct Operation(short Slot, SlotState After, WorldItemStateCommitKind? Kind);
        private readonly record struct Step(int Start, int End, short Slot, int LeaseTicks, WorldItemStackTransfer1458[] Pending, WorldItemSentinelCommit1458? Transient);
    }
}
