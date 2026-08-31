using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeSimpleTileKillAuthorityTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(53, true)]
    [InlineData(2, false)]
    [InlineData(147, false)]
    [InlineData(226, false)]
    public void Simple_kill_catalog_is_explicit_and_fail_closed(int rawTileType, bool expected)
    {
        Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
        Assert.Equal(expected, VanillaSimpleTileKillCatalog.IsSupported(tileType));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(53, 169)]
    public void Copper_pickaxe_commits_supported_simple_tile_and_drop(int rawTileType, int expectedDropItem)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9400 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(10, 10, rawTileType);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.False(fixture.Tiles.Get(10, 10).IsActive);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot drop));
        Assert.Equal(expectedDropItem, drop.ItemNetId);
    }

    [Theory]
    [InlineData(2)]   // Grass transforms to Dirt when a pick breaks the grass layer.
    [InlineData(147)] // Snow is not yet an end-to-end modelled drop/removal family.
    [InlineData(226)] // Lihzahrd Brick has a source-backed 210 pick-power gate.
    public void Copper_pickaxe_cannot_generic_clear_unmodelled_tile(int rawTileType)
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 9500 + rawTileType);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.CopperPickaxe, stack: 1);
        fixture.SetActiveTile(10, 10, rawTileType);
        WorldTile before = fixture.Tiles.Get(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(
            connection,
            new TerrariaTileManipulationState(
                (byte)TerrariaTileManipulationAction.KillTile,
                TileX: 10,
                TileY: 10,
                Data: 0,
                Style: 0)));

        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
    }

    [Fact]
    public void Generic_world_mutation_reports_transforming_grass_as_unsupported_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var grass = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Grass.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(20, 20, in grass);
        var service = new VanillaWorldTileMutationService(tiles);
        var request = new WorldTileMutationRequest(WorldTileMutationKind.KillTile, 20, 20);

        WorldTileMutationResult result = service.Apply(in request);

        Assert.Equal(WorldTileMutationStatus.UnsupportedState, result.Status);
        Assert.Equal(grass, tiles.Get(20, 20));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Items = new RuntimeWorldItemStore();
            State = new ServerRuntimeState(worldTiles: Tiles, worldItems: Items);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore Items { get; }
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
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

        public void SetSelectedInventoryItem(ConnectionHandle connection, ItemTypeId itemType, short stack)
        {
            var request = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: stack,
                Prefix: 0,
                ItemNetId: checked((short)itemType.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, request));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
        }

        public void SetActiveTile(int x, int y, int rawTileType)
        {
            Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId tileType));
            var tile = new WorldTile
            {
                Type = checked((ushort)tileType.Value),
                Flags = WorldTileFlags.Active
            };
            Tiles.Set(x, y, in tile);
        }

        public void Dispose() => session?.Dispose();
    }
}
