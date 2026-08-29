using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileLifetimeReplicationTests
{
    [Fact]
    public void Expired_player_owned_projectile_clears_baseline_without_packet29()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(601);
        var outbound = CreateQueue();
        Assert.True(replication.TryRegister(source, outbound));
        MarkPlaying(replication, source, playerSlot: 4);

        var store = new RuntimeProjectileStore(capacity: 4, commitSink: replication);
        ProjectileStateUpdate state = CreateProjectile(spawner: 3);
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);
        Assert.True(replication.WireIdentities.TryGetWireKey(spawned.Handle, out _));

        var executor = new RuntimeProjectileStateExecutor(store);
        ProjectileStateTickSummary summary = executor.Tick(new ExpireStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);
        Assert.False(replication.WireIdentities.TryGetWireKey(spawned.Handle, out _));

        GameCommandSourceId lateSource = GameCommandSourceId.FromConnection(602);
        var lateOutbound = CreateQueue();
        Assert.True(replication.TryRegister(lateSource, lateOutbound));
        MarkPlaying(replication, lateSource, playerSlot: 5);
        Assert.Equal(0, lateOutbound.QueuedFrames);
    }

    [Fact]
    public void Expired_server_owned_projectile_broadcasts_packet29()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(603);
        var outbound = CreateQueue();
        Assert.True(replication.TryRegister(source, outbound));
        MarkPlaying(replication, source, playerSlot: 4);

        var store = new RuntimeProjectileStore(capacity: 4, commitSink: replication);
        ProjectileStateUpdate state = CreateProjectile(VanillaProjectileOwnership.ServerOwner);
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);

        var executor = new RuntimeProjectileStateExecutor(store);
        ProjectileStateTickSummary summary = executor.Tick(new ExpireStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.False(replication.WireIdentities.TryGetWireKey(spawned.Handle, out _));
    }

    private static TerrariaConnectionOutboundQueue CreateQueue() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static void MarkPlaying(
        RuntimeProjectileReplicationRegistry replication,
        GameCommandSourceId source,
        byte playerSlot)
    {
        var connection = new ConnectionHandle(
            source,
            new PlayerHandle(new PlayerSlotId(playerSlot), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in spawn);
    }

    private static ProjectileStateUpdate CreateProjectile(byte spawner) =>
        new(
            Type: new ProjectileTypeId(1122),
            Spawner: spawner,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 1f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);

    private sealed class ExpireStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            var state = new ProjectileStateUpdate(
                current.Type,
                current.Spawner,
                current.PositionX + current.VelocityX,
                current.PositionY + current.VelocityY,
                current.VelocityX,
                current.VelocityY,
                current.Ai,
                current.BannerIdToRespondTo,
                current.Damage,
                current.KnockBack,
                current.OriginalDamage);
            next = new ProjectileSimulationStepResult(state, 0);
            return true;
        }
    }
}
