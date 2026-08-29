using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeClientTileIngressTests
{
    [Fact]
    public void Known_in_bounds_request_is_validated_but_cannot_mutate_world_without_gameplay_authority()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 901);
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: 1,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(1, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Stale_connection_and_out_of_bounds_coordinates_are_rejected_authoritatively()
    {
        using var fixture = new Fixture();
        ConnectionHandle current = fixture.SpawnPlayer(connectionId: 902);
        var stale = new ConnectionHandle(
            GameCommandSourceId.FromConnection(903),
            current.Player);
        var request = new TerrariaTileManipulationState(0, 10, 10, 0, 0);
        var outside = request with { TileX = -1 };

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(stale, request));
        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(current, outside));

        Assert.Equal(2, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(2, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.Tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Unmodeled_action_is_preserved_but_not_treated_as_authorized_gameplay()
    {
        using var fixture = new Fixture();
        ConnectionHandle connection = fixture.SpawnPlayer(connectionId: 904);
        var request = new TerrariaTileManipulationState(Action: 5, TileX: 10, TileY: 10, Data: 0, Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(connection, request));

        Assert.Equal(1, fixture.State.ClientTileManipulationRequests);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
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

        public void Dispose() => session?.Dispose();
    }
}
