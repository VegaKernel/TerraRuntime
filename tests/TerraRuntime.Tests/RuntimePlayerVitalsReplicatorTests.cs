using TerraRuntime.Application.Operations;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerVitalsReplicatorTests
{
    [Fact]
    public void Playing_health_relays_to_peers_but_mana_does_not_relay_immediately()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        ConnectionHandle first = Connection(firstSource, slot: 1, generation: 1);
        ConnectionHandle second = Connection(secondSource, slot: 2, generation: 1);

        Assert.True(replicator.TryRegister(firstSource, firstOutbound));
        Assert.True(replicator.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first.Player.Slot);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second.Player.Slot);
        replicator.PlayerSpawned(first, in firstSpawn);
        replicator.PlayerSpawned(second, in secondSpawn);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        var health = new PlayerHealthCommitRequest(first.Player.Slot, Life: 80, MaxLife: 100);
        replicator.PlayerHealthUpdated(first, in health);

        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(1, secondOutbound.QueuedFrames);
        Assert.Equal(1, replicator.RelayedHealthFrames);

        var mana = new PlayerManaCommitRequest(first.Player.Slot, Mana: 40, MaxMana: 80);
        replicator.PlayerManaUpdated(first, in mana);

        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(1, secondOutbound.QueuedFrames);
        Assert.Equal(1, replicator.RelayedHealthFrames);
    }

    [Fact]
    public void Identical_playing_health_is_not_relayed_twice_to_peers()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(3);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(4);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        ConnectionHandle first = Connection(firstSource, slot: 3, generation: 1);
        ConnectionHandle second = Connection(secondSource, slot: 4, generation: 1);

        Assert.True(replicator.TryRegister(firstSource, firstOutbound));
        Assert.True(replicator.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first.Player.Slot);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second.Player.Slot);
        replicator.PlayerSpawned(first, in firstSpawn);
        replicator.PlayerSpawned(second, in secondSpawn);

        var health = new PlayerHealthCommitRequest(first.Player.Slot, Life: 80, MaxLife: 100);
        replicator.PlayerHealthUpdated(first, in health);
        int afterFirst = secondOutbound.QueuedFrames;
        replicator.PlayerHealthUpdated(first, in health);

        Assert.Equal(afterFirst, secondOutbound.QueuedFrames);
        Assert.Equal(1, replicator.RelayedHealthFrames);
        Assert.Equal(1, replicator.SuppressedDuplicateHealthFrames);

        var operations = new LocalRuntimeNetworkOperations(
            new TerrariaConnectionAdmissionGate(8),
            new RuntimeConnectionRegistry(),
            new RuntimeConnectionQueueTelemetry(),
            new RuntimeConnectionRateTelemetry(),
            vitalsReplication: replicator);
        RuntimeNetworkSnapshot snapshot = operations.CaptureSnapshot();
        Assert.Equal(1, snapshot.HealthRelayedFrames);
        Assert.Equal(1, snapshot.SuppressedDuplicateHealthFrames);
    }

    [Fact]
    public void Authoritative_identical_health_reasserts_owner_without_relaying_duplicate_to_peer()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId ownerSource = GameCommandSourceId.FromConnection(5);
        GameCommandSourceId peerSource = GameCommandSourceId.FromConnection(6);
        var ownerOutbound = CreateOutbound();
        var peerOutbound = CreateOutbound();
        ConnectionHandle owner = Connection(ownerSource, slot: 5, generation: 1);
        ConnectionHandle peer = Connection(peerSource, slot: 6, generation: 1);

        Assert.True(replicator.TryRegister(ownerSource, ownerOutbound));
        Assert.True(replicator.TryRegister(peerSource, peerOutbound));
        PlayerSpawnCommitRequest ownerSpawn = CreateSpawn(owner.Player.Slot);
        PlayerSpawnCommitRequest peerSpawn = CreateSpawn(peer.Player.Slot);
        replicator.PlayerSpawned(owner, in ownerSpawn);
        replicator.PlayerSpawned(peer, in peerSpawn);

        var health = new PlayerHealthCommitRequest(owner.Player.Slot, Life: 100, MaxLife: 100);
        replicator.PlayerHealthUpdated(owner, in health);
        int peerAfterInitial = peerOutbound.QueuedFrames;
        int ownerBeforeCorrection = ownerOutbound.QueuedFrames;

        replicator.PlayerAuthoritativeHealthUpdated(owner, in health);

        Assert.Equal(ownerBeforeCorrection + 1, ownerOutbound.QueuedFrames);
        Assert.Equal(peerAfterInitial, peerOutbound.QueuedFrames);
        Assert.Equal(2, replicator.RelayedHealthFrames);
        Assert.Equal(1, replicator.SuppressedDuplicateHealthFrames);
    }

    [Fact]
    public void Pre_spawn_health_and_mana_are_exchanged_as_bidirectional_spawn_baselines()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(10);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(20);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        ConnectionHandle first = Connection(firstSource, slot: 10, generation: 1);
        ConnectionHandle second = Connection(secondSource, slot: 20, generation: 1);

        Assert.True(replicator.TryRegister(firstSource, firstOutbound));
        Assert.True(replicator.TryRegister(secondSource, secondOutbound));

        var firstHealth = new PlayerHealthCommitRequest(first.Player.Slot, Life: 100, MaxLife: 100);
        var firstMana = new PlayerManaCommitRequest(first.Player.Slot, Mana: 20, MaxMana: 20);
        var secondHealth = new PlayerHealthCommitRequest(second.Player.Slot, Life: 75, MaxLife: 100);
        var secondMana = new PlayerManaCommitRequest(second.Player.Slot, Mana: 60, MaxMana: 80);
        replicator.PlayerHealthUpdated(first, in firstHealth);
        replicator.PlayerManaUpdated(first, in firstMana);
        replicator.PlayerHealthUpdated(second, in secondHealth);
        replicator.PlayerManaUpdated(second, in secondMana);
        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first.Player.Slot);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second.Player.Slot);
        replicator.PlayerSpawned(first, in firstSpawn);
        replicator.PlayerSpawned(second, in secondSpawn);

        Assert.Equal(2, firstOutbound.QueuedFrames);
        Assert.Equal(2, secondOutbound.QueuedFrames);
        Assert.Equal(2, replicator.HealthBaselineFrames);
        Assert.Equal(2, replicator.ManaBaselineFrames);
    }

    [Fact]
    public void New_generation_drops_stale_mana_snapshot_from_reused_slot()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(30);
        GameCommandSourceId peerSource = GameCommandSourceId.FromConnection(40);
        var outbound = CreateOutbound();
        var peerOutbound = CreateOutbound();
        ConnectionHandle oldConnection = Connection(source, slot: 3, generation: 1);
        ConnectionHandle newConnection = Connection(source, slot: 3, generation: 2);
        ConnectionHandle peer = Connection(peerSource, slot: 4, generation: 1);

        Assert.True(replicator.TryRegister(source, outbound));
        Assert.True(replicator.TryRegister(peerSource, peerOutbound));

        var oldHealth = new PlayerHealthCommitRequest(oldConnection.Player.Slot, Life: 50, MaxLife: 100);
        var oldMana = new PlayerManaCommitRequest(oldConnection.Player.Slot, Mana: 40, MaxMana: 80);
        replicator.PlayerHealthUpdated(oldConnection, in oldHealth);
        replicator.PlayerManaUpdated(oldConnection, in oldMana);

        var newHealth = new PlayerHealthCommitRequest(newConnection.Player.Slot, Life: 90, MaxLife: 100);
        replicator.PlayerHealthUpdated(newConnection, in newHealth);
        PlayerSpawnCommitRequest newSpawn = CreateSpawn(newConnection.Player.Slot);
        replicator.PlayerSpawned(newConnection, in newSpawn);

        PlayerSpawnCommitRequest peerSpawn = CreateSpawn(peer.Player.Slot);
        replicator.PlayerSpawned(peer, in peerSpawn);

        Assert.Equal(1, peerOutbound.QueuedFrames);
        Assert.Equal(1, replicator.HealthBaselineFrames);
        Assert.Equal(0, replicator.ManaBaselineFrames);
    }

    [Fact]
    public void Health_normalizes_max_life_before_relay_encoding()
    {
        var replicator = new RuntimePlayerVitalsReplicator();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(50);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(60);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        ConnectionHandle first = Connection(firstSource, slot: 5, generation: 1);
        ConnectionHandle second = Connection(secondSource, slot: 6, generation: 1);

        Assert.True(replicator.TryRegister(firstSource, firstOutbound));
        Assert.True(replicator.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first.Player.Slot);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second.Player.Slot);
        replicator.PlayerSpawned(first, in firstSpawn);
        replicator.PlayerSpawned(second, in secondSpawn);

        var health = new PlayerHealthCommitRequest(first.Player.Slot, Life: 1, MaxLife: 1);
        replicator.PlayerHealthUpdated(first, in health);

        Assert.Equal(1, secondOutbound.QueuedFrames);
        Assert.Equal(1, replicator.RelayedHealthFrames);
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
