using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileClientCommitRoutingTests
{
    [Fact]
    public void Packet29_final_position_despawn_is_atomic_and_emits_no_update_commit()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(capacity: 8, commitSink: sink);
        ProjectileStateUpdate state = CreateState(spawner: 4, positionX: 10f);
        Assert.True(store.TrySpawn(3, in state, out ProjectileSnapshot created));

        Assert.True(store.TryDespawnAt(created.Handle, 321f, 654f, out ProjectileSnapshot final));

        Assert.Equal(321f, final.PositionX);
        Assert.Equal(654f, final.PositionY);
        Assert.Equal(created.Revision, final.Revision);
        Assert.Equal(2, sink.Commits.Count);
        Assert.Equal(ProjectileStateCommitKind.Spawn, sink.Commits[0].Kind);
        Assert.Equal(ProjectileStateCommitKind.Despawn, sink.Commits[1].Kind);
        Assert.Equal(final, sink.Commits[1].Snapshot);
        Assert.False(store.TryGet(created.Handle, out _));
    }

    [Fact]
    public void Packet29_final_position_rejects_nonfinite_or_stale_handles_without_mutation()
    {
        var store = new RuntimeProjectileStore(capacity: 8);
        ProjectileStateUpdate state = CreateState(spawner: 4, positionX: 10f);
        Assert.True(store.TrySpawn(3, in state, out ProjectileSnapshot created));

        Assert.False(store.TryDespawnAt(created.Handle, float.NaN, 20f, out _));
        Assert.True(store.TryGet(created.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(created, unchanged);

        var stale = new ProjectileHandle(created.Handle.Slot, new ProjectileGeneration(99));
        Assert.False(store.TryDespawnAt(stale, 20f, 30f, out _));
        Assert.True(store.TryGet(created.Handle, out unchanged));
        Assert.Equal(created, unchanged);
    }

    [Fact]
    public void Vanilla_physical_allocation_range_is_distinct_from_protocol_key_range()
    {
        Assert.Equal((ushort)999, RuntimeProjectileStore.MaximumVanillaPhysicalSlot);
        Assert.Equal(1000, RuntimeProjectileStore.VanillaPhysicalSlotCount);
        Assert.Equal((ushort)1000, RuntimeProjectileStore.MaximumProtocolIndex);
        Assert.Equal(1001, RuntimeProjectileStore.MaximumProtocolAddressableCapacity);
    }

    [Fact]
    public void Client_spawn_update_and_despawn_preserve_exact_key_and_never_echo_to_source()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry(runtimeCapacity: 8);
        var clientCommits = new RuntimeProjectileClientCommitContext();
        var replication = new RuntimeProjectileReplicationRegistry(identities, clientCommits);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId peer = GameCommandSourceId.FromConnection(2);
        TerrariaConnectionOutboundQueue sourceOutbound = CreateOutbound();
        TerrariaConnectionOutboundQueue peerOutbound = CreateOutbound();
        RegisterPlaying(replication, source, sourceOutbound, playerSlot: 4);
        RegisterPlaying(replication, peer, peerOutbound, playerSlot: 5);
        var store = new RuntimeProjectileStore(capacity: 8, commitSink: replication);
        var key = new TerrariaProjectileKeyState(Spawner: 4, ProjectileIndex: 777, Generation: 1234);
        ProjectileStateUpdate state = CreateState(spawner: 4, positionX: 10f);

        using (clientCommits.Enter(source, in key))
            Assert.True(store.TrySpawn(3, in state, out _));

        Assert.True(identities.TryResolve(in key, out ProjectileHandle handle));
        Assert.Equal((ushort)3, handle.Slot);
        Assert.Equal(0, sourceOutbound.QueuedFrames);
        Assert.Equal(1, peerOutbound.QueuedFrames);

        ProjectileStateUpdate moved = state with { PositionX = 20f };
        ProjectileSnapshot updated;
        using (clientCommits.Enter(source, in key))
            Assert.True(store.TryUpdate(handle, in moved, out updated));

        Assert.Equal(20f, updated.PositionX);
        Assert.Equal(0, sourceOutbound.QueuedFrames);
        Assert.Equal(2, peerOutbound.QueuedFrames);

        ProjectileSnapshot final;
        using (clientCommits.Enter(source, in key))
            Assert.True(store.TryDespawnAt(handle, 30f, 40f, out final));

        Assert.Equal(30f, final.PositionX);
        Assert.Equal(40f, final.PositionY);
        Assert.Equal(0, sourceOutbound.QueuedFrames);
        Assert.Equal(3, peerOutbound.QueuedFrames);
        Assert.False(identities.TryResolve(in key, out _));
        Assert.Equal(3, replication.RelayedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
        Assert.False(clientCommits.TryGet(out _, out _));
    }

    [Fact]
    public void Unknown_destroy_is_relayed_to_playing_peers_but_not_sender()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId peer = GameCommandSourceId.FromConnection(2);
        TerrariaConnectionOutboundQueue sourceOutbound = CreateOutbound();
        TerrariaConnectionOutboundQueue peerOutbound = CreateOutbound();
        RegisterPlaying(replication, source, sourceOutbound, playerSlot: 4);
        RegisterPlaying(replication, peer, peerOutbound, playerSlot: 5);
        var destroy = new TerrariaProjectileDestroyState(
            new TerrariaProjectileKeyState(Spawner: 4, ProjectileIndex: 888, Generation: 7),
            PositionX: 100f,
            PositionY: 200f);

        Assert.True(replication.TryRelayUnresolvedDestroy(source, in destroy));

        Assert.Equal(0, sourceOutbound.QueuedFrames);
        Assert.Equal(1, peerOutbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);
    }

    [Fact]
    public void Client_commit_key_spawner_must_match_authoritative_snapshot_spawner()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry(runtimeCapacity: 8);
        var clientCommits = new RuntimeProjectileClientCommitContext();
        var replication = new RuntimeProjectileReplicationRegistry(identities, clientCommits);
        var store = new RuntimeProjectileStore(capacity: 8, commitSink: replication);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        var foreignKey = new TerrariaProjectileKeyState(Spawner: 5, ProjectileIndex: 100, Generation: 1);
        ProjectileStateUpdate state = CreateState(spawner: 4, positionX: 10f);
        ProjectileSnapshot created;

        using (clientCommits.Enter(source, in foreignKey))
            Assert.True(store.TrySpawn(2, in state, out created));

        Assert.False(identities.TryResolve(in foreignKey, out _));
        Assert.Equal(1, replication.UnsupportedCommits);
        Assert.True(store.TryGet(created.Handle, out _));
    }

    private static ProjectileStateUpdate CreateState(byte spawner, float positionX) =>
        new(
            Type: VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: spawner,
            PositionX: positionX,
            PositionY: 50f,
            VelocityX: 2f,
            VelocityY: -1f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 25,
            KnockBack: 2f,
            OriginalDamage: 25);

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static void RegisterPlaying(
        RuntimeProjectileReplicationRegistry replication,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        byte playerSlot)
    {
        Assert.True(replication.TryRegister(source, outbound));
        var player = new PlayerHandle(new PlayerSlotId(playerSlot), new PlayerSessionGeneration(1));
        var connection = new ConnectionHandle(source, player);
        var request = new PlayerSpawnCommitRequest(player.Slot, 10, 10, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in request);
    }

    private sealed class RecordingCommitSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
