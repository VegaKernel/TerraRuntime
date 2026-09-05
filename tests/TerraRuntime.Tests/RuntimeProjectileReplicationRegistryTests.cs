using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileReplicationRegistryTests
{
    [Fact]
    public void Existing_projectile_is_sent_as_baseline_only_after_player_enters_playing_state()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ProjectileSnapshot projectile = CreateProjectile(revision: 3, positionX: 120f);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in projectile);
        Assert.Equal(0, outbound.QueuedFrames);

        ConnectionHandle player = Connection(source, slot: 4, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.BaselineFrames);
        Assert.Equal(0, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Mismatched_spawn_claim_does_not_mark_connection_as_playing()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));

        ConnectionHandle player = Connection(source, slot: 4, generation: 1);
        PlayerSpawnCommitRequest mismatchedSpawn = CreatePlayerSpawn(new PlayerSlotId(5));
        replication.PlayerSpawned(player, in mismatchedSpawn);

        ProjectileSnapshot projectile = CreateProjectile(revision: 1, positionX: 100f);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in projectile);

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(0, replication.RelayedFrames);
        Assert.Equal(0, replication.BaselineFrames);
    }

    [Fact]
    public void Playing_client_receives_spawn_update_and_despawn_and_despawn_clears_future_baseline()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue firstOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(firstSource, firstOutbound));
        ConnectionHandle first = Connection(firstSource, slot: 1, generation: 1);
        PlayerSpawnCommitRequest firstSpawn = CreatePlayerSpawn(first.Player.Slot);
        replication.PlayerSpawned(first, in firstSpawn);

        ProjectileSnapshot projectile = CreateProjectile(revision: 1, positionX: 100f);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in projectile);
        Assert.Equal(1, firstOutbound.QueuedFrames);

        ProjectileSnapshot moved = projectile with
        {
            Revision = new ProjectileRevision(2),
            PositionX = 130f
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in moved);
        Assert.Equal(2, firstOutbound.QueuedFrames);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in moved);
        Assert.Equal(3, firstOutbound.QueuedFrames);

        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        TerrariaConnectionOutboundQueue secondOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(secondSource, secondOutbound));
        ConnectionHandle second = Connection(secondSource, slot: 2, generation: 1);
        PlayerSpawnCommitRequest secondSpawn = CreatePlayerSpawn(second.Player.Slot);
        replication.PlayerSpawned(second, in secondSpawn);

        Assert.Equal(0, secondOutbound.QueuedFrames);
        Assert.Equal(3, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Identical_projectile_wire_update_is_not_relayed_twice_for_same_generation()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        ProjectileSnapshot first = CreateProjectile(revision: 1, positionX: 100f);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in first);
        ProjectileSnapshot sameWireState = first with { Revision = new ProjectileRevision(2) };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in sameWireState);

        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);
        Assert.Equal(1, replication.SuppressedDuplicateFrames);
    }

    [Fact]
    public void Identical_projectile_wire_update_is_not_suppressed_after_slot_generation_changes()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        ProjectileSnapshot first = CreateProjectile(revision: 1, positionX: 100f);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in first);
        ProjectileSnapshot replacement = first with
        {
            Handle = new ProjectileHandle(first.Handle.Slot, new ProjectileGeneration(16384)),
            Revision = new ProjectileRevision(1)
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in replacement);

        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.Equal(0, replication.SuppressedDuplicateFrames);
    }

    [Fact]
    public void Reused_projectile_slot_replaces_baseline_with_new_generation()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        ProjectileSnapshot first = CreateProjectile(revision: 1, positionX: 100f);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in first);
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in first);

        ProjectileSnapshot replacement = first with
        {
            Handle = new ProjectileHandle(first.Handle.Slot, new ProjectileGeneration(2)),
            Revision = new ProjectileRevision(1),
            PositionX = 200f
        };
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in replacement);

        GameCommandSourceId source = GameCommandSourceId.FromConnection(3);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 3, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.BaselineFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
    }

    [Fact]
    public void Invalid_snapshot_is_not_put_on_the_wire()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);
        ProjectileSnapshot unsupported = CreateProjectile(revision: 1, positionX: float.NaN);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in unsupported);

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(1, replication.UnsupportedCommits);
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
