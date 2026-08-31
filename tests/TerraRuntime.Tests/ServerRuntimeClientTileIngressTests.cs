using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeClientTileIngressTests
{
    [Fact]
    public void Source_backed_dirt_request_commits_authoritatively_on_empty_target()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 901);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, stack: 20);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: checked((short)VanillaTileIds.Dirt.Value),
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        WorldTile placed = fixture.Tiles.Get(10, 10);
        Assert.True(placed.IsActive);
        Assert.Equal(VanillaTileIds.Dirt, placed.TileType);
        Assert.Equal(2, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(1, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Nonempty_target_rejects_dirt_without_a_second_world_commit()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 902);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, stack: 20);
        var existing = new WorldTile { Wall = 1 };
        fixture.Tiles.Set(10, 10, in existing);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        _ = fixture.Tiles.DirtySections.Drain(drained);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: checked((short)VanillaTileIds.Dirt.Value),
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(existing, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Empty_selected_inventory_slot_rejects_dirt_placement()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 903);
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: checked((short)VanillaTileIds.Dirt.Value),
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Selected_dirt_item_rejects_a_different_tile_claim()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 904);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, stack: 20);
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: 1,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Stale_connection_and_packet17_world_margin_are_rejected_authoritatively()
    {
        using var fixture = new Fixture();
        ConnectionHandle current = fixture.SpawnPlayer(connectionId: 905);
        var stale = new ConnectionHandle(
            GameCommandSourceId.FromConnection(906),
            current.Player);
        var request = new TerrariaTileManipulationState(0, 10, 10, 0, 0);
        var outsideVanillaMargin = request with { TileX = 2 };

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(stale, request));
        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(current, outsideVanillaMargin));

        Assert.Equal(2, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(2, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Kill_tile_no_item_is_wire_known_but_not_admitted_without_destruction_authority()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 907);
        Assert.True(VanillaDirtPlacement.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTileNoItem,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Kill_tile_no_item_is_rejected_before_world_topology_can_change_the_result()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 908);
        Assert.True(VanillaDirtPlacement.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        Assert.True(VanillaDirtPlacement.TryPlaceOnEmpty(fixture.Tiles, 11, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTileNoItem,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Normal_kill_tile_without_supported_pickaxe_is_rejected_without_commit()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 909);
        Assert.True(VanillaDirtPlacement.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 10, 10);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        Assert.Equal(1, fixture.Tiles.DirtySections.Drain(drained));
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(beforeVersion, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Unknown_action_is_preserved_but_not_treated_as_authorized_gameplay()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 910);
        var request = new TerrariaTileManipulationState(Action: 5, TileX: 10, TileY: 10, Data: 0, Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            State = new ServerRuntimeState(worldTiles: Tiles);
        }

        public WorldTileStore Tiles { get; }
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

        public void Dispose() => session?.Dispose();
    }
}
