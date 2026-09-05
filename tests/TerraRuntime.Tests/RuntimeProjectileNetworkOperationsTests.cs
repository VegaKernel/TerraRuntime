using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileNetworkOperationsTests
{
    [Fact]
    public void Network_operations_surface_existing_projectile_replication_counters()
    {
        var admission = new TerrariaConnectionAdmissionGate(maxConnections: 8);
        var connections = new RuntimeConnectionRegistry();
        var replication = new RuntimeProjectileReplicationRegistry();
        var operations = new LocalRuntimeNetworkOperations(
            admission,
            connections,
            new RuntimeConnectionQueueTelemetry(),
            new RuntimeConnectionRateTelemetry(),
            npcReplication: null,
            projectileReplication: replication);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 2, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));

        ProjectileSnapshot projectile = CreateProjectile(revision: 1, positionX: 100f);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in projectile);
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        ProjectileSnapshot moved = projectile with
        {
            Revision = new ProjectileRevision(2),
            PositionX = 130f
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in moved);

        ProjectileSnapshot rejected = moved with
        {
            Revision = new ProjectileRevision(3),
            PositionX = 150f
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in rejected);

        ProjectileSnapshot unsupported = rejected with
        {
            Revision = new ProjectileRevision(4),
            PositionX = float.NaN
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in unsupported);

        RuntimeNetworkSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(1, snapshot.ProjectileBaselineFrames);
        Assert.Equal(1, snapshot.ProjectileRelayedFrames);
        Assert.Equal(1, snapshot.ProjectileRejectedFrames);
        Assert.Equal(1, snapshot.ProjectileUnsupportedCommits);
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

    private static ProjectileSnapshot CreateProjectile(ulong revision, float positionX) =>
        new(
            Handle: new ProjectileHandle(7, new ProjectileGeneration(1)),
            Revision: new ProjectileRevision(revision),
            Type: new ProjectileTypeId(1),
            Spawner: 4,
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: -2f,
            Ai: new ProjectileAiState(1f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 25,
            KnockBack: 2f,
            OriginalDamage: 25);
}
