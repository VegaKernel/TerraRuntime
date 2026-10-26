using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeClientTileIngressTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paid_placement_retains_its_source_unslope_tail_in_the_same_cohort(bool above)
    {
        using var fixture = new Fixture(ownedPlacement: true);
        var connection = fixture.SpawnPlayer(899);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, 20);
        int neighborY = above ? 51 : 53;
        var neighbor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        Assert.True(neighbor.TrySetSourceShape(false, above ? (byte)3 : (byte)1));
        fixture.Tiles.Set(50, neighborY, neighbor);
        PlacementSourceScene1458.Start(fixture.State, fixture.Tiles, connection, 50, 52);
        var after = PlacementSourceScene1458.AfterOne(fixture.State, connection);
        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, new(1, 50, 52, 0, 0)));
        Assert.False(fixture.Tiles.Get(50, 52).IsActive);
        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, new(14, 50, (short)neighborY, 0, 0)));
        Assert.False(fixture.Tiles.Get(50, 52).IsActive);
        Assert.Equal(neighbor, fixture.Tiles.Get(50, neighborY));
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(connection, after));
        Assert.True(fixture.Tiles.Get(50, 52).IsActive);
        Assert.Equal((byte)0, fixture.Tiles.Get(50, neighborY).SourceSlope);
        Assert.Equal(2, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        PlacementSourceScene1458.AssertFundedStock(fixture.State, connection, 19);
    }

    [Fact]
    public void Imported_dirt_and_a_raw_placement_report_do_not_create_paid_world_authority()
    {
        using var fixture = new Fixture();
        var connection = fixture.SpawnPlayer(900);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, 20);
        var before = fixture.Tiles.Get(10, 10);
        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, new(1, 10, 10, 0, 0)));
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(connection.Player, 0, out var item));
        Assert.Equal((short)20, item.Stack);
    }

    [Fact]
    public void Source_backed_dirt_request_commits_authoritatively_on_empty_target()
    {
        using var fixture = new Fixture(ownedPlacement: true);
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 901);
        fixture.SetSelectedInventoryItem(connection, VanillaItemIds.DirtBlock, stack: 20);
        PlacementSourceScene1458.Start(fixture.State, fixture.Tiles, connection, 50, 52);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(fixture.Tiles.Dimensions, 50, 52);
        long beforeVersion = fixture.Tiles.GetSectionVersion(section);
        Span<WorldSectionId> dirty = stackalloc WorldSectionId[1];
        _ = fixture.Tiles.DirtySections.Drain(dirty);
        var after = PlacementSourceScene1458.AfterOne(fixture.State, connection);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 50,
            TileY: 52,
            Data: checked((short)VanillaTileIds.Dirt.Value),
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.False(fixture.Tiles.Get(50, 52).IsActive);
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(connection, after));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        WorldTile placed = fixture.Tiles.Get(50, 52);
        Assert.True(placed.IsActive);
        Assert.Equal(VanillaTileIds.Dirt, placed.TileType);
        Assert.Equal(beforeVersion + 2, fixture.Tiles.GetSectionVersion(section));
        Assert.Equal(1, fixture.Tiles.DirtySections.DirtyCount);
        PlacementSourceScene1458.AssertFundedStock(fixture.State, connection, 19);
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
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
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
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 11, 10));
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
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
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

        public Fixture(bool ownedPlacement = false)
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            State = ownedPlacement ? PlacementSourceScene1458.Create(Tiles) : new ServerRuntimeState(worldTiles: Tiles);
        }

        public WorldTileStore Tiles { get; }
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
