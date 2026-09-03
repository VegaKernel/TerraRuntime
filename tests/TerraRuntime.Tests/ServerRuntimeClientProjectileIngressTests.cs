using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeClientProjectileIngressTests
{
    [Fact]
    public void Unknown_packet27_allocates_first_physical_slot_and_exact_key_updates_same_handle()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 1);
        TerrariaProjectileUpdateState first = CreateUpdate(source.Player.Slot.Value, index: 777, generation: 9, type: 1, positionX: 100f);
        TerrariaProjectileKeyState firstKey = first.Key;

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, first));

        Assert.Equal(1, fixture.State.AppliedProjectileSpawns);
        Assert.Equal(0, fixture.State.RejectedClientProjectileUpdates);
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in firstKey, out ProjectileHandle handle));
        Assert.Equal((ushort)0, handle.Slot);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot created));
        Assert.Equal(100f, created.PositionX);
        Assert.Equal(new ProjectileRevision(1), created.Revision);

        TerrariaProjectileUpdateState moved = first with { PositionX = 150f };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, moved));

        Assert.Equal(1, fixture.State.AppliedProjectileSpawns);
        Assert.Equal(1, fixture.State.AppliedProjectileUpdates);
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in firstKey, out ProjectileHandle same));
        Assert.Equal(handle, same);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot updated));
        Assert.Equal(150f, updated.PositionX);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
    }

    [Fact]
    public void Existing_client_projectile_cannot_rewrite_combat_identity_or_damage()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 11);
        TerrariaProjectileUpdateState first = CreateUpdate(source.Player.Slot.Value, 91, 1, type: 1, positionX: 100f);
        TerrariaProjectileKeyState key = first.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, first));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in key, out ProjectileHandle handle));
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot before));

        TerrariaProjectileUpdateState forged = first with
        {
            ProjectileType = 3,
            Damage = 30_000,
            OriginalDamage = 30_000,
            KnockBack = 99f,
            PositionX = 150f
        };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, forged));

        Assert.Equal(1, fixture.State.RejectedClientProjectileUpdates);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot after));
        Assert.Equal(before.Type, after.Type);
        Assert.Equal(before.Damage, after.Damage);
        Assert.Equal(before.OriginalDamage, after.OriginalDamage);
        Assert.Equal(before.KnockBack, after.KnockBack);
        Assert.Equal(before.PositionX, after.PositionX);
    }

    [Fact]
    public void New_generation_for_same_wire_index_shadows_lookup_but_preserves_old_reverse_identity()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 2);
        TerrariaProjectileUpdateState first = CreateUpdate(source.Player.Slot.Value, index: 88, generation: 1, type: 1, positionX: 10f);
        TerrariaProjectileKeyState firstKey = first.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, first));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in firstKey, out ProjectileHandle oldHandle));

        TerrariaProjectileUpdateState replacement = first with
        {
            Key = new TerrariaProjectileKeyState(source.Player.Slot.Value, 88, 2),
            PositionX = 20f
        };
        TerrariaProjectileKeyState replacementKey = replacement.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, replacement));

        Assert.True(fixture.Replication.WireIdentities.TryResolve(in replacementKey, out ProjectileHandle newHandle));
        Assert.Equal((ushort)1, newHandle.Slot);
        Assert.NotEqual(oldHandle, newHandle);
        Assert.False(fixture.Replication.WireIdentities.TryResolve(in firstKey, out _));
        Assert.True(fixture.Replication.WireIdentities.TryGetWireKey(oldHandle, out TerrariaProjectileKeyState retainedOld));
        Assert.Equal(firstKey, retainedOld);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(oldHandle, out _));
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(newHandle, out _));
    }

    [Fact]
    public void Authoritative_state_rejects_hostile_and_foreign_spawner_even_if_frame_sink_is_bypassed()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 3);
        TerrariaProjectileUpdateState hostile = CreateUpdate(source.Player.Slot.Value, 1, 1, type: 31, positionX: 10f);
        TerrariaProjectileUpdateState foreign = CreateUpdate(checked((byte)(source.Player.Slot.Value + 1)), 2, 1, type: 1, positionX: 20f);

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, hostile));
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, foreign));

        Assert.Equal(0, fixture.State.AppliedProjectileSpawns);
        Assert.Equal(2, fixture.State.RejectedClientProjectileUpdates);
        Assert.Equal(0, fixture.Projectiles.ActiveCount);
    }

    [Fact]
    public void Trusted_server_projectile_rejects_owner_packet27_and_packet29_mutations()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 12);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new ProjectileStateUpdate(
            VanillaProjectileIds.WoodenArrowFriendly,
            owner.Player.Slot.Value,
            100f,
            200f,
            6f,
            0f,
            default,
            0,
            25,
            2f,
            25);

        fixture.State.Apply(new ProjectileSpawnRuntimeCommand(0, spawn, completion));
        ProjectileSnapshot? trustedResult = completion.Task.GetAwaiter().GetResult();
        Assert.True(trustedResult.HasValue);
        ProjectileSnapshot trusted = trustedResult.Value;
        Assert.True(fixture.Projectiles.IsCombatTrusted(trusted.Handle));
        Assert.True(fixture.Replication.WireIdentities.TryGetWireKey(trusted.Handle, out TerrariaProjectileKeyState key));

        var forgedUpdate = new TerrariaProjectileUpdateState(
            key,
            trusted.Type.Value,
            900f,
            900f,
            120f,
            -80f,
            99f,
            98f,
            97f,
            trusted.BannerIdToRespondTo,
            trusted.Damage,
            trusted.KnockBack,
            trusted.OriginalDamage);
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedUpdate));

        Assert.Equal(1, fixture.State.RejectedTrustedClientProjectileUpdates);
        Assert.Equal(1, fixture.State.RejectedClientProjectileUpdates);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(trusted.Handle, out ProjectileSnapshot afterUpdate));
        Assert.Equal(trusted.PositionX, afterUpdate.PositionX);
        Assert.Equal(trusted.PositionY, afterUpdate.PositionY);
        Assert.Equal(trusted.VelocityX, afterUpdate.VelocityX);
        Assert.Equal(trusted.VelocityY, afterUpdate.VelocityY);
        Assert.Equal(trusted.Ai, afterUpdate.Ai);

        var forgedDestroy = new TerrariaProjectileDestroyState(key, 777f, 888f);
        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(owner, forgedDestroy));

        Assert.Equal(1, fixture.State.RejectedTrustedClientProjectileDestroys);
        Assert.Equal(1, fixture.State.RejectedClientProjectileDestroys);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(trusted.Handle, out ProjectileSnapshot afterDestroy));
        Assert.Equal(afterUpdate, afterDestroy);
    }

    [Fact]
    public void Unknown_packet29_relays_to_playing_peer_without_local_mutation_or_sender_echo()
    {
        using var fixture = new Fixture(playerCount: 2);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 4);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 5);
        TerrariaConnectionOutboundQueue sourceOutbound = fixture.Outbound(source.Source);
        TerrariaConnectionOutboundQueue peerOutbound = fixture.Outbound(peer.Source);
        var destroy = new TerrariaProjectileDestroyState(
            new TerrariaProjectileKeyState(source.Player.Slot.Value, 999, 7),
            300f,
            400f);

        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(source, destroy));

        Assert.Equal(0, fixture.Projectiles.ActiveCount);
        Assert.Equal(1, fixture.State.RelayedUnknownProjectileDestroys);
        Assert.Equal(0, fixture.State.RejectedClientProjectileDestroys);
        Assert.Equal(0, sourceOutbound.QueuedFrames);
        Assert.Equal(1, peerOutbound.QueuedFrames);
    }

    [Fact]
    public void Resolved_packet29_requires_owner_and_applies_final_position_before_despawn()
    {
        using var fixture = new Fixture(playerCount: 2);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 6);
        ConnectionHandle foreign = fixture.SpawnPlayer(connectionId: 7);
        TerrariaProjectileUpdateState spawn = CreateUpdate(owner.Player.Slot.Value, 123, 1, type: 1, positionX: 10f);
        TerrariaProjectileKeyState spawnKey = spawn.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, spawn));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in spawnKey, out ProjectileHandle handle));

        var destroy = new TerrariaProjectileDestroyState(spawnKey, 500f, 600f);
        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(foreign, destroy));

        Assert.Equal(1, fixture.State.RejectedClientProjectileDestroys);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot alive));
        Assert.Equal(10f, alive.PositionX);

        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(owner, destroy));

        Assert.Equal(1, fixture.State.AppliedProjectileDespawns);
        Assert.False(fixture.State.TryCaptureProjectileSnapshot(handle, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(in spawnKey, out _));
    }

    private static TerrariaProjectileUpdateState CreateUpdate(
        byte spawner,
        ushort index,
        ushort generation,
        int type,
        float positionX) =>
        new(
            new TerrariaProjectileKeyState(spawner, index, generation),
            type,
            positionX,
            200f,
            2f,
            -1f,
            0f,
            0f,
            0f,
            0,
            25,
            2f,
            25);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots;
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];

        public Fixture(int playerCount)
        {
            slots = new PlayerSlotPool(playerCount);
            Replication = new RuntimeProjectileReplicationRegistry();
            Projectiles = new RuntimeProjectileStore(capacity: 8, commitSink: Replication);
            State = new ServerRuntimeState(
                playerEvents: Replication,
                projectiles: Projectiles,
                projectileReplication: Replication);
        }

        public RuntimeProjectileReplicationRegistry Replication { get; }
        public RuntimeProjectileStore Projectiles { get; }
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

            GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
            var queue = new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 16_384, maxFrameBytes: 2_048));
            Assert.True(Replication.TryRegister(source, queue));
            outbound.Add(source, queue);

            var connection = new ConnectionHandle(source, session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public TerrariaConnectionOutboundQueue Outbound(GameCommandSourceId source) => outbound[source];

        public void Dispose()
        {
            foreach (KeyValuePair<GameCommandSourceId, TerrariaConnectionOutboundQueue> pair in outbound)
                Replication.TryUnregister(pair.Key);
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }
}
