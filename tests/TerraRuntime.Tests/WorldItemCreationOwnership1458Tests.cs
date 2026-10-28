using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using PickupFixture = TerraRuntime.Tests.OwnedHumanPickupAuthority1458Tests.Fixture;

namespace TerraRuntime.Tests;

public sealed class WorldItemCreationOwnership1458Tests
{
    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)]
    public void Creation_mode_is_an_event_and_later_source_sync_does_not_reapply_it(byte mode, bool deferred)
    {
        var registry = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(registry);
        using var f = new PickupFixture(providedItems: items);
        var outbound = new TerrariaConnectionOutboundQueue(new(32, 16384, 1024));
        Assert.True(registry.TryRegister(f.Connection.Source, outbound));
        registry.PlayerSpawned(f.Connection, new(f.Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
        var drop = new WorldItemDropStateUpdate(1600, 1606, 0, 0, 20, 0, (WorldItemOwnershipMode)mode,
            110, false, 0, mode is 1 or 2 ? (byte)100 : (byte)0);
        using (var plan = items.CreateAllocationPreview([new(0, 1600, 1606, 20, 42)]))
        {
            Assert.True(plan.TrySpawnSource(drop, 0, out short slot, sourceLocalPlayerId: 0));
            Assert.Equal(0, slot); Assert.True(plan.TryClaim());
            if (deferred)
            {
                Assert.True(plan.TryAdoptUnpublished(out var publication));
                Assert.NotNull(publication); Assert.True(publication!.TryPublishNext(out _));
            }
            else Assert.True(plan.TryCommitNext(out _, out _));
        }
        var frames = Drain(outbound);
        var born = Assert.Single(frames, frame => frame[2] == 21);
        Assert.Equal(mode, (byte)WorldItemSyncView.FromPayload(PacketTypes.ItemDrop, born.AsSpan(3)).Ownership);
        Assert.True(items.TryGetActive(0, out var before));
        // Actual original WorldItem has no retained NewItemOwnership field.
        // SyncItem sends index-only21: the creation number2 cannot be replayed.
        Assert.Equal(WorldItemOwnershipMode.None, before.Ownership);
        Assert.Equal(mode is 2 or 3 ? 100 : 0, before.GrabDelayTime);
        Assert.Equal(mode == 3 ? byte.MaxValue : (byte)0, before.GrabDelayPlayer);
        Assert.Equal(0, before.TimeToKeepReservation);
        using (var take = items.CreateAllocationPreview())
        {
            Assert.True(take.TryTakeSource(before, 1, requireFundedQuantity: true));
            Assert.True(take.TryClaim());
            if (deferred)
            {
                Assert.True(take.TryAdoptUnpublished(out var publication));
                Assert.NotNull(publication); Assert.True(publication!.TryPublishNext(out _));
            }
            else Assert.True(take.TryCommitNext(out _, out _));
        }
        var later = Assert.Single(Drain(outbound));
        Assert.Equal(21, later[2]);
        var view = WorldItemSyncView.FromPayload(PacketTypes.ItemDrop, later.AsSpan(3));
        Assert.Equal(0, (byte)view.Ownership); Assert.Equal(19, view.Stack);
        Assert.True(items.TryGetActive(0, out var residual));
        Assert.Equal(before.Handle, residual.Handle);
        Assert.Equal(before.OwnerPlayerId, residual.OwnerPlayerId);
        Assert.Equal(before.GrabDelayPlayer, residual.GrabDelayPlayer);
        Assert.Equal(before.GrabDelayTime, residual.GrabDelayTime);
        Assert.Equal(before.TimeToKeepReservation, residual.TimeToKeepReservation);
        Assert.Equal(before.EnemyGrabDelayTime, residual.EnemyGrabDelayTime);
        Assert.True(items.TryGetFundedQuantity(residual, out int funded)); Assert.Equal(19, funded);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Vanilla_None_residual_credits_only_actual_contact_storage_and_creation_mode_cannot_replay(byte mode)
    {
        using var f = new PickupFixture();
        // Wire can use ammo storage too. Match the original full-census pickup
        // fixture rather than leaving an empty ammo endpoint for all seven.
        for (short slot = 1; slot < 58; slot++) f.Set(slot, 3, 9999);
        f.Set(0, 530, 9997);
        var drop = new WorldItemDropStateUpdate(1600, 1606, 0, 0, 7, 0, (WorldItemOwnershipMode)mode, 530, false, 0, 100);
        Assert.True(f.Items.TryAllocateSourceDrop(drop, [new(0, 1600, 1606, 20, 42)], out var item, out _, sourceLocalPlayerId: 0));
        // Trusted current owner/contact preimage; no claim that local grab delay
        // is bypassed by ordinary client ingress or that its 100ticks elapsed.
        Assert.True(f.Items.TryApplyOwner(item.Handle.Slot, new(0, 0, 1, 0, 1600, 1606), out item));
        var vanilla = new WorldItemDropStateUpdate(item.PositionX, item.PositionY, item.VelocityX, item.VelocityY,
            5, item.Prefix, WorldItemOwnershipMode.None, item.ItemNetId, item.Shimmered, item.ShimmerTime, item.EnemyGrabDelayTime);
        long applied = f.Runtime.WorldItems.AppliedDrops;
        f.State.Apply(new WorldItemDropRuntimeCommand(f.Connection, item.Handle, vanilla with { Ownership = (WorldItemOwnershipMode)mode }));
        Assert.Equal(applied, f.Runtime.WorldItems.AppliedDrops);
        Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var refused)); Assert.Equal(item, refused);
        f.State.Apply(new WorldItemDropRuntimeCommand(f.Connection, item.Handle, vanilla));
        Assert.True(applied + 1 == f.Runtime.WorldItems.AppliedDrops,
            $"mode={mode} known={f.Players.HasOwnedPickupContext} phase={f.Member.ItemPhase} body={f.Member.PositionX},{f.Member.PositionY} " +
            $"ground={item} slots={string.Join(';', f.ItemImage().Take(4))}");
        Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var received)); Assert.Equal(9999, received.Stack);
        Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var residual)); Assert.Equal(5, residual.Stack);
        Assert.Equal(item.Handle, residual.Handle); Assert.Equal(0, residual.OwnerPlayerId);
        Assert.True(f.Items.TryGetFundedQuantity(residual, out int funded)); Assert.Equal(5, funded);
        var inventory = Assert.IsType<RuntimePlayerInventoryStore>(typeof(PlayerAuthority).GetField("inventory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.Players));
        Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out short owned)); Assert.Equal(2, owned);
    }

    [Fact]
    public void Both_reserved_drop_routes_and_direct_drop_keep_creation_intent_out_of_later_state()
    {
        foreach (int route in new[] { 0, 1, 2 })
        foreach (byte mode in new byte[] { 0, 1, 2, 3 })
        {
            var sink = new CaptureSink();
            var items = new RuntimeWorldItemStore(sink);
            var drop = new WorldItemDropStateUpdate(640, 640, -.5f, -2.5f, 7, 0,
                (WorldItemOwnershipMode)mode, 530, false, 0, 100);
            WorldItemSnapshot item;
            if (route == 0) Assert.True(items.TryAllocateDrop(drop, out item));
            else if (route == 1)
            {
                Assert.True(items.TryReserveDrop(drop, out var lease));
                Assert.Equal(0, items.ActiveCount); Assert.Empty(sink.Drops);
                Assert.True(items.TryCommitReservedDrop(lease, out item));
            }
            else
            {
                Assert.True(items.TryReserveDropSlot(out var lease));
                Assert.True(items.TryCommitReservedDrop(lease, drop, out item));
            }
            Assert.Equal((WorldItemOwnershipMode)mode, Assert.Single(sink.Drops).Ownership);
            Assert.Equal(WorldItemOwnershipMode.None, item.Ownership);
            Assert.True(items.TryGetActive(item.Handle.Slot, out var active)); Assert.Equal(item, active);
            // Existing-slot source21 ignores spawn ownership even if an
            // obsolete caller repeats a creation argument in its update.
            Assert.True(items.TryApplyDrop(item.Handle.Slot, drop with { Stack = 5 }, out var residual));
            Assert.Equal(2, sink.Drops.Count);
            Assert.Equal(WorldItemOwnershipMode.None, sink.Drops[1].Ownership);
            Assert.Equal(WorldItemOwnershipMode.None, residual.Ownership);
            Assert.Equal(item.Handle, residual.Handle); Assert.Equal(5, residual.Stack);
            Assert.Equal(item.OwnerPlayerId, residual.OwnerPlayerId);
            Assert.Equal(item.TimeToKeepReservation, residual.TimeToKeepReservation);
            if (mode == 1)
            {
                // Actual unmodified original NewItem530 and private PickupItem:
                // world7->5, inventory9997->9999, repeated SyncItemNone.
                // The original owner22 is separate from this literal21 body.
                var birthWire = RuntimeWorldItemReplicationRegistry.MapDrop(sink.Drops[0]);
                var laterWire = RuntimeWorldItemReplicationRegistry.MapDrop(sink.Drops[1]);
                Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,
                    TerrariaWorldItemFrameEncoder.TryEncodeDrop(birthWire, out var first));
                Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,
                    TerrariaWorldItemFrameEncoder.TryEncodeDrop(laterWire, out var later));
                Assert.Equal(Convert.FromHexString("1C001500000000204400002044000000BF000020C007000009120264"), first.ToArray());
                Assert.Equal(Convert.FromHexString("1C001500000000204400002044000000BF000020C005000008120264"), later.ToArray());
            }
        }
    }

    [Fact]
    public void Pending_merge_normalizes_both_legacy_creation_hints_without_creating_funding()
    {
        foreach (byte firstMode in new byte[] { 1, 2 })
        foreach (byte secondMode in new byte[] { 1, 2 })
        {
            var sink = new CaptureSink(); var items = new RuntimeWorldItemStore(sink);
            var input = new WorldItemStateUpdate(640, 640, -.5f, -2.5f, 4, 0,
                (WorldItemOwnershipMode)firstMode, 2, false, 0, 100, 255, 0, 0, 0);
            Assert.True(items.TryUpsert(0, input, out var source));
            Assert.True(items.TryUpsert(1, input with { Stack = 3, Ownership = (WorldItemOwnershipMode)secondMode }, out var destination));
            var transfers = Assert.IsType<TerraRuntime.Gameplay.Items.WorldItemStackTransfer1458[]>(typeof(RuntimeWorldItemStore)
                .GetField("sourceTransfers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(items));
            transfers[0] = new(0, 1, source.Handle.Generation.Value, destination.Handle.Generation.Value, 0, 0, 0);
            typeof(RuntimeWorldItemStore).GetField("sourceTransferCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(items, 1);
            sink.Drops.Clear();
            Assert.True(items.TryProcessPendingSourceTransfers());
            Assert.False(items.TryGetActive(0, out _));
            Assert.True(items.TryGetActive(1, out var merged)); Assert.Equal(7, merged.Stack);
            Assert.Equal(destination.Handle, merged.Handle); Assert.Equal(WorldItemOwnershipMode.None, merged.Ownership);
            Assert.Equal(WorldItemOwnershipMode.None, Assert.Single(sink.Drops).Ownership);
            Assert.Equal(destination.EnemyGrabDelayTime, merged.EnemyGrabDelayTime);
            Assert.True(items.TryGetFundedQuantity(merged, out int funded)); Assert.Equal(0, funded);
        }
    }

    [Fact]
    public void Unpublished_source_leases_do_not_run_ApplySpawnOwnership()
    {
        foreach (byte mode in new byte[] { 0, 1, 2, 3 })
        {
            var sink = new CaptureSink(); var items = new RuntimeWorldItemStore(sink);
            using var preview = items.CreateAllocationPreview();
            var drop = new WorldItemDropStateUpdate(640, 640, -.5f, -2.5f, 1, 0,
                (WorldItemOwnershipMode)mode, 530, false, 0, mode is 1 or 2 ? (byte)100 : (byte)0);
            Assert.True(preview.TrySpawnSource(drop, 30, out short slot)); Assert.True(preview.TryClaim());
            Assert.True(preview.TryCommitNext(out _, out var lease));
            Assert.True(lease.IsAssigned); Assert.False(items.TryGetActive(slot, out _)); Assert.Empty(sink.Drops);
            // Read the inactive reserved cell without activating a physical
            // source item or inventing an ordinary post-lease publication.
            var states = Assert.IsAssignableFrom<Array>(typeof(RuntimeWorldItemStore)
                .GetField("_slots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(items));
            var retained = states.GetValue(slot)!;
            var update = Assert.IsType<WorldItemStateUpdate>(retained.GetType().GetField("Update")!.GetValue(retained));
            Assert.Equal(0, update.GrabDelayTime); Assert.Equal(0, update.GrabDelayPlayer);
            Assert.Equal(byte.MaxValue, update.OwnerPlayerId); Assert.Equal(0, update.TimeToKeepReservation);
            Assert.Equal(drop.EnemyGrabDelayTime, update.EnemyGrabDelayTime);
            Assert.Equal(WorldItemOwnershipMode.None, update.Ownership);
        }
    }

    private sealed class CaptureSink : IWorldItemStateCommitSink
    {
        internal readonly List<WorldItemSnapshot> Drops = [];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot,
            int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind == WorldItemStateCommitKind.Drop) Drops.Add(snapshot);
        }
    }

    private static List<byte[]> Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(outbound));
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames;
    }
}
