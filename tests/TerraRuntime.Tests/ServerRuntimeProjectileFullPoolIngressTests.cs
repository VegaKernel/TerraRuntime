using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeProjectileFullPoolIngressTests
{
    [Fact]
    public void Unknown_packet27_replaces_vanilla_oldest_slot_and_rebinds_exact_wire_key()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        var projectiles = new RuntimeProjectileStore(commitSink: replication);
        var state = new ServerRuntimeState(
            playerEvents: replication,
            projectiles: projectiles,
            projectileReplication: replication);

        ProjectileHandle displaced = default;
        TerrariaProjectileKeyState displacedKey = default;
        for (ushort slot = 0; slot < RuntimeProjectileStore.VanillaPhysicalSlotCount; slot++)
        {
            ProjectileStateUpdate fill = CreateRuntimeUpdate(
                type: slot == 123 ? 1122 : 3,
                spawner: 3,
                positionX: slot);
            Assert.True(projectiles.TrySpawn(slot, in fill, out ProjectileSnapshot spawned));
            if (slot != 123)
                continue;

            displaced = spawned.Handle;
            Assert.True(replication.WireIdentities.TryGetWireKey(displaced, out displacedKey));
        }

        Assert.Equal(RuntimeProjectileStore.VanillaPhysicalSlotCount, projectiles.ActiveCount);
        Assert.Equal((ushort)123, displaced.Slot);
        Assert.Equal((ulong)1, displaced.Generation.Value);

        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

        GameCommandSourceId sourceId = GameCommandSourceId.FromConnection(9001);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(
                maxFrames: 2_048,
                maxQueuedBytes: 4 * 1024 * 1024,
                maxFrameBytes: 2_048));
        Assert.True(replication.TryRegister(sourceId, outbound));
        try
        {
            var connection = new ConnectionHandle(sourceId, session.Handle);
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
            Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);

            var packet = new TerrariaProjectileUpdateState(
                new TerrariaProjectileKeyState(session.Slot.Value, 777, 9),
                ProjectileType: 1,
                PositionX: 9000f,
                PositionY: 200f,
                VelocityX: 2f,
                VelocityY: -1f,
                Ai0: 0f,
                Ai1: 0f,
                Ai2: 0f,
                BannerIdToRespondTo: 0,
                Damage: 25,
                KnockBack: 2f,
                OriginalDamage: 25);
            TerrariaProjectileKeyState newKey = packet.Key;

            state.Apply(new ClientProjectileUpdateRuntimeCommand(connection, packet));

            Assert.Equal(1, state.AppliedProjectileSpawns);
            Assert.Equal(0, state.RejectedClientProjectileUpdates);
            Assert.True(replication.WireIdentities.TryResolve(in newKey, out ProjectileHandle replacement));
            Assert.Equal((ushort)123, replacement.Slot);
            Assert.Equal((ulong)2, replacement.Generation.Value);
            Assert.False(projectiles.TryGet(displaced, out _));
            Assert.False(replication.WireIdentities.TryResolve(in displacedKey, out _));
            Assert.True(projectiles.TryGet(replacement, out ProjectileSnapshot snapshot));
            Assert.Equal(9000f, snapshot.PositionX);
            Assert.Equal(new ProjectileTypeId(1), snapshot.Type);
            Assert.Equal(RuntimeProjectileStore.VanillaPhysicalSlotCount, projectiles.ActiveCount);
        }
        finally
        {
            replication.TryUnregister(sourceId);
        }
    }

    private static ProjectileStateUpdate CreateRuntimeUpdate(
        int type,
        byte spawner,
        float positionX) =>
        new(
            Type: new ProjectileTypeId(type),
            Spawner: spawner,
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 1,
            KnockBack: 0f,
            OriginalDamage: 1);
}
