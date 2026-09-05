using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeObjectPlacementCommandProcessorTests
{
    [Fact]
    public void Selected_chest_item_places_base_container_creates_metadata_and_consumes_one_item()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1901);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.Chest, stack: 2);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        var packet = new TerrariaPlaceObjectState(
            TileX: 10,
            TileY: 10,
            TileType: checked((short)VanillaTileIds.Containers.Value),
            Style: 0,
            Alternate: 0,
            Random: -1,
            Direction: false);

        Assert.True(fixture.Processor.TryApply(
            new ClientPlaceObjectRuntimeCommand(connection, packet)));

        Assert.Equal(RuntimeObjectPlacementResult.Applied, fixture.Processor.LastResult);
        Assert.Equal(1, fixture.Processor.Requests);
        Assert.Equal(1, fixture.Processor.Applied);
        Assert.Equal(0, fixture.Processor.Rejected);
        Assert.Equal(0, fixture.Processor.Unsupported);
        Assert.Equal(0, fixture.Processor.Rollbacks);

        AssertCell(fixture.Tiles.Get(10, 9), frameX: 0, frameY: 0);
        AssertCell(fixture.Tiles.Get(11, 9), frameX: 18, frameY: 0);
        AssertCell(fixture.Tiles.Get(10, 10), frameX: 0, frameY: 18);
        AssertCell(fixture.Tiles.Get(11, 10), frameX: 18, frameY: 18);

        WorldChest chest = Assert.Single(fixture.Chests.CaptureSnapshot());
        Assert.Equal(10, chest.X);
        Assert.Equal(9, chest.Y);
        Assert.Equal(VanillaChestStorageFacts1458.DefaultItemSlots, chest.Items.Length);

        Assert.True(fixture.Players.TryGetInventoryItem(connection.Player, 0, out RuntimePlayerInventoryItem remaining));
        Assert.Equal(VanillaItemIds.Chest, remaining.ItemType);
        Assert.Equal((short)1, remaining.Stack);
    }

    [Fact]
    public void Unsupported_selected_item_never_reaches_world_mutation()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1902);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, stack: 20);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        TerrariaPlaceObjectState packet = fixture.BaseChestPacket();

        Assert.True(fixture.Processor.TryApply(
            new ClientPlaceObjectRuntimeCommand(connection, packet)));

        Assert.Equal(RuntimeObjectPlacementResult.UnsupportedSelectedItem, fixture.Processor.LastResult);
        Assert.Equal(1, fixture.Processor.Unsupported);
        Assert.False(fixture.Tiles.Get(10, 9).IsActive);
        Assert.Empty(fixture.Chests.CaptureSnapshot());
        Assert.True(fixture.Players.TryGetInventoryItem(connection.Player, 0, out RuntimePlayerInventoryItem item));
        Assert.Equal((short)20, item.Stack);
    }

    [Theory]
    [InlineData(467, 0, 0)]
    [InlineData(21, 1, 0)]
    [InlineData(21, 0, 1)]
    public void Packet_object_identity_must_match_selected_item_catalog(int tileType, int style, int alternate)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1903);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.Chest, stack: 2);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        TerrariaPlaceObjectState packet = fixture.BaseChestPacket() with
        {
            TileType = checked((short)tileType),
            Style = checked((short)style),
            Alternate = checked((byte)alternate)
        };

        Assert.True(fixture.Processor.TryApply(
            new ClientPlaceObjectRuntimeCommand(connection, packet)));

        Assert.Equal(RuntimeObjectPlacementResult.PacketMismatch, fixture.Processor.LastResult);
        Assert.False(fixture.Tiles.Get(10, 9).IsActive);
        Assert.Empty(fixture.Chests.CaptureSnapshot());
        Assert.True(fixture.Players.TryGetInventoryItem(connection.Player, 0, out RuntimePlayerInventoryItem item));
        Assert.Equal((short)2, item.Stack);
    }

    [Fact]
    public void Missing_support_rejects_before_metadata_and_inventory_changes()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1904);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.Chest, stack: 2);
        TerrariaPlaceObjectState packet = fixture.BaseChestPacket();

        Assert.True(fixture.Processor.TryApply(
            new ClientPlaceObjectRuntimeCommand(connection, packet)));

        Assert.Equal(RuntimeObjectPlacementResult.WorldRejected, fixture.Processor.LastResult);
        Assert.Equal(VanillaMultiTileObjectMutationStatus.MissingSupport, fixture.Processor.LastWorldStatus);
        Assert.False(fixture.Tiles.Get(10, 9).IsActive);
        Assert.Empty(fixture.Chests.CaptureSnapshot());
        Assert.True(fixture.Players.TryGetInventoryItem(connection.Player, 0, out RuntimePlayerInventoryItem item));
        Assert.Equal((short)2, item.Stack);
    }

    [Fact]
    public void Wrong_connection_source_rolls_world_and_chest_metadata_back_when_inventory_commit_rejects()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1905);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.Chest, stack: 2);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        var wrongSource = new ConnectionHandle(
            GameCommandSourceId.FromConnection(991905),
            connection.Player);
        TerrariaPlaceObjectState packet = fixture.BaseChestPacket();

        Assert.True(fixture.Processor.TryApply(
            new ClientPlaceObjectRuntimeCommand(wrongSource, packet)));

        Assert.Equal(RuntimeObjectPlacementResult.InventoryCommitFailed, fixture.Processor.LastResult);
        Assert.Equal(1, fixture.Processor.Rollbacks);
        Assert.False(fixture.Tiles.Get(10, 9).IsActive);
        Assert.False(fixture.Tiles.Get(11, 10).IsActive);
        Assert.Empty(fixture.Chests.CaptureSnapshot());
        Assert.True(fixture.Players.TryGetInventoryItem(connection.Player, 0, out RuntimePlayerInventoryItem item));
        Assert.Equal((short)2, item.Stack);
    }

    [Fact]
    public void Catalog_pins_only_the_verified_base_chest_mapping()
    {
        Assert.True(VanillaItemObjectPlacementCatalog.TryGet(
            VanillaItemIds.Chest,
            out VanillaItemObjectPlacementDefinition chest));
        Assert.Equal(48, chest.ItemType.Value);
        Assert.Equal(VanillaTileIds.Containers, chest.TileType);
        Assert.Equal((short)0, chest.Style);
        Assert.Equal((byte)0, chest.Alternate);
        Assert.False(VanillaItemObjectPlacementCatalog.TryGet(VanillaItemIds.DirtBlock, out _));
    }

    private static void AssertCell(WorldTile tile, short frameX, short frameY)
    {
        Assert.True(tile.IsActive);
        Assert.Equal(VanillaTileIds.Containers, tile.TileType);
        Assert.Equal(frameX, tile.FrameX);
        Assert.Equal(frameY, tile.FrameY);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Chests = new RuntimeChestStore([]);
            Players = new PlayerAuthority(events: null, worldTiles: Tiles);
            Commands = new RuntimeCommandCounter();
            Processor = new RuntimeObjectPlacementCommandProcessor(Tiles, Chests, Players, Commands);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeChestStore Chests { get; }
        public PlayerAuthority Players { get; }
        public RuntimeCommandCounter Commands { get; }
        public RuntimeObjectPlacementCommandProcessor Processor { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

            var connection = new ConnectionHandle(
                GameCommandSourceId.FromConnection(connectionId),
                session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
            Players.TryApply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, Players.LastSpawnCommitResult);
            return connection;
        }

        public void SetSelectedInventoryItem(ConnectionHandle connection, ItemTypeId itemType, short stack)
        {
            var request = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: stack,
                Prefix: 0,
                ItemNetId: checked((short)itemType.Value),
                ItemFlags: 0);
            Players.TryApply(new PlayerEquipmentRuntimeCommand(connection, request));
            Assert.Equal(0, Players.RejectedEquipmentUpdates);
        }

        public TerrariaPlaceObjectState BaseChestPacket() =>
            new(
                TileX: 10,
                TileY: 10,
                TileType: checked((short)VanillaTileIds.Containers.Value),
                Style: 0,
                Alternate: 0,
                Random: -1,
                Direction: false);

        public void SetSupport(int x, int y)
        {
            var tile = new WorldTile();
            Assert.True(tile.TrySetTileType(VanillaTileIds.Stone));
            tile.Flags = WorldTileFlags.Active;
            Tiles.Set(x, y, in tile);
        }

        public void Dispose() => session?.Dispose();
    }
}
