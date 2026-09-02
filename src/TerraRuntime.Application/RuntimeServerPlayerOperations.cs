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
