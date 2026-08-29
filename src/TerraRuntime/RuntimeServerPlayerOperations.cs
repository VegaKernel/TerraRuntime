using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed record ServerPlayerCreateRuntimeCommand(
    ServerPlayerId Id,
    float PositionX,
    float PositionY,
    TaskCompletionSource<ServerPlayerCreateResult> Completion) : RuntimeCommand;

internal sealed record ServerPlayerHorizontalIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerHorizontalIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerJumpIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerJumpIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerDespawnRuntimeCommand(
    ServerPlayerId Id,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

/// <summary>
/// Authoritative-thread owner of server-player slot leases, semantic control intent and connection-free state. A live
/// server player keeps its shared slot lease for its entire lifetime. Control state is keyed by the exact reusable
/// <see cref="PlayerHandle"/> generation, so despawn/reuse cannot transfer stale input to a replacement player.
/// </summary>
internal sealed class RuntimeServerPlayerCommandService
{
    private readonly RuntimeServerPlayerSlotRegistry identities;
    private readonly RuntimeServerPlayerStateStore states;
    private readonly Dictionary<ServerPlayerId, RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerHorizontalIntent> horizontalIntents = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerJumpIntent> jumpIntents = [];
    private readonly Dictionary<PlayerHandle, VanillaServerPlayerJumpState> jumpStates = [];

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

            case ServerPlayerHorizontalIntentRuntimeCommand horizontal:
                horizontal.Completion.TrySetResult(SetHorizontalIntent(horizontal.Id, horizontal.Intent));
                return true;

            case ServerPlayerJumpIntentRuntimeCommand jump:
                jump.Completion.TrySetResult(SetJumpIntent(jump.Id, jump.Intent));
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

    public bool SetHorizontalIntent(ServerPlayerId id, ServerPlayerHorizontalIntent intent)
    {
        if (!id.IsAssigned ||
            !IsValidHorizontalIntent(intent) ||
            !leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) ||
            !states.TryGet(lease.Player, out _))
        {
            return false;
        }

        if (intent == ServerPlayerHorizontalIntent.Stop)
            horizontalIntents.Remove(lease.Player);
        else
            horizontalIntents[lease.Player] = intent;

        return true;
    }

    public ServerPlayerHorizontalIntent GetHorizontalIntent(PlayerHandle player) =>
        player.IsAssigned && horizontalIntents.TryGetValue(player, out ServerPlayerHorizontalIntent intent)
            ? intent
            : ServerPlayerHorizontalIntent.Stop;

    public bool SetJumpIntent(ServerPlayerId id, ServerPlayerJumpIntent intent)
    {
        if (!id.IsAssigned ||
            !IsValidJumpIntent(intent) ||
            !leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) ||
            !states.TryGet(lease.Player, out _))
        {
            return false;
        }

        if (intent == ServerPlayerJumpIntent.Released)
        {
            jumpIntents.Remove(lease.Player);
            jumpStates.Remove(lease.Player);
        }
        else
        {
            jumpIntents[lease.Player] = intent;
        }

        return true;
    }

    public ServerPlayerJumpIntent GetJumpIntent(PlayerHandle player) =>
        player.IsAssigned && jumpIntents.TryGetValue(player, out ServerPlayerJumpIntent intent)
            ? intent
            : ServerPlayerJumpIntent.Released;

    public VanillaServerPlayerJumpState GetJumpState(PlayerHandle player) =>
        player.IsAssigned && jumpStates.TryGetValue(player, out VanillaServerPlayerJumpState state)
            ? state
            : VanillaServerPlayerJumpState.Initial;

    public void CommitJumpState(PlayerHandle player, in VanillaServerPlayerJumpState state)
    {
        if (!player.IsAssigned || !states.TryGet(player, out _))
        {
            jumpIntents.Remove(player);
            jumpStates.Remove(player);
            return;
        }

        if (state == VanillaServerPlayerJumpState.Initial)
            jumpStates.Remove(player);
        else
            jumpStates[player] = state;
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

        horizontalIntents.Remove(lease.Player);
        jumpIntents.Remove(lease.Player);
        jumpStates.Remove(lease.Player);
        leases.Remove(id);
        lease.Dispose();
        return true;
    }

    private static bool IsValidHorizontalIntent(ServerPlayerHorizontalIntent intent) =>
        intent is ServerPlayerHorizontalIntent.Left or
            ServerPlayerHorizontalIntent.Stop or
            ServerPlayerHorizontalIntent.Right;

    private static bool IsValidJumpIntent(ServerPlayerJumpIntent intent) =>
        intent is ServerPlayerJumpIntent.Released or ServerPlayerJumpIntent.Held;
}

/// <summary>
/// Trusted-host facade that serializes server-player lifecycle and semantic control through the authoritative command
/// queue. Once accepted by the queue, completion is intentionally not cancellable to avoid an ambiguous maybe-applied
/// control mutation.
/// </summary>
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

    public async ValueTask<bool> SetHorizontalIntentAsync(
        ServerPlayerId id,
        ServerPlayerHorizontalIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerHorizontalIntentRuntimeCommand(id, intent, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player horizontal intent command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> SetJumpIntentAsync(
        ServerPlayerId id,
        ServerPlayerJumpIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerJumpIntentRuntimeCommand(id, intent, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player jump intent command.");
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
