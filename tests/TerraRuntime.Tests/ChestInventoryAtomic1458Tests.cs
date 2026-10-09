using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ChestInventoryAtomic1458Tests
{
    [Fact]
    public void First_inventory_observer_sees_chest_metadata_footprint_stock_and_accepted_accounting()
    {
        foreach (short stack in new short[] { 1, 2 })
        {
            using var f = new Fixture(stack);
            f.Observer.Callback = request =>
            {
                f.AssertChest();
                Assert.Equal(stack == 1 ? default : new RuntimePlayerInventoryItem(new(48), 1, new(0), 1), f.Item());
                Assert.Equal(1, f.Processor.Applied);
                Assert.Equal(RuntimeObjectPlacementResult.Applied, f.Processor.LastResult);
                Assert.Equal(0, f.Processor.Rollbacks);
            };
            Assert.True(f.Place());
            Assert.Equal(1, f.Observer.Publications);
            f.AssertChest();
            Assert.Equal(0, f.OwnerOutbound.QueuedFrames);
            Assert.Equal(1, f.PeerOutbound.QueuedFrames);
        }
    }

    [Fact]
    public void Post_publication_reentry_preserves_the_accepted_chest_and_suppresses_stale_placement_frames()
    {
        foreach (string mode in new[] { "stock", "world", "disconnect", "metadata-aba" })
        {
            using var f = new Fixture(2);
            f.Observer.Callback = request =>
            {
                Assert.Equal((short)1, f.Item().Stack);
                f.Observer.Callback = null;
                if (mode == "stock") f.SetItem(7);
                if (mode == "world")
                {
                    WorldTile edited = f.Tiles.Get(10, 9);
                    edited.FrameX = 216;
                    f.Tiles.Set(10, 9, in edited);
                }
                if (mode == "disconnect") f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connection));
                if (mode == "metadata-aba")
                {
                    Assert.True(f.Chests.TryRemoveAt(10, 9, out _));
                    Assert.True(f.Chests.TryCreate(10, 9, 40, out _));
                }
            };
            Assert.True(f.Place());
            Assert.Equal(1, f.Processor.Applied);
            Assert.Equal(0, f.Processor.Rollbacks);
            Assert.Equal(0, f.PeerOutbound.QueuedFrames);
            Assert.Single(f.Chests.CaptureSnapshot());
            if (mode == "world") Assert.Equal((short)216, f.Tiles.Get(10, 9).FrameX);
            else f.AssertChest();
            if (mode == "stock") Assert.Equal((short)7, f.Item().Stack);
            else if (mode != "disconnect") Assert.Equal((short)1, f.Item().Stack);
        }
    }

    [Fact]
    public void Throwing_inventory_observer_leaves_all_adopted_owners_and_accounting_complete()
    {
        using var f = new Fixture(1);
        f.Observer.Callback = _ => throw new ObserverFailure();
        Assert.Throws<ObserverFailure>(() => f.Place());
        f.AssertChest();
        Assert.Equal(default, f.Item());
        Assert.Equal(1, f.Processor.Applied);
        Assert.Equal(RuntimeObjectPlacementResult.Applied, f.Processor.LastResult);
        Assert.Equal(0, f.Processor.Rollbacks);
        Assert.Equal(0, f.PeerOutbound.QueuedFrames);
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
        internal readonly RuntimeChestStore Chests = new([]);
        internal readonly Observer Observer = new();
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeObjectPlacementCommandProcessor Processor;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaConnectionOutboundQueue OwnerOutbound = Outbound();
        internal readonly TerrariaConnectionOutboundQueue PeerOutbound = Outbound();
        private readonly PlayerJoinSession session;

        internal Fixture(short stack)
        {
            Players = new(events: Observer, worldTiles: Tiles);
            var replication = new RuntimeTileManipulationReplicationRegistry();
            Processor = new(Tiles, Chests, Players, new RuntimeCommandCounter(), replication);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(14587901), session.Handle);
            var spawn = new PlayerSpawnCommitRequest(Connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            Assert.True(Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, spawn)));
            Assert.Equal(PlayerSpawnCommitResult.Committed, Players.LastSpawnCommitResult);
            Assert.True(replication.TryRegister(Connection.Source, OwnerOutbound));
            replication.PlayerSpawned(Connection, in spawn);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(14587902), new(new(1), new(1)));
            var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            Assert.True(replication.TryRegister(peer.Source, PeerOutbound));
            replication.PlayerSpawned(peer, in peerSpawn);
            var support = new WorldTile { Type = 0, Flags = WorldTileFlags.Active };
            Tiles.Set(10, 11, in support); Tiles.Set(11, 11, in support);
            SetItem(stack);
            Observer.Armed = true;
        }

        internal void SetItem(short stack) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Connection.Player.Slot, 0, stack, 0, 48, 1)));
        internal bool Place() => Processor.TryApply(new ClientPlaceObjectRuntimeCommand(Connection,
            new(10, 10, 21, 0, 0, -1, false)));
        internal RuntimePlayerInventoryItem Item()
        {
            Assert.True(Players.TryGetInventoryItem(Connection, 0, out var item));
            return item;
        }
        internal void AssertChest()
        {
            WorldChest chest = Assert.Single(Chests.CaptureSnapshot());
            Assert.Equal(10, chest.X); Assert.Equal(9, chest.Y);
            for (int row = 0; row < 2; row++)
            for (int column = 0; column < 2; column++)
            {
                WorldTile tile = Tiles.Get(10 + column, 9 + row);
                Assert.True(tile.IsActive);
                Assert.Equal(VanillaTileIds.Containers, tile.TileType);
                Assert.Equal((short)(column * 18), tile.FrameX);
                Assert.Equal((short)(row * 18), tile.FrameY);
            }
        }
        private static TerrariaConnectionOutboundQueue Outbound() => new(new OutboundQueueOptions(16, 8192, 2048));
        public void Dispose() => session.Dispose();
    }
}
