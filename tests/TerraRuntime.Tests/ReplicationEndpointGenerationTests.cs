using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ReplicationEndpointGenerationTests
{
    [Fact]
    public void Sign_endpoint_ignores_disconnect_from_an_older_generation()
    {
        var registry = new RuntimeSignReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(101);
        var outbound = CreateOutbound();
        ConnectionHandle oldConnection = Connection(source, slot: 3, generation: 1);
        ConnectionHandle newConnection = Connection(source, slot: 3, generation: 2);

        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawn(newConnection.Player.Slot);
        registry.PlayerSpawned(newConnection, in spawn);
        registry.PlayerDisconnected(oldConnection);

        Assert.True(registry.TrySendRead(newConnection, new WorldSign(0, "hello", 10, 20)));
        Assert.Equal(1, outbound.QueuedFrames);
    }

    [Fact]
    public void Chest_endpoint_ignores_disconnect_from_an_older_generation()
    {
        var registry = new RuntimeChestReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(102);
        var outbound = CreateOutbound();
        ConnectionHandle oldConnection = Connection(source, slot: 4, generation: 1);
        ConnectionHandle newConnection = Connection(source, slot: 4, generation: 2);

        Assert.True(registry.TryRegister(source, outbound));
        PlayerSpawnCommitRequest spawn = CreateSpawn(newConnection.Player.Slot);
        registry.PlayerSpawned(newConnection, in spawn);
        registry.PlayerDisconnected(oldConnection);

        var chest = new WorldChest(0, 12, 24, "storage", []);
        Assert.True(registry.TrySendName(newConnection, chest));
        Assert.Equal(1, outbound.QueuedFrames);
    }

    [Fact]
    public void Vitals_endpoint_keeps_new_generation_snapshots_after_old_disconnect()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(103);
        GameCommandSourceId peerSource = GameCommandSourceId.FromConnection(104);
        var outbound = CreateOutbound();
        var peerOutbound = CreateOutbound();
        ConnectionHandle oldConnection = Connection(source, slot: 5, generation: 1);
        ConnectionHandle newConnection = Connection(source, slot: 5, generation: 2);
        ConnectionHandle peer = Connection(peerSource, slot: 6, generation: 1);

        Assert.True(replicator.TryRegister(source, outbound));
        Assert.True(replicator.TryRegister(peerSource, peerOutbound));

        var health = new PlayerHealthCommitRequest(newConnection.Player.Slot, Life: 90, MaxLife: 100);
        var mana = new PlayerManaCommitRequest(newConnection.Player.Slot, Mana: 60, MaxMana: 80);
        replicator.PlayerHealthUpdated(newConnection, in health);
        replicator.PlayerManaUpdated(newConnection, in mana);
        PlayerSpawnCommitRequest newSpawn = CreateSpawn(newConnection.Player.Slot);
        replicator.PlayerSpawned(newConnection, in newSpawn);

        replicator.PlayerDisconnected(oldConnection);

        PlayerSpawnCommitRequest peerSpawn = CreateSpawn(peer.Player.Slot);
        replicator.PlayerSpawned(peer, in peerSpawn);

        Assert.Equal(2, peerOutbound.QueuedFrames);
        Assert.Equal(1, replicator.HealthBaselineFrames);
        Assert.Equal(1, replicator.ManaBaselineFrames);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(
        GameCommandSourceId source,
        byte slot,
        ulong generation) =>
        new(
            source,
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(
            slot,
            SpawnX: 100,
            SpawnY: 200,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);
}
