using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileCatalogReplicationTests
{
    [Fact]
    public void Unknown_vanilla_projectile_type_is_neither_broadcast_nor_retained_as_baseline()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(41);
        TerrariaConnectionOutboundQueue firstOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(firstSource, firstOutbound));
        ConnectionHandle firstPlayer = Connection(firstSource, slot: 1);
        PlayerSpawnCommitRequest firstSpawn = CreatePlayerSpawn(firstPlayer.Player.Slot);
        replication.PlayerSpawned(firstPlayer, in firstSpawn);

        ProjectileSnapshot unsupported = CreateProjectile(new ProjectileTypeId(VanillaProjectileIds.Count));
        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in unsupported);

        Assert.Equal(0, firstOutbound.QueuedFrames);
        Assert.Equal(0, replication.RelayedFrames);
        Assert.Equal(1, replication.UnsupportedCommits);

        GameCommandSourceId lateSource = GameCommandSourceId.FromConnection(42);
        TerrariaConnectionOutboundQueue lateOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(lateSource, lateOutbound));
        ConnectionHandle latePlayer = Connection(lateSource, slot: 2);
        PlayerSpawnCommitRequest lateSpawn = CreatePlayerSpawn(latePlayer.Player.Slot);
        replication.PlayerSpawned(latePlayer, in lateSpawn);

        Assert.Equal(0, lateOutbound.QueuedFrames);
        Assert.Equal(0, replication.BaselineFrames);
        Assert.Equal(1, replication.UnsupportedCommits);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, byte slot) =>
        new(
            source,
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(1)));

    private static PlayerSpawnCommitRequest CreatePlayerSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static ProjectileSnapshot CreateProjectile(ProjectileTypeId type) =>
        new(
            Handle: new ProjectileHandle(7, new ProjectileGeneration(1)),
            Revision: new ProjectileRevision(1),
            Type: type,
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
