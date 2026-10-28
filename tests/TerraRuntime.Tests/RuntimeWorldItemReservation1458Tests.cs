using System.Reflection;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemReservation1458Tests
{
    [Fact]
    public void Shipping_native_smoke_executes_both_allocation_publication_paths_and_source_owner_offers() =>
        Assert.True(WorldItemReservationNativeSmoke1458.Run());

    [Fact]
    public void ReserveLocal_live_offer_and_join_defaults_match_independently_captured_original_bytes()
    {
        using var f = new Fixture();
        var drop = new WorldItemDropStateUpdate(640, 640, -0.5f, -2.5f, 1, 0,
            WorldItemOwnershipMode.ReserveForLocalPlayer, 530, false, 0, 100);
        Assert.True(f.Items.TryAllocateSourceDrop(drop, [new(0, 640, 640, 20, 42)], out var item,
            out short selected, sourceLocalPlayerId: 0));
        Assert.Equal(0, selected);
        Assert.Equal(0, item.TimeToKeepReservation);
        // Original Linux-edition 1458 server executed under Windows CoreCLR, seed1458,
        // NewItem530 at640,640, ReserveLocal; live SendData21/22 then join index-only21/22.
        Assert.Equal(Convert.FromHexString("1C001500000000204400002044000000BF000020C001000009120264"), Read(f.Outbound));
        Assert.Equal(Convert.FromHexString("1100160000006400000000204400002044"), Read(f.Outbound));
        Assert.Equal(WorldItemBootstrapPacketEncodeResult.Encoded,
            WorldItemBootstrapPacketEncoder.TryEncode([item], out var baseline));
        Assert.Equal(Convert.FromHexString("1C001500000000204400002044000000BF000020C001000008120264"), baseline[0].ToArray());
        Assert.Equal(Convert.FromHexString("1100160000000000000000204400002044"), baseline[1].ToArray());
        Assert.Equal(0, f.Outbound.QueuedFrames);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(137, 100)]
    public void Offered_hold_is_transient_and_never_changes_raw_server_hold_or_late_join(int rawHold, int offer)
    {
        using var f = new Fixture();
        var before = f.AddItem(0, rawHold);
        var owner = new WorldItemOwnerStateUpdate(0, offer, 0, 0, 1000, 1000);
        Assert.True(f.Items.TryApplySourceOwner(before, owner, false, out var after));
        Assert.Equal(rawHold, after.TimeToKeepReservation);
        Assert.Equal(offer, ItemOwnerView.FromPayload(Read(f.Outbound).AsSpan(3)).TimeToKeepReservation);
        Assert.Equal(WorldItemBootstrapPacketEncodeResult.Encoded,
            WorldItemBootstrapPacketEncoder.TryEncode([after], out var baseline));
        Assert.Equal(0, ItemOwnerView.FromPayload(baseline[1].Span[3..]).TimeToKeepReservation);
        Assert.True(f.Items.TryGetSourceOwnerMetadata(after.Handle, out int age, out bool pending));
        Assert.Equal(0, age);
        Assert.False(pending);
    }

    [Fact]
    public void Discovery_then_disconnected_owner_offer15_keeps_server_clock_running()
    {
        using var f = new Fixture();
        f.AddItem(byte.MaxValue, 0);
        f.Items.TickReservationTimers();
        f.Graph.WorldItems.TickPlayerReservations(1);
        Assert.True(f.Items.TryGetActive(0, out var owned));
        Assert.Equal(0, owned.OwnerPlayerId);
        Assert.Equal(0, owned.TimeToKeepReservation);
        Assert.Equal(15, ItemOwnerView.FromPayload(Read(f.Outbound).AsSpan(3)).TimeToKeepReservation);
        f.Items.TickReservationTimers();
        Assert.True(f.Items.TryGetSourceOwnerMetadata(owned.Handle, out int age, out _));
        Assert.Equal(1, age);
        f.RegisterObserver();
        f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        f.Graph.WorldItems.TickPlayerReservations(2);
        Assert.True(f.Items.TryGetActive(0, out var released));
        Assert.Equal(byte.MaxValue, released.OwnerPlayerId);
        Assert.Equal(0, released.TimeToKeepReservation);
        var packet = Read(f.Observer);
        Assert.Equal(byte.MaxValue, ItemOwnerView.FromPayload(packet.AsSpan(3)).PlayerId);
        Assert.Equal(15, ItemOwnerView.FromPayload(packet.AsSpan(3)).TimeToKeepReservation);
    }

    [Fact]
    public void Real_tick_checks_pending_transfer_before_merge_and_waits_for_next_source_owner_cadence()
    {
        using var f = new Fixture();
        f.RegisterObserver();
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection,
            new(new(0), 0, 0, 0, 0, 0, 640, 640, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        var update = new WorldItemStateUpdate(640, 640, 0, 0, 10, 0,
            WorldItemOwnershipMode.None, 2, false, 0, 0, byte.MaxValue, 0, 0, 0);
        Assert.True(f.Items.TryUpsert(0, update, out var source));
        Assert.True(f.Items.TryUpsert(1, update, out var destination));
        // Fixture-only setup mirrors the original server's EmergencyStacking.PendingTransfers
        // scene: both released endpoints are unowned, with owner ages0 before UpdateServer.
        var transfers = Assert.IsType<WorldItemStackTransfer1458[]>(typeof(RuntimeWorldItemStore)
            .GetField("sourceTransfers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Items));
        transfers[0] = new(0, 1, source.Handle.Generation.Value, destination.Handle.Generation.Value, 0, 0, 0);
        typeof(RuntimeWorldItemStore).GetField("sourceTransferCount", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(f.Items, 1);
        Assert.True(f.Items.HasPendingSourceTransfer(source.Handle));
        Drain(f.Outbound); Drain(f.Observer);

        f.State.Tick();

        Assert.False(f.Items.TryGetActive(0, out _));
        Assert.True(f.Items.TryGetActive(1, out var merged));
        Assert.Equal(20, merged.Stack);
        Assert.Equal(byte.MaxValue, merged.OwnerPlayerId);
        Assert.Equal(0, merged.TimeToKeepReservation);
        Assert.False(f.Items.HasPendingSourceTransfer(merged.Handle));
        Assert.True(f.Items.TryGetSourceOwnerMetadata(merged.Handle, out int age, out bool pending));
        Assert.Equal(1, age);
        Assert.False(pending);
        // Original Main.UpdateServer emits only destination21 then source151 in this tick;
        // moving ProcessPendingTransfers before FindOwner adds an incorrect owner22 here.
        byte[] drop = Read(f.Outbound), removal = Read(f.Outbound);
        Assert.Equal(21, drop[2]);
        Assert.Equal(151, removal[2]);
        Assert.Equal(drop, Read(f.Observer));
        Assert.Equal(removal, Read(f.Observer));
        Assert.Equal(0, f.Outbound.QueuedFrames);
        Assert.Equal(0, f.Observer.QueuedFrames);

        for (int tick = 2; tick <= 5; tick++)
        {
            f.State.Tick();
            Assert.True(f.Items.TryGetActive(1, out var waiting));
            Assert.Equal(byte.MaxValue, waiting.OwnerPlayerId);
            Assert.Equal(0, f.Outbound.QueuedFrames);
            Assert.Equal(0, f.Observer.QueuedFrames);
        }
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(1, out var owned));
        Assert.Equal(0, owned.OwnerPlayerId);
        Assert.Equal(0, owned.TimeToKeepReservation);
        byte[] owner = Read(f.Outbound);
        Assert.Equal(22, owner[2]);
        Assert.Equal(15, ItemOwnerView.FromPayload(owner.AsSpan(3)).TimeToKeepReservation);
        Assert.Equal(owner, Read(f.Observer));
        Assert.Equal(0, f.Outbound.QueuedFrames);
        Assert.Equal(0, f.Observer.QueuedFrames);
    }

    [Theory]
    [InlineData(0, false, false, false, 0, 0, 15)]
    [InlineData(37, false, false, false, 255, 37, 0)]
    [InlineData(37, true, false, false, 255, 0, 0)]
    [InlineData(0, false, true, false, 255, 0, 0)]
    [InlineData(0, false, false, true, 255, 0, 0)]
    public void Authenticated39_keeps_raw_hold_and_excludes_sender_only_for_default_unowned_reply(
        int hold, bool force, bool shimmer, bool delayed, int expectedOwner, int expectedHold, int expectedOffer)
    {
        using var f = new Fixture();
        f.RegisterObserver();
        var item = f.AddItem(0, hold, shimmer, delayed);
        Assert.True(f.Items.TryRequestSourceOwnerRelease(item.Handle));
        Assert.Equal(39, Read(f.Outbound)[2]);
        f.State.Apply(new WorldItemReleaseRuntimeCommand(f.Connection, item.Handle, force));
        Assert.True(f.Items.TryGetActive(0, out var after));
        Assert.Equal(expectedOwner, after.OwnerPlayerId);
        Assert.Equal(expectedHold, after.TimeToKeepReservation);
        Assert.True(f.Items.TryGetSourceOwnerMetadata(item.Handle, out int age, out bool pending));
        Assert.Equal(0, age);
        Assert.False(pending);
        var packet = Read(f.Observer);
        Assert.Equal(expectedOwner, ItemOwnerView.FromPayload(packet.AsSpan(3)).PlayerId);
        Assert.Equal(expectedOffer, ItemOwnerView.FromPayload(packet.AsSpan(3)).TimeToKeepReservation);
        if (expectedOwner == byte.MaxValue) Assert.Equal(0, f.Outbound.QueuedFrames);
        else Assert.Equal(packet, Read(f.Outbound));
        Assert.Equal(0, f.Observer.QueuedFrames);
        // Exclusion belongs only to this event, not the endpoint or retained snapshot.
        var offer = new WorldItemOwnerStateUpdate(0, 15, 0, 0, after.PositionX, after.PositionY);
        Assert.True(f.Items.TryApplySourceOwner(after, offer, false, out _));
        Assert.Equal(Read(f.Observer), Read(f.Outbound));
    }

    [Fact]
    public void Release_command_cannot_mutate_replacement_item_generation()
    {
        using var f = new Fixture();
        var old = f.AddItem(0, 37);
        Assert.True(f.Items.TryRemove(0, out _));
        var replacement = f.AddItem(0, 19);
        Assert.NotEqual(old.Handle, replacement.Handle);
        f.State.Apply(new WorldItemReleaseRuntimeCommand(f.Connection, old.Handle, true));
        Assert.True(f.Items.TryGetActive(0, out var after));
        Assert.Equal(replacement, after);
        Assert.Equal(0, f.Outbound.QueuedFrames);
        Assert.Equal(1, f.Graph.WorldItems.RejectedOwners);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeWorldItemReplicationRegistry Registry = new();
        internal readonly RuntimeWorldItemStore Items;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaConnectionOutboundQueue Outbound = new(new OutboundQueueOptions(32, 16384, 1024));
        internal readonly TerrariaConnectionOutboundQueue Observer = new(new OutboundQueueOptions(32, 16384, 1024));
        private readonly PlayerSlotPool slots = new(2);
        private readonly PlayerJoinSession session;

        internal Fixture()
        {
            Items = new(Registry);
            State = new(worldItems: Items, worldTiles: new WorldTileStore(new WorldDimensions(128, 128)),
                worldItemReplication: Registry, townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
            Graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State));
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(7201), session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 62, 62, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection,
                new(session.Slot, 0, 0, 0, 0, 0, 1000, 1000, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.True(Registry.TryRegister(Connection.Source, Outbound));
            Registry.PlayerSpawned(Connection, new(session.Slot, 62, 62, 0, 0, 0, 0, 0));
        }

        internal void RegisterObserver()
        {
            // A network observer without a simulated actor cannot affect FindOwner selection.
            var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(7202),
                new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
            Assert.True(Registry.TryRegister(observer.Source, Observer));
            Registry.PlayerSpawned(observer, new(new(1), 62, 62, 0, 0, 0, 0, 0));
        }

        internal WorldItemSnapshot AddItem(byte owner, int hold, bool shimmer = false, bool delayed = false)
        {
            Assert.True(Items.TryUpsert(0, new(1000, 1000, 0, 0, 1, 0, WorldItemOwnershipMode.None,
                2, shimmer, shimmer ? 1 : 0, 0, owner, hold, delayed ? byte.MaxValue : (byte)0,
                delayed ? 100 : 0), out var item));
            Drain(Outbound); Drain(Observer);
            return item;
        }

        public void Dispose() => session.Dispose();
    }

    private static BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue outbound) =>
        Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
    private static byte[] Read(TerrariaConnectionOutboundQueue outbound)
    {
        Assert.True(Queue(outbound).TryRead(out var frame));
        return frame.Bytes.ToArray();
    }
    private static void Drain(TerrariaConnectionOutboundQueue outbound)
    {
        while (Queue(outbound).TryRead(out _)) { }
    }
}
