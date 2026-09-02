using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeObjectPlacementProductionCompositionTests
{
    [Fact]
    public void Bound_world_metadata_makes_packet79_a_real_server_runtime_command()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1910);
        fixture.SetSelectedChest(connection, stack: 2);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        TerrariaPlaceObjectState packet = fixture.BaseChestPacket();

        fixture.State.Apply(new ClientPlaceObjectRuntimeCommand(connection, packet));

        AssertCell(fixture.Tiles.Get(10, 9), frameX: 0, frameY: 0);
        AssertCell(fixture.Tiles.Get(11, 9), frameX: 18, frameY: 0);
        AssertCell(fixture.Tiles.Get(10, 10), frameX: 0, frameY: 18);
        AssertCell(fixture.Tiles.Get(11, 10), frameX: 18, frameY: 18);

        WorldChest chest = Assert.Single(fixture.Chests.CaptureSnapshot());
        Assert.Equal(10, chest.X);
        Assert.Equal(9, chest.Y);
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(
            connection.Player,
            0,
            out RuntimePlayerInventoryItem remaining));
        Assert.Equal(VanillaItemIds.Chest, remaining.ItemType);
        Assert.Equal((short)1, remaining.Stack);
    }

    [Fact]
    public void Wire_random_and_direction_fields_do_not_override_verified_item_object_identity()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 1911);
        fixture.SetSelectedChest(connection, stack: 2);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        TerrariaPlaceObjectState packet = fixture.BaseChestPacket() with
        {
            Random = 0,
            Direction = true
        };

        fixture.State.Apply(new ClientPlaceObjectRuntimeCommand(connection, packet));

        Assert.True(fixture.Tiles.Get(10, 9).IsActive);
        Assert.Single(fixture.Chests.CaptureSnapshot());
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(
            connection.Player,
            0,
            out RuntimePlayerInventoryItem remaining));
        Assert.Equal((short)1, remaining.Stack);
    }

    [Fact]
    public void World_metadata_bindings_are_isolated_by_exact_tile_store_identity()
    {
        var tilesA = new WorldTileStore(new WorldDimensions(20, 20));
        var tilesB = new WorldTileStore(new WorldDimensions(20, 20));
        var chestsA = new RuntimeChestStore([]);
        var chestsB = new RuntimeChestStore([]);

        RuntimeWorldObjectMetadataRegistry.Bind(tilesA, chestsA);
        RuntimeWorldObjectMetadataRegistry.Bind(tilesB, chestsB);

        Assert.True(RuntimeWorldObjectMetadataRegistry.TryGet(tilesA, out IVanillaMultiTileObjectMetadataLifecycle metadataA));
        Assert.True(RuntimeWorldObjectMetadataRegistry.TryGet(tilesB, out IVanillaMultiTileObjectMetadataLifecycle metadataB));
        Assert.NotSame(metadataA, metadataB);
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
            RuntimeWorldObjectMetadataRegistry.Bind(Tiles, Chests);
            State = new ServerRuntimeState(worldTiles: Tiles);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeChestStore Chests { get; }
        public ServerRuntimeState State { get; }

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
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public void SetSelectedChest(ConnectionHandle connection, short stack)
        {
            var request = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: stack,
                Prefix: 0,
                ItemNetId: checked((short)VanillaItemIds.Chest.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, request));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
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
