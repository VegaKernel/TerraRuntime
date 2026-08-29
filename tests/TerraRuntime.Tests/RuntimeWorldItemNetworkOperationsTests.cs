using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemNetworkOperationsTests
{
    [Fact]
    public void Network_operations_surface_existing_world_item_replication_counters()
    {
        var admission = new TerrariaConnectionAdmissionGate(maxConnections: 8);
        var connections = new RuntimeConnectionRegistry();
        var replication = new RuntimeWorldItemReplicationRegistry();
        var operations = new LocalRuntimeNetworkOperations(
            admission,
            connections,
            new RuntimeConnectionQueueTelemetry(),
            new RuntimeConnectionRateTelemetry(),
            npcReplication: null,
            projectileReplication: null,
            worldItemReplication: replication);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 1, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));

        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        WorldItemSnapshot item = CreateItem(revision: 1, itemNetId: 1, positionX: 100f);
        replication.WorldItemStateCommitted(WorldItemStateCommitKind.Drop, in item);

        WorldItemSnapshot rejected = item with
        {
            Revision = new WorldItemRevision(2),
            PositionX = 130f
        };
        replication.WorldItemStateCommitted(WorldItemStateCommitKind.Drop, in rejected);

        WorldItemSnapshot unsupported = rejected with
        {
            Revision = new WorldItemRevision(3),
            ItemNetId = 0
        };
        replication.WorldItemStateCommitted(WorldItemStateCommitKind.Drop, in unsupported);

        RuntimeNetworkSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(1, snapshot.WorldItemRelayedFrames);
        Assert.Equal(1, snapshot.WorldItemRejectedFrames);
        Assert.Equal(1, snapshot.WorldItemUnsupportedCommits);
    }

    [Fact]
    public void Network_operations_distinguish_capacity_and_admission_rate_rejections()
    {
        var admission = new TerrariaConnectionAdmissionGate(
            maxConnections: 1,
            maxAdmissionsPerWindow: 2,
            admissionWindow: TimeSpan.FromSeconds(1));
        var operations = new LocalRuntimeNetworkOperations(
            admission,
            new RuntimeConnectionRegistry(),
            new RuntimeConnectionQueueTelemetry(),
            new RuntimeConnectionRateTelemetry());

        Assert.True(admission.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? held));
        Assert.False(admission.TryAcquire(out _));
        Assert.False(admission.TryAcquire(out _));

        RuntimeNetworkSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(2, snapshot.RejectedConnections);
        Assert.Equal(1, snapshot.AdmissionCapacityRejectedConnections);
        Assert.Equal(1, snapshot.AdmissionRateRejectedConnections);

        held!.Dispose();
    }

    [Fact]
    public void Network_operations_surface_normalized_connection_stop_counters()
    {
        var stops = new RuntimeConnectionStopTelemetry();
        stops.Record(TerrariaConnectionStopReason.ProtocolFailure);
        stops.Record(TerrariaConnectionStopReason.ProtocolFailure);
        stops.Record(TerrariaConnectionStopReason.RateLimited);
        stops.Record(TerrariaConnectionStopReason.InvalidHandshake);
        stops.Record(TerrariaConnectionStopReason.UnsupportedProtocol);
        stops.Record(TerrariaConnectionStopReason.SlowClient);
        stops.Record(TerrariaConnectionStopReason.ApplicationStopped);
        stops.Record(TerrariaConnectionStopReason.HandshakeTimeout);
        stops.Record(TerrariaConnectionStopReason.IdleTimeout);

        var operations = new LocalRuntimeNetworkOperations(
            new TerrariaConnectionAdmissionGate(maxConnections: 8),
            new RuntimeConnectionRegistry(),
            new RuntimeConnectionQueueTelemetry(),
            new RuntimeConnectionRateTelemetry(),
            stopTelemetry: stops);

        RuntimeNetworkSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(2, snapshot.StopProtocolFailures);
        Assert.Equal(1, snapshot.StopRateLimited);
        Assert.Equal(1, snapshot.StopInvalidHandshake);
        Assert.Equal(1, snapshot.StopUnsupportedProtocol);
        Assert.Equal(1, snapshot.StopSlowClient);
        Assert.Equal(1, snapshot.StopApplicationStopped);
        Assert.Equal(1, snapshot.StopHandshakeTimeout);
        Assert.Equal(1, snapshot.StopIdleTimeout);
    }

    private static ConnectionHandle Connection(
        GameCommandSourceId source,
        byte slot,
        ulong generation) =>
        new(
            source,
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest CreatePlayerSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static WorldItemSnapshot CreateItem(ulong revision, short itemNetId, float positionX) =>
        new(
            Handle: new WorldItemHandle(7, new WorldItemGeneration(1)),
            Revision: new WorldItemRevision(revision),
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: -2f,
            Stack: 5,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: itemNetId,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: byte.MaxValue,
            TimeToKeepReservation: 0,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0);
}
