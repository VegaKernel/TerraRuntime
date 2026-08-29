using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed record ServerPlayerCreateRuntimeCommand(
    ServerPlayerId Id,
    float PositionX,
    float PositionY,
    TaskCompletionSource<ServerPlayerCreateResult> Completion) : RuntimeCommand;

internal sealed record ServerPlayerDespawnRuntimeCommand(
    ServerPlayerId Id,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

/// <summary>
/// Authoritative-thread owner of server-player slot leases and connection-free state. A live server player keeps its
/// shared slot lease for its entire lifetime. Despawn removes authoritative state before releasing the reusable slot.
/// </summary>
internal sealed class RuntimeServerPlayerCommandService
{
    private readonly RuntimeServerPlayerSlotRegistry identities;
    private readonly RuntimeServerPlayerStateStore states;
    private readonly Dictionary<ServerPlayerId, RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];

    public RuntimeServerPlayerCommandService(
        RuntimeServerPlayerSlotRegistry identities,
        RuntimeServerPlayerStateStore states)
    {
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        this.states = states ?? throw new ArgumentNullException(nameof(states));
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case ServerPlayerCreateRuntimeCommand create:
                create.Completion.TrySetResult(Create(create.Id, create.PositionX, create.PositionY));
                return true;

            case ServerPlayerDespawnRuntimeCommand despawn:
                despawn.Completion.TrySetResult(Despawn(despawn.Id));
                return true;

            default:
                return false;
        }
    }

    public ServerPlayerCreateResult Create(ServerPlayerId id, float positionX, float positionY)
    {
        if (!id.IsAssigned)
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.InvalidId, default);
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.InvalidPosition, default);
        if (leases.ContainsKey(id))
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.AlreadyExists, default);

        ServerPlayerSlotAcquireResult acquire = identities.TryAcquire(
            id,
            out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease);
        if (acquire != ServerPlayerSlotAcquireResult.Acquired || lease is null)
        {
            return new ServerPlayerCreateResult(
                acquire switch
                {
                    ServerPlayerSlotAcquireResult.InvalidId => ServerPlayerCreateStatus.InvalidId,
                    ServerPlayerSlotAcquireResult.DuplicateId => ServerPlayerCreateStatus.AlreadyExists,
                    ServerPlayerSlotAcquireResult.NoAvailableSlot => ServerPlayerCreateStatus.NoAvailableSlot,
                    _ => throw new InvalidOperationException("Unknown server-player slot acquisition result.")
                },
                default);
        }

        if (!states.TrySpawn(id, positionX, positionY, out PlayerStateSnapshot snapshot))
        {
            lease.Dispose();
            throw new InvalidOperationException("Server-player identity was acquired but authoritative state could not be created.");
        }

        leases.Add(id, lease);
        return new ServerPlayerCreateResult(ServerPlayerCreateStatus.Created, snapshot.Player);
    }

    public bool Despawn(ServerPlayerId id)
    {
        if (!id.IsAssigned || !leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease))
            return false;

        if (!states.TryRemove(lease.Player, out _))
        {
            throw new InvalidOperationException(
                "A live server-player lease lost its authoritative state before despawn.");
        }

        leases.Remove(id);
        lease.Dispose();
        return true;
    }
}

/// <summary>Trusted-host facade that serializes server-player lifecycle through the authoritative command queue.</summary>
internal sealed class RuntimeServerPlayerOperations : IServerPlayerOperations
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeServerPlayerOperations(IGameCommandIngress<RuntimeCommand> ingress)
    {
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public async ValueTask<ServerPlayerCreateResult> CreateAsync(
        ServerPlayerId id,
        float positionX,
        float positionY,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ServerPlayerCreateResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerCreateRuntimeCommand(id, positionX, positionY, completion)))
        {
            return new ServerPlayerCreateResult(ServerPlayerCreateStatus.QueueRejected, default);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> DespawnAsync(
        ServerPlayerId id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerDespawnRuntimeCommand(id, completion)))
        {
            throw new InvalidOperationException("The authoritative command queue rejected the server-player despawn command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }
}
