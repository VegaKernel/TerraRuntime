using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerEventFanoutTests
{
    [Fact]
    public void Nested_fanout_delivers_spawn_and_disconnect_to_all_independent_sinks_once()
    {
        var network = new RecordingSink();
        var npcs = new RecordingSink();
        var projectiles = new RecordingSink();
        var entityReplication = new RuntimePlayerEventFanout(npcs, projectiles);
        var all = new RuntimePlayerEventFanout(network, entityReplication);
        ConnectionHandle connection = new(
            GameCommandSourceId.FromConnection(42),
            new PlayerHandle(new PlayerSlotId(7), new PlayerSessionGeneration(3)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 200, 0, 0, 0, 0, 0);

        all.PlayerSpawned(connection, in spawn);
        all.PlayerDisconnected(connection);

        Assert.Equal(1, network.Spawned);
        Assert.Equal(1, npcs.Spawned);
        Assert.Equal(1, projectiles.Spawned);
        Assert.Equal(1, network.Disconnected);
        Assert.Equal(1, npcs.Disconnected);
        Assert.Equal(1, projectiles.Disconnected);
    }

    private sealed class RecordingSink : IRuntimePlayerEventSink
    {
        public int Spawned { get; private set; }

        public int Disconnected { get; private set; }

        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) => Spawned++;

        public void PlayerDisconnected(ConnectionHandle connection) => Disconnected++;

        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
        {
        }

        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
        }

        public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
        {
        }

        public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
        {
        }

        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
        {
        }
    }
}
