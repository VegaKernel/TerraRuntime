using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TempleDoorInventoryAtomic1458Tests
{
    [Fact]
    public void First_inventory_publication_observes_the_complete_source_door_and_key_result()
    {
        // Source Player key search is ascending 0..57; WorldGen.UnlockDoor adds54 to all three rows.
        // This tests the runtime publication boundary, rather than claiming vanilla callback ordering.
        foreach (short stack in new short[] { 1, 2 })
        foreach (short touchedRow in new short[] { 0, 1, 2 })
        {
            using var f = new Fixture(stack);
            WorldTile[] before = f.CopyDoor();
            f.Observer.Callback = request =>
            {
                Assert.Equal((short)9, request.SlotId);
                Assert.Equal(stack == 1 ? (short)0 : (short)1, request.Stack);
                Assert.Equal(stack == 1 ? (short)0 : (short)1141, request.ItemNetId);
                f.AssertUnlocked(before);
                Assert.Equal(stack == 1 ? default : new RuntimePlayerInventoryItem(new(1141), 1, new(0), 1), f.Item(9));
                Assert.Equal((short)3, f.Item(17).Stack);
                Assert.Equal((short)9, f.Item(58).Stack);
            };

            f.Unlock(touchedRow);

            Assert.Equal(1, f.Observer.Publications);
            f.AssertUnlocked(before);
            Assert.Equal(1, f.State.AppliedClientTileManipulations);
            Assert.Equal(1, f.OwnerOutbound.QueuedFrames);
            Assert.Equal(2, f.PeerOutbound.QueuedFrames);
            f.Unlock(touchedRow);
            Assert.Equal(1, f.Observer.Publications);
            Assert.Equal(1, f.State.RejectedClientTileManipulations);
        }
    }

    [Fact]
    public void Publication_reentry_preserves_newer_inventory_world_and_membership_without_stale_frames()
    {
        foreach (string mutation in new[] { "world", "inventory", "disconnect" })
        {
            using var f = new Fixture(2);
            WorldTile[] before = f.CopyDoor();
            f.Observer.Callback = _ =>
            {
                f.AssertUnlocked(before);
                Assert.Equal((short)1, f.Item(9).Stack);
                f.Observer.Callback = null;
                if (mutation == "inventory") f.SetItem(9, 7);
                if (mutation == "world")
                {
                    for (int row = 0; row < 3; row++)
                    {
                        WorldTile changed = f.Tiles.Get(40, 50 + row);
                        changed.FrameY = (short)(900 + row * 18);
                        f.Tiles.Set(40, 50 + row, in changed);
                    }
                }
                if (mutation == "disconnect")
                    f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
            };

            f.Unlock(1);

            Assert.Equal(1, f.State.AppliedClientTileManipulations);
            if (mutation == "world")
            {
                for (int row = 0; row < 3; row++)
                    Assert.Equal((short)(900 + row * 18), f.Tiles.Get(40, 50 + row).FrameY);
            }
            else f.AssertUnlocked(before);
            Assert.Equal(0, f.OwnerOutbound.QueuedFrames);
            Assert.Equal(0, f.PeerOutbound.QueuedFrames);
            if (mutation == "disconnect")
                Assert.False(f.Players.TryGet(f.Connection, out _));
            else
                Assert.Equal(mutation == "inventory" ? (short)7 : (short)1, f.Item(9).Stack);
        }
    }

    [Fact]
    public void A_throwing_publication_cannot_leave_only_the_key_adopted()
    {
        using var f = new Fixture(1);
        WorldTile[] before = f.CopyDoor();
        f.Observer.Callback = _ => throw new ObserverFailure();

        Assert.Throws<ObserverFailure>(() => f.Unlock(2));

        f.AssertUnlocked(before);
        Assert.Equal(default, f.Item(9));
        Assert.Equal(0, f.OwnerOutbound.QueuedFrames);
        Assert.Equal(0, f.PeerOutbound.QueuedFrames);
    }

    [Fact]
    public void Captured_key_writes_refuse_stale_owners_and_adopt_publish_only_once()
    {
        foreach (string mutation in new[] { "item-aba", "other-slot", "input-only", "report", "pose", "phase", "disconnect" })
        {
            using var f = new Fixture(2);
            RuntimePlayerInventoryItem before = f.Item(9);
            RuntimePlayerInventoryItem after = before with { Stack = 1 };
            Assert.True(f.Players.TryPrepareInventoryMutation(f.Connection, 9, in before, in after, out var preparation));
            Assert.True(preparation!.IsCurrent);
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            switch (mutation)
            {
                case "item-aba":
                    f.SetItem(9, 3);
                    f.SetItem(9, 2);
                    break;
                case "other-slot":
                    // Isolate the whole-inventory stamp from member/input revisions.
                    var update = new PlayerEquipmentCommitRequest(f.Connection.Player.Slot, 17, 4, 0, 1141, 1);
                    Assert.True(f.Inventory.TrySet(f.Connection, in update));
                    break;
                case "input-only":
                    typeof(RuntimePlayerMember).GetProperty("ProjectileUseInputRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(member, member.ProjectileUseInputRevision + 1);
                    break;
                case "report":
                    f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 100, 100)));
                    break;
                case "pose":
                    member.PositionX += 1;
                    break;
                case "phase":
                    member.ItemPhase = null;
                    break;
                case "disconnect":
                    f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
                    break;
            }

            int publications = f.Observer.Publications;
            Assert.False(preparation.IsCurrent);
            Assert.False(preparation.TryAdoptUnpublished());
            Assert.False(preparation.TryPublish());
            Assert.Equal(publications, f.Observer.Publications);
            Assert.Equal((short)594, f.Tiles.Get(40, 50).FrameY);
            if (mutation != "disconnect") Assert.Equal(before, f.Item(9));
        }

        using var accepted = new Fixture(2);
        RuntimePlayerInventoryItem oldItem = accepted.Item(9);
        RuntimePlayerInventoryItem nextItem = oldItem with { Stack = 1 };
        RuntimePlayerInventoryItem malformed = oldItem with { Stack = 0 };
        Assert.False(accepted.Players.TryPrepareInventoryMutation(accepted.Connection, 9, in oldItem, in malformed, out _));
        Assert.True(accepted.Players.TryPrepareInventoryMutation(accepted.Connection, 9, in oldItem, in nextItem, out var token));
        Assert.True(token!.TryAdoptUnpublished());
        Assert.Equal(0, accepted.Observer.Publications);
        Assert.Equal(nextItem, accepted.Item(9));
        Assert.False(token.TryAdoptUnpublished());
        Assert.True(token.IsAcceptedCurrent);
        Assert.True(token.TryPublish());
        Assert.False(token.TryPublish());
        Assert.Equal(1, accepted.Observer.Publications);

        foreach (string saturated in new[] { "member", "input", "inventory", "inventory-last-serial" })
        {
            using var f = new Fixture(2);
            RuntimePlayerInventoryItem before = f.Item(9);
            RuntimePlayerInventoryItem after = before with { Stack = 1 };
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            if (saturated == "member") member.Revision = ulong.MaxValue;
            if (saturated == "input")
                typeof(RuntimePlayerMember).GetProperty("ProjectileUseInputRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(member, ulong.MaxValue);
            if (saturated.StartsWith("inventory", StringComparison.Ordinal))
            {
                ulong serial = saturated == "inventory" ? ulong.MaxValue : ulong.MaxValue - 1;
                typeof(RuntimePlayerInventoryStore).GetProperty("Serial", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Inventory, serial);
            }
            Assert.False(f.Players.TryPrepareInventoryMutation(f.Connection, 9, in before, in after, out _));
            f.Unlock(0);
            Assert.Equal(before, f.Item(9));
            Assert.Equal((short)594, f.Tiles.Get(40, 50).FrameY);
            Assert.Equal(0, f.Observer.Publications);
            Assert.Equal(0, f.OwnerOutbound.QueuedFrames);
            Assert.Equal(0, f.PeerOutbound.QueuedFrames);
        }

        using var nearLimit = new Fixture(2);
        typeof(RuntimePlayerInventoryStore).GetProperty("Serial", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(nearLimit.Inventory, ulong.MaxValue - 2);
        nearLimit.Observer.Callback = _ =>
        {
            var otherSlot = new PlayerEquipmentCommitRequest(nearLimit.Connection.Player.Slot, 17, 4, 0, 1141, 1);
            Assert.True(nearLimit.Inventory.TrySet(nearLimit.Connection, in otherSlot));
        };
        nearLimit.Unlock(1);
        Assert.Equal((short)1, nearLimit.Item(9).Stack);
        Assert.Equal((short)4, nearLimit.Item(17).Stack);
        Assert.Equal((short)648, nearLimit.Tiles.Get(40, 50).FrameY);
        Assert.Equal(0, nearLimit.OwnerOutbound.QueuedFrames);
        Assert.Equal(0, nearLimit.PeerOutbound.QueuedFrames);
    }

    private sealed class ObserverFailure : Exception;

    private sealed class Observer : IRuntimePlayerEventSink
    {
        internal Action<PlayerEquipmentCommitRequest>? Callback;
        internal int Publications;
        internal bool Armed;
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
            if (!Armed) return;
            Publications++;
            Callback?.Invoke(request);
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(100, 100));
        internal readonly Observer Observer = new();
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal RuntimePlayerInventoryStore Inventory => (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Players)!;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaConnectionOutboundQueue OwnerOutbound = Outbound();
        internal readonly TerrariaConnectionOutboundQueue PeerOutbound = Outbound();
        private readonly PlayerJoinSession session;

        internal Fixture(short stack)
        {
            for (int row = 0; row < 3; row++)
            {
                var tile = new WorldTile { Type = 10, FrameX = 0, FrameY = (short)(594 + row * 18), Flags = WorldTileFlags.Active };
                Tiles.Set(40, 50 + row, in tile);
            }
            var replication = new RuntimeTileManipulationReplicationRegistry();
            State = new(playerEvents: Observer, worldTiles: Tiles, tileManipulationReplication: replication);
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!).Players;
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(992990), session.Handle);
            var spawn = new PlayerSpawnCommitRequest(Connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, spawn));
            Assert.True(replication.TryRegister(Connection.Source, OwnerOutbound));
            replication.PlayerSpawned(Connection, in spawn);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(992991), new(new(1), new(1)));
            var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            Assert.True(replication.TryRegister(peer.Source, PeerOutbound));
            replication.PlayerSpawned(peer, in peerSpawn);
            SetItem(9, stack);
            SetItem(17, 3);
            SetItem(58, 9);
            Observer.Armed = true;
        }

        internal void SetItem(short slot, short stack) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Connection.Player.Slot, slot, stack, 0, 1141, 1)));
        internal void Unlock(short row) => State.Apply(new ClientTempleDoorUnlockRuntimeCommand(Connection, new(2, 40, (short)(50 + row))));
        internal RuntimePlayerInventoryItem Item(short slot)
        {
            Assert.True(State.TryCapturePlayerInventoryItem(Connection.Player, slot, out var item));
            return item;
        }
        internal WorldTile[] CopyDoor() => Enumerable.Range(0, 3).Select(row => Tiles.Get(40, 50 + row)).ToArray();
        internal void AssertUnlocked(WorldTile[] before)
        {
            for (int row = 0; row < 3; row++)
            {
                WorldTile expected = before[row];
                expected.FrameY += 54;
                Assert.Equal(expected, Tiles.Get(40, 50 + row));
            }
        }
        private static TerrariaConnectionOutboundQueue Outbound() => new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 8192, maxFrameBytes: 2048));
        public void Dispose() => session.Dispose();
    }
}
