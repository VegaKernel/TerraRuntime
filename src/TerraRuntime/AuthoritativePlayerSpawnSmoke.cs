using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal static class AuthoritativePlayerSpawnSmoke
{
    public static bool Run(out string failure)
    {
        failure = string.Empty;
        var state = new ServerRuntimeState();
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var commandIngress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(loop);
        var spawnIngress = new RuntimePlayerSpawnCommitIngress(commandIngress);
        var slots = new PlayerSlotPool(1);

        if (!slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease) || lease is null)
        {
            failure = "failed to acquire player slot";
            return false;
        }

        using var session = new PlayerJoinSession(lease);
        if (session.ObserveWorldRequest() != PlayerJoinTransition.WorldRequestAccepted ||
            session.ObserveSectionRequest() != PlayerJoinTransition.SectionRequestAccepted ||
            session.State != PlayerJoinState.AwaitingSpawn)
        {
            failure = "failed to prepare join state 3";
            return false;
        }

        loop.Start();
        var request = new PlayerSpawnCommitRequest(
            session.Slot,
            SpawnX: 100,
            SpawnY: 200,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);

        if (!spawnIngress.TryPost(GameCommandSourceId.FromConnection(1), session, in request))
        {
            failure = "authoritative spawn ingress rejected command";
            loop.Stop(TimeSpan.FromSeconds(1));
            return false;
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while ((session.State != PlayerJoinState.Playing || state.CommittedPlayerSpawns != 1) &&
               DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        bool stopped = loop.Stop(TimeSpan.FromSeconds(1));
        if (!stopped || loop.Fault is not null)
        {
            failure = $"game loop failed to stop cleanly: fault={loop.Fault}";
            return false;
        }

        if (session.State != PlayerJoinState.Playing ||
            state.LastSpawnCommitResult != PlayerSpawnCommitResult.Committed ||
            state.CommittedPlayerSpawns != 1)
        {
            failure = $"spawn commit did not reach playing state: state={session.State}, " +
                      $"commit={state.LastSpawnCommitResult}, count={state.CommittedPlayerSpawns}";
            return false;
        }

        return true;
    }
}
