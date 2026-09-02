using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime;

internal static class AuthoritativePlayerSpawnSmoke
{
    public static bool Run(out string failure)
    {
        failure = string.Empty;
        var interestManagement = new InterestManagementControl(enabled: true);
        var registry = new RuntimeConnectionRegistry(interestManagement);
        var source1 = GameCommandSourceId.FromConnection(1);
        var source2 = GameCommandSourceId.FromConnection(2);
        var outbound1 = CreateOutbound();
        var outbound2 = CreateOutbound();
        if (!registry.TryRegister(source1, outbound1) || !registry.TryRegister(source2, outbound2))
        {
            failure = "failed to register runtime connection endpoints";
            return false;
        }

        if (!interestManagement.IsEnabled)
        {
            failure = "interest-management routing was not enabled for the authoritative relay smoke";
            return false;
        }

        var state = new ServerRuntimeState(registry);
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var commandIngress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(loop);
        var spawnIngress = new RuntimePlayerSpawnCommitIngress(commandIngress);
        var movementIngress = new RuntimePlayerMovementIngress(commandIngress);
        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);
        var slots = new PlayerSlotPool(2);

        if (!TryCreateAwaitingSpawnSession(slots, out PlayerJoinSession? session1) || session1 is null)
        {
            failure = "failed to prepare first player spawn session";
            return false;
        }

        if (!TryCreateAwaitingSpawnSession(slots, out PlayerJoinSession? session2) || session2 is null)
        {
            session1.Dispose();
            failure = "failed to prepare second player spawn session";
            return false;
        }

        using (session1)
        using (session2)
        {
            loop.Start();
            PlayerSpawnCommitRequest spawn1 = CreateSpawnRequest(session1.Slot, 100, 200);
            PlayerSpawnCommitRequest spawn2 = CreateSpawnRequest(session2.Slot, 120, 220);
            var connection1 = new ConnectionHandle(source1, session1.Handle);

            if (!spawnIngress.TryPost(source1, session1, in spawn1) ||
                !spawnIngress.TryPost(source2, session2, in spawn2))
            {
                failure = "authoritative spawn ingress rejected command";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            DateTime spawnDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while ((session1.State != PlayerJoinState.Playing ||
                    session2.State != PlayerJoinState.Playing ||
                    state.CommittedPlayerSpawns != 2 ||
                    outbound1.QueuedFrames != 1 ||
                    outbound2.QueuedFrames != 1 ||
                    registry.PlayerActiveBaselineFrames != 2) &&
                   DateTime.UtcNow < spawnDeadline)
            {
                Thread.Sleep(5);
            }

            if (session1.State != PlayerJoinState.Playing ||
                session2.State != PlayerJoinState.Playing ||
                state.CommittedPlayerSpawns != 2)
            {
                failure = $"spawn commits did not reach playing state: first={session1.State}, second={session2.State}, count={state.CommittedPlayerSpawns}";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            int senderBaselineFrames = outbound1.QueuedFrames;
            int peerBaselineFrames = outbound2.QueuedFrames;
            if (senderBaselineFrames != 1 ||
                peerBaselineFrames != 1 ||
                registry.PlayerActiveBaselineFrames != 2)
            {
                failure = $"player-active baseline mismatch: sender={senderBaselineFrames}, peer={peerBaselineFrames}, active={registry.PlayerActiveBaselineFrames}";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            PlayerMovementCommitRequest movement = CreateMovementRequest(session1.Slot, 123.5f, 456.25f);
            if (!movementIngress.TryPost(connection1, in movement))
            {
                failure = "authoritative movement ingress rejected command";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            DateTime movementDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while ((state.AppliedPlayerMovements != 1 || registry.RelayedMovementFrames != 1) &&
                   DateTime.UtcNow < movementDeadline)
            {
                Thread.Sleep(5);
            }

            if (state.AppliedPlayerMovements != 1 ||
                registry.RelayedMovementFrames != 1 ||
                outbound1.QueuedFrames != senderBaselineFrames ||
                outbound2.QueuedFrames != peerBaselineFrames + 1)
            {
                failure = $"movement relay mismatch with interest management enabled: applied={state.AppliedPlayerMovements}, relayed={registry.RelayedMovementFrames}, senderQueued={outbound1.QueuedFrames}, peerQueued={outbound2.QueuedFrames}";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            if (!disconnectIngress.TryPost(connection1))
            {
                failure = "authoritative disconnect ingress rejected command";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            DateTime disconnectDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (state.DisconnectedPlayers != 1 && DateTime.UtcNow < disconnectDeadline)
                Thread.Sleep(5);

            if (state.DisconnectedPlayers != 1)
            {
                failure = "authoritative disconnect did not remove the player";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            if (!movementIngress.TryPost(connection1, in movement))
            {
                failure = "post-disconnect movement could not enter bounded ingress for rejection test";
                loop.Stop(TimeSpan.FromSeconds(1));
                return false;
            }

            DateTime rejectionDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (state.RejectedPlayerMovements != 1 && DateTime.UtcNow < rejectionDeadline)
                Thread.Sleep(5);

            bool stopped = loop.Stop(TimeSpan.FromSeconds(1));
            registry.TryUnregister(source1, out _);
            registry.TryUnregister(source2, out _);

            if (!stopped || loop.Fault is not null)
            {
                failure = $"game loop failed to stop cleanly: fault={loop.Fault}";
                return false;
            }

            if (state.RejectedPlayerMovements != 1 ||
                registry.PlayerDeactivationFrames != 1 ||
                outbound2.QueuedFrames != peerBaselineFrames + 2)
            {
                failure = $"post-disconnect movement was not rejected cleanly: rejected={state.RejectedPlayerMovements}, peerQueued={outbound2.QueuedFrames}";
                return false;
            }
        }

        return true;
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static bool TryCreateAwaitingSpawnSession(PlayerSlotPool slots, out PlayerJoinSession? session)
    {
        session = null;
        if (!slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease) || lease is null)
            return false;

        var candidate = new PlayerJoinSession(lease);
        if (candidate.ObserveWorldRequest() != PlayerJoinTransition.WorldRequestAccepted ||
            candidate.ObserveSectionRequest() != PlayerJoinTransition.SectionRequestAccepted ||
            candidate.State != PlayerJoinState.AwaitingSpawn)
        {
            candidate.Dispose();
            return false;
        }

        session = candidate;
        return true;
    }

    private static PlayerSpawnCommitRequest CreateSpawnRequest(PlayerSlotId slot, short spawnX, short spawnY) =>
        new(
            slot,
            SpawnX: spawnX,
            SpawnY: spawnY,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);

    private static PlayerMovementCommitRequest CreateMovementRequest(PlayerSlotId slot, float x, float y) =>
        new(
            slot,
            ControlFlags: 0x03,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 4,
            PositionX: x,
            PositionY: y,
            HasVelocity: false,
            VelocityX: 0f,
            VelocityY: 0f,
            HasMount: false,
            MountType: 0,
            HasPotionOfReturnPositions: false,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            HasCameraTarget: false,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
}
