using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileExactWireReplicationTests
{
    [Fact]
    public void Preserved_wire_key_survives_spawn_and_update_and_is_released_on_despawn()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        var replication = new RuntimeProjectileReplicationRegistry(identities);
        ProjectileSnapshot projectile = CreateProjectile(revision: 1);
        var exactKey = new TerrariaProjectileKeyState(
            Spawner: projectile.Spawner,
            ProjectileIndex: 777,
            Generation: 1234);
        Assert.True(identities.TryBind(in exactKey, projectile.Handle));

        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in projectile);
        ProjectileSnapshot moved = projectile with
        {
            Revision = new ProjectileRevision(2),
            PositionX = 130f
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in moved);

        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
        Assert.True(identities.TryGetWireKey(projectile.Handle, out TerrariaProjectileKeyState retained));
        Assert.Equal(exactKey, retained);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in moved);

        Assert.Equal(3, outbound.QueuedFrames);
        Assert.Equal(3, replication.RelayedFrames);
        Assert.False(identities.TryGetWireKey(projectile.Handle, out _));
        Assert.False(identities.TryResolve(in exactKey, out _));
    }

    [Fact]
    public void Runtime_created_projectile_gets_canonical_wire_identity_once_and_reuses_it()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        var replication = new RuntimeProjectileReplicationRegistry(identities);
        ProjectileSnapshot projectile = CreateProjectile(revision: 1);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in projectile);

        Assert.True(identities.TryGetWireKey(projectile.Handle, out TerrariaProjectileKeyState canonical));
        Assert.Equal(projectile.Spawner, canonical.Spawner);
        Assert.Equal(projectile.Handle.Slot, canonical.ProjectileIndex);
        Assert.Equal(
            RuntimeProjectilePacketProjection.ToProtocolGeneration(projectile.Handle.Generation),
            canonical.Generation);

        ProjectileSnapshot moved = projectile with
        {
            Revision = new ProjectileRevision(2),
            PositionX = 140f
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in moved);

        Assert.True(identities.TryGetWireKey(projectile.Handle, out TerrariaProjectileKeyState retained));
        Assert.Equal(canonical, retained);
        Assert.Equal(0, replication.UnsupportedCommits);
    }

    [Fact]
    public void Preserved_wire_key_with_different_spawner_is_not_relayed()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        var replication = new RuntimeProjectileReplicationRegistry(identities);
        ProjectileSnapshot projectile = CreateProjectile(revision: 1);
        var wrongSpawnerKey = new TerrariaProjectileKeyState(
            Spawner: (byte)(projectile.Spawner + 1),
            ProjectileIndex: 777,
            Generation: 1234);
        Assert.True(identities.TryBind(in wrongSpawnerKey, projectile.Handle));

        GameCommandSourceId source = GameCommandSourceId.FromConnection(3);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 3, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in projectile);

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(0, replication.RelayedFrames);
        Assert.Equal(1, replication.UnsupportedCommits);
    }

    [Fact]
    public void Invalid_despawn_still_releases_stale_reverse_identity_and_baseline()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        var replication = new RuntimeProjectileReplicationRegistry(identities);
        ProjectileSnapshot projectile = CreateProjectile(revision: 1);
        var exactKey = new TerrariaProjectileKeyState(
            Spawner: projectile.Spawner,
            ProjectileIndex: 555,
            Generation: 44);
        Assert.True(identities.TryBind(in exactKey, projectile.Handle));
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in projectile);

        ProjectileSnapshot invalidFinal = projectile with
        {
            PositionX = float.NaN
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in invalidFinal);

        Assert.Equal(1, replication.UnsupportedCommits);
        Assert.False(identities.TryGetWireKey(projectile.Handle, out _));
        Assert.False(identities.TryResolve(in exactKey, out _));

        GameCommandSourceId source = GameCommandSourceId.FromConnection(2);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 2, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);
        Assert.Equal(0, outbound.QueuedFrames);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

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

    private static ProjectileSnapshot CreateProjectile(ulong revision) =>
        new(
            Handle: new ProjectileHandle(7, new ProjectileGeneration(1)),
            Revision: new ProjectileRevision(revision),
            Type: new ProjectileTypeId(14),
            Spawner: 4,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: -2f,
            Ai: new ProjectileAiState(1f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 25,
            KnockBack: 2f,
            OriginalDamage: 25);
}
