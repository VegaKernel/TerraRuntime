using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class WorldItemReservationClock1458Tests
{
    // WorldItem.ReserveFor sends its time argument in packet22 without assigning the
    // server field. ApplySpawnOwnership supplies100; FindOwner uses ReserveFor's15.
    [Theory]
    [InlineData(false, false, 15)]
    [InlineData(false, true, 15)]
    [InlineData(true, false, 100)]
    [InlineData(true, true, 100)]
    public void Source_allocation_offer_is_transient_in_both_publication_paths(bool reserveLocal, bool deferred, int expectedOffer)
    {
        var sink = new Sink();
        var store = new RuntimeWorldItemStore(sink);
        store.AttachOwnerFactsProvider(new OwnerFacts());
        var drop = Drop(reserveLocal ? WorldItemOwnershipMode.ReserveForLocalPlayer : WorldItemOwnershipMode.None);
        using var plan = store.CreateAllocationPreview();
        Assert.True(plan.TrySpawnSource(drop, 0, out short slot, sourceLocalPlayerId: 7));
        Assert.True(plan.TryClaim());
        if (deferred)
        {
            Assert.True(plan.TryAdoptUnpublished(out var publication));
            Assert.Empty(sink.Events);
            Assert.True(store.TryGetActive(slot, out var adopted));
            Assert.Equal(7, adopted.OwnerPlayerId);
            Assert.Equal(0, adopted.TimeToKeepReservation);
            Assert.NotNull(publication);
            Assert.True(publication!.TryPublishNext(out short published));
            Assert.Equal(slot, published);
            Assert.False(publication.TryPublishNext(out _));
        }
        else
        {
            Assert.True(plan.TryCommitNext(out short committed, out _));
            Assert.Equal(slot, committed);
        }

        Assert.Equal(new[] { WorldItemStateCommitKind.Drop, WorldItemStateCommitKind.Owner }, sink.Events.Select(x => x.Kind));
        Assert.Null(sink.Events[0].Offer);
        var owner = sink.Events[1];
        Assert.Equal(expectedOffer, owner.Offer);
        Assert.Equal(0, owner.Snapshot.TimeToKeepReservation);
        Assert.True(store.TryGetActive(slot, out var current));
        Assert.Equal(7, current.OwnerPlayerId);
        Assert.Equal(0, current.TimeToKeepReservation);
        plan.Dispose();
        store.TickReservationTimers();
        Assert.True(store.TryGetActive(slot, out var ticked));
        Assert.Equal(0, ticked.TimeToKeepReservation);
        Assert.Equal(2, sink.Events.Count);
    }

    [Fact]
    public void Source_offer_preserves_real_server_countdown_while_force_assignment_clears_it()
    {
        var sink = new Sink();
        var store = new RuntimeWorldItemStore(sink);
        Assert.True(store.TryAllocateDrop(Drop(), out var initial));
        var trusted = new WorldItemOwnerStateUpdate(7, 7, 255, 0, initial.PositionX, initial.PositionY);
        Assert.True(store.TryApplyOwner(initial.Handle.Slot, trusted, out var seeded));
        sink.Events.Clear();

        var offer = trusted with { OwnerPlayerId = 8, TimeToKeepReservation = 100 };
        Assert.True(store.TryApplySourceOwner(seeded, offer, clearServerReservation: false, out var assigned));
        Assert.Equal(7, assigned.TimeToKeepReservation);
        Assert.Equal(8, assigned.OwnerPlayerId);
        var offered = Assert.Single(sink.Events);
        Assert.Equal(100, offered.Offer);
        Assert.Equal(7, offered.Snapshot.TimeToKeepReservation);
        store.TickReservationTimers();
        Assert.True(store.TryGetActive(initial.Handle.Slot, out var ticked));
        Assert.Equal(6, ticked.TimeToKeepReservation);
        Assert.Single(sink.Events);

        Assert.True(store.TryRequestSourceOwnerRelease(ticked.Handle));
        Assert.True(store.TryGetSourceOwnerMetadata(ticked.Handle, out int age, out bool pending));
        Assert.Equal(-1, age);
        Assert.True(pending);
        Assert.True(store.TryGetActive(initial.Handle.Slot, out var releaseRequested));
        var release = offer with { OwnerPlayerId = 255, TimeToKeepReservation = 15 };
        Assert.True(store.TryApplySourceOwner(releaseRequested, release, clearServerReservation: true, out var released));
        Assert.Equal(0, released.TimeToKeepReservation);
        Assert.Equal(255, released.OwnerPlayerId);
        Assert.Equal(15, sink.Events[^1].Offer);
        Assert.True(store.TryGetSourceOwnerMetadata(released.Handle, out age, out pending));
        Assert.Equal(0, age);
        Assert.False(pending);
    }

    [Fact]
    public void Source_owner_rejects_stale_revision_reused_generation_and_claimed_slot_without_publication()
    {
        var sink = new Sink();
        var store = new RuntimeWorldItemStore(sink);
        Assert.True(store.TryAllocateDrop(Drop(), out var original));
        var offer = new WorldItemOwnerStateUpdate(7, 15, 255, 0, original.PositionX, original.PositionY);
        Assert.True(store.TryAdvanceMotion(original.Handle, 20, 30, 1, 2, out var moved));
        sink.Events.Clear();
        Assert.False(store.TryApplySourceOwner(original, offer, false, out _));
        Assert.True(store.TryGetActive(original.Handle.Slot, out var unchanged));
        Assert.Equal(moved, unchanged);
        Assert.Empty(sink.Events);

        Assert.True(store.TryRemove(original.Handle.Slot, out _));
        Assert.True(store.TryAllocateDrop(Drop(), out var replacement));
        Assert.Equal(original.Handle.Slot, replacement.Handle.Slot);
        Assert.NotEqual(original.Handle.Generation, replacement.Handle.Generation);
        sink.Events.Clear();
        Assert.False(store.TryApplySourceOwner(moved, offer, true, out _));
        Assert.True(store.TryGetActive(replacement.Handle.Slot, out unchanged));
        Assert.Equal(replacement, unchanged);
        Assert.Empty(sink.Events);

        // A claim on an existing item (not a replacement allocation) must also guard
        // source-owner callbacks that reenter while another transaction owns it.
        using var claim = store.CreateAllocationPreview();
        Assert.True(claim.TryTakeSource(replacement, 1));
        Assert.True(claim.TryClaim());
        Assert.False(store.TryApplySourceOwner(replacement, offer, false, out _));
        Assert.True(store.TryGetActive(replacement.Handle.Slot, out unchanged));
        Assert.Equal(replacement, unchanged);
        Assert.Empty(sink.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emergency_force_release_clears_unowned_endpoint_clock_without_owner_event(bool deferred)
    {
        var sink = new Sink();
        var store = WorldItemSourceAllocation1458Tests.Arrange(17, sink);
        Assert.True(store.TryRemove(360, out _));
        Assert.True(store.TryGetActive(51, out var target));
        var trusted = new WorldItemOwnerStateUpdate(255, 37, 255, 0, target.PositionX, target.PositionY);
        Assert.True(store.TryApplyOwner(51, trusted, out var before));
        sink.Events.Clear();

        // Player7 still owns the other endpoint, so this pair remains pending.
        // Source forces both endpoints through FindOwner even though this one is255.
        var players = new[] { new WorldItemAllocationPlayer1458(7, 10000, 10000, 20, 42) };
        using var plan = store.CreateAllocationPreview(players);
        Assert.True(plan.TrySpawnSource(Drop(), 0, out short slot));
        Assert.Equal(360, slot);
        Assert.True(plan.TryClaim());
        if (deferred)
        {
            Assert.True(plan.TryAdoptUnpublished(out var publication));
            Assert.NotNull(publication);
            Assert.True(publication!.TryPublishNext(out _));
        }
        else
        {
            Assert.True(plan.TryCommitNext(out _, out _));
        }

        Assert.True(store.TryGetActive(51, out var cleared));
        Assert.Equal(255, cleared.OwnerPlayerId);
        Assert.Equal(0, cleared.TimeToKeepReservation);
        Assert.Equal(before.Revision.Value + 1, cleared.Revision.Value);
        Assert.Equal(before.Stack, cleared.Stack);
        Assert.True(store.HasPendingSourceTransfer(cleared.Handle));
        Assert.DoesNotContain(sink.Events, x => x.Snapshot.Handle.Slot == 51);
        Assert.DoesNotContain(sink.Events, x => x.Kind == WorldItemStateCommitKind.Owner);
        var release = Assert.Single(sink.Events, x => x.Kind == WorldItemStateCommitKind.OwnershipReleaseRequested);
        Assert.Equal(50, release.Snapshot.Handle.Slot);
    }

    [Fact]
    public void Ordinary_release_request_preserves_actual_server_hold()
    {
        var sink = new Sink();
        var store = new RuntimeWorldItemStore(sink);
        Assert.True(store.TryAllocateDrop(Drop(), out var initial));
        var owner = new WorldItemOwnerStateUpdate(7, 37, 255, 0, initial.PositionX, initial.PositionY);
        Assert.True(store.TryApplyOwner(initial.Handle.Slot, owner, out _));
        sink.Events.Clear();
        Assert.True(store.TryRequestSourceOwnerRelease(initial.Handle));
        Assert.True(store.TryGetActive(initial.Handle.Slot, out var pending));
        Assert.Equal(37, pending.TimeToKeepReservation);
        Assert.Equal(WorldItemStateCommitKind.OwnershipReleaseRequested, Assert.Single(sink.Events).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_emergency_force_release_adopts_clock_clear_without_resending_owner_messages(bool deferred)
    {
        var sink = new Sink();
        var store = WorldItemSourceAllocation1458Tests.Arrange(17, sink);
        Assert.True(store.TryRemove(360, out _));
        Assert.True(store.TryGetActive(50, out var source));
        var trusted = new WorldItemOwnerStateUpdate(7, 37, 255, 0, source.PositionX, source.PositionY);
        Assert.True(store.TryApplyOwner(50, trusted, out _));
        Assert.True(store.TryRequestSourceOwnerRelease(source.Handle));
        Assert.True(store.TryGetActive(50, out var pending));
        Assert.Equal(37, pending.TimeToKeepReservation);
        sink.Events.Clear();

        var players = new[] { new WorldItemAllocationPlayer1458(7, 10000, 10000, 20, 42) };
        using var plan = store.CreateAllocationPreview(players);
        Assert.True(plan.TrySpawnSource(Drop(), 0, out short slot));
        Assert.Equal(360, slot);
        Assert.True(plan.TryClaim());
        if (deferred)
        {
            Assert.True(plan.TryAdoptUnpublished(out var publication));
            Assert.NotNull(publication);
            Assert.True(publication!.TryPublishNext(out _));
        }
        else
        {
            Assert.True(plan.TryCommitNext(out _, out _));
        }

        Assert.True(store.TryGetActive(50, out var cleared));
        Assert.Equal(0, cleared.TimeToKeepReservation);
        Assert.Equal(pending.Revision.Value + 1, cleared.Revision.Value);
        Assert.Equal(7, cleared.OwnerPlayerId);
        Assert.True(store.TryGetSourceOwnerMetadata(cleared.Handle, out int age, out bool requested));
        Assert.True(requested);
        Assert.Equal(-1, age);
        Assert.DoesNotContain(sink.Events, x => x.Kind is WorldItemStateCommitKind.Owner or WorldItemStateCommitKind.OwnershipReleaseRequested);
        Assert.Equal(WorldItemStateCommitKind.Drop, Assert.Single(sink.Events).Kind);
    }

    [Fact]
    public void Emergency_force_release_clears_server_clock_and_offers_fifteen_to_unowned_item()
    {
        var sink = new Sink();
        var store = WorldItemSourceAllocation1458Tests.Arrange(17, sink);
        Assert.True(store.TryRemove(360, out _));
        Assert.True(store.TryGetActive(50, out var source));
        var trusted = new WorldItemOwnerStateUpdate(7, 8, 255, 0, source.PositionX, source.PositionY);
        Assert.True(store.TryApplyOwner(50, trusted, out _));
        sink.Events.Clear();

        // No active player7: source FindOwner(force:true) reaches ReserveFor(255),
        // whose default packet22 offer is15 even though the raw server field is0.
        using var plan = store.CreateAllocationPreview();
        Assert.True(plan.TrySpawnSource(Drop(), 0, out short slot));
        Assert.Equal(360, slot);
        Assert.True(plan.TryClaim());
        Assert.True(plan.TryCommitNext(out _, out _));
        var release = Assert.Single(sink.Events, x => x.Kind == WorldItemStateCommitKind.Owner && x.Snapshot.Handle.Slot == 50);
        Assert.Equal(255, release.Snapshot.OwnerPlayerId);
        Assert.Equal(0, release.Snapshot.TimeToKeepReservation);
        Assert.Equal(15, release.Offer);
        Assert.True(store.TryGetActive(50, out var released));
        Assert.Equal(0, released.TimeToKeepReservation);
        Assert.Equal(255, released.OwnerPlayerId);
    }

    [Fact]
    public void Source_recipient_exclusion_and_zero_offer_belong_only_to_that_publication()
    {
        var sink = new Sink();
        var store = new RuntimeWorldItemStore(sink);
        Assert.True(store.TryAllocateDrop(Drop(), out var initial));
        sink.Events.Clear();
        var offer = new WorldItemOwnerStateUpdate(255, 0, 255, 0, initial.PositionX, initial.PositionY);
        var source = GameCommandSourceId.FromConnection(17);
        Assert.True(store.TryApplySourceOwner(initial, offer, false, out var acknowledged, excludedSource: source));
        var acknowledgement = Assert.Single(sink.Events);
        Assert.Equal(0, acknowledgement.Offer);
        Assert.Equal(source, acknowledgement.Excluded);
        Assert.True(store.TryGetActive(initial.Handle.Slot, out var current));
        Assert.Equal(acknowledged, current);

        offer = offer with { OwnerPlayerId = 7, TimeToKeepReservation = 15 };
        Assert.True(store.TryApplySourceOwner(current, offer, false, out var assigned));
        Assert.Equal(15, sink.Events[^1].Offer);
        Assert.Equal(GameCommandSourceId.System, sink.Events[^1].Excluded);
        Assert.Equal(0, assigned.TimeToKeepReservation);
    }

    [Fact]
    public void Invalid_offer_does_not_clear_actual_hold_or_advance_revision()
    {
        var sink = new Sink();
        var store = new RuntimeWorldItemStore(sink);
        Assert.True(store.TryAllocateDrop(Drop(), out var initial));
        var owner = new WorldItemOwnerStateUpdate(7, 4, 255, 0, initial.PositionX, initial.PositionY);
        Assert.True(store.TryApplyOwner(initial.Handle.Slot, owner, out var before));
        sink.Events.Clear();
        Assert.False(store.TryApplySourceOwner(before, owner with { TimeToKeepReservation = -1 }, true, out _));
        Assert.False(store.TryApplySourceOwner(before, owner with { PositionX = float.NaN }, true, out _));
        Assert.True(store.TryGetActive(before.Handle.Slot, out var after));
        Assert.Equal(before, after);
        Assert.Empty(sink.Events);
    }

    private static WorldItemDropStateUpdate Drop(WorldItemOwnershipMode ownership = WorldItemOwnershipMode.None)
        => new(10, 20, 0, 0, 1, 0, ownership, 2, false, 0, 0);

    private sealed class OwnerFacts : IWorldItemOwnerFactsProvider1458, IWorldItemOwnerFactsSnapshot1458
    {
        public IWorldItemOwnerFactsSnapshot1458 Capture() => this;
        public bool IsCurrent => true;
        public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int grabDelay, byte grabPlayer, out byte owner)
        {
            owner = 7;
            return true;
        }
    }

    private sealed class Sink : IWorldItemStateCommitSink
    {
        public List<(WorldItemStateCommitKind Kind, WorldItemSnapshot Snapshot, int? Offer, GameCommandSourceId Excluded)> Events { get; } = [];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot,
            int? reservationOffer = null, GameCommandSourceId excludedSource = default)
            => Events.Add((kind, snapshot, reservationOffer, excludedSource));
    }
}
