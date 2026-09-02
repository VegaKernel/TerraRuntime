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

internal sealed record ServerPlayerMovementIntentRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerMovementIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerAppearanceRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerAppearanceState Appearance,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerVitalsRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerVitalsState Vitals,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record ServerPlayerItemRuntimeCommand(
    ServerPlayerId Id,
    ServerPlayerItemState Item,
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
    private readonly IRuntimeServerPlayerEventSink? events;
    private readonly Dictionary<ServerPlayerId, RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease> leases = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerHorizontalIntent> horizontalIntents = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerJumpIntent> jumpIntents = [];
    private readonly Dictionary<PlayerHandle, VanillaServerPlayerJumpState> jumpStates = [];
    private readonly Dictionary<PlayerHandle, ServerPlayerMovementIntent> movementIntents = [];

    public RuntimeServerPlayerCommandService(
        RuntimeServerPlayerSlotRegistry identities,
        RuntimeServerPlayerStateStore states,
        IRuntimeServerPlayerEventSink? events = null)
    {
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
        this.states = states ?? throw new ArgumentNullException(nameof(states));
        this.events = events;
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

            case ServerPlayerMovementIntentRuntimeCommand movement:
                {
                    ServerPlayerMovementIntent intent = movement.Intent;
                    movement.Completion.TrySetResult(SetMovementIntent(movement.Id, in intent));
                    return true;
                }

            case ServerPlayerAppearanceRuntimeCommand appearance:
                {
                    ServerPlayerAppearanceState value = appearance.Appearance;
                    appearance.Completion.TrySetResult(SetAppearance(appearance.Id, in value));
                    return true;
                }

            case ServerPlayerVitalsRuntimeCommand vitals:
                {
                    ServerPlayerVitalsState value = vitals.Vitals;
                    vitals.Completion.TrySetResult(SetVitals(vitals.Id, in value));
                    return true;
                }

            case ServerPlayerItemRuntimeCommand item:
                {
                    ServerPlayerItemState value = item.Item;
                    item.Completion.TrySetResult(SetItem(item.Id, in value));
                    return true;
                }

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
        events?.ServerPlayerCreated(in snapshot);
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
        movementIntents.Remove(lease.Player);

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
        movementIntents.Remove(lease.Player);

        return true;
    }

    public bool SetMovementIntent(ServerPlayerId id, in ServerPlayerMovementIntent intent)
    {
        if (!intent.IsValid || !TryGetPlayer(id, out PlayerHandle player))
            return false;

        horizontalIntents.Remove(player);
        jumpIntents.Remove(player);
        jumpStates.Remove(player);
        if (intent.Kind == ServerPlayerMovementIntentKind.Stop)
            movementIntents.Remove(player);
        else
            movementIntents[player] = intent;
        return true;
    }

    public ServerPlayerMovementIntent GetMovementIntent(PlayerHandle player) =>
        player.IsAssigned && movementIntents.TryGetValue(player, out ServerPlayerMovementIntent intent)
            ? intent
            : ServerPlayerMovementIntent.Stop();

    public ServerPlayerJumpIntent GetJumpIntent(PlayerHandle player) =>
        player.IsAssigned && jumpIntents.TryGetValue(player, out ServerPlayerJumpIntent intent)
            ? intent
            : ServerPlayerJumpIntent.Released;

    public bool SetAppearance(ServerPlayerId id, in ServerPlayerAppearanceState appearance)
    {
        if (!TryGetPlayer(id, out PlayerHandle player) ||
            !states.TrySetAppearance(player, in appearance, out ServerPlayerAppearanceState normalized))
        {
            return false;
        }

        events?.ServerPlayerAppearanceUpdated(player, in normalized);
        return true;
    }

    public bool SetVitals(ServerPlayerId id, in ServerPlayerVitalsState vitals)
    {
        if (!TryGetPlayer(id, out PlayerHandle player) ||
            !states.TrySetVitals(player, in vitals, out PlayerStateSnapshot normalized))
        {
            return false;
        }

        var committed = new ServerPlayerVitalsState(
            normalized.Life,
            normalized.MaxLife,
            normalized.Mana,
            normalized.MaxMana);
        events?.ServerPlayerVitalsUpdated(player, in committed);
        return true;
    }

    public bool SetItem(ServerPlayerId id, in ServerPlayerItemState item)
    {
        if (!TryGetPlayer(id, out PlayerHandle player) ||
            !states.TrySetItem(player, in item, out ServerPlayerItemState normalized))
        {
            return false;
        }

        events?.ServerPlayerItemUpdated(player, in normalized);
        return true;
    }

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
        movementIntents.Remove(lease.Player);
        leases.Remove(id);
        events?.ServerPlayerDespawned(lease.Player);
        lease.Dispose();
        return true;
    }

    private bool TryGetPlayer(ServerPlayerId id, out PlayerHandle player)
    {
        if (id.IsAssigned &&
            leases.TryGetValue(id, out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? lease) &&
            states.TryGet(lease.Player, out _))
        {
            player = lease.Player;
            return true;
        }

        player = default;
        return false;
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

    public async ValueTask<bool> SetMovementIntentAsync(
        ServerPlayerId id,
        ServerPlayerMovementIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerMovementIntentRuntimeCommand(id, intent, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player movement intent command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> SetAppearanceAsync(
        ServerPlayerId id,
        ServerPlayerAppearanceState appearance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerAppearanceRuntimeCommand(id, appearance, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player appearance command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> SetVitalsAsync(
        ServerPlayerId id,
        ServerPlayerVitalsState vitals,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerVitalsRuntimeCommand(id, vitals, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player vitals command.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> SetItemAsync(
        ServerPlayerId id,
        ServerPlayerItemState item,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new ServerPlayerItemRuntimeCommand(id, item, completion)))
        {
            throw new InvalidOperationException(
                "The authoritative command queue rejected the server-player item command.");
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
