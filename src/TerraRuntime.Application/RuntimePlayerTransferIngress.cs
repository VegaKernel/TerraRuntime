using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerTransferDetachRuntimeCommand(
    ConnectionHandle Connection,
    TaskCompletionSource<RuntimePlayerTransferState?> Completion) : RuntimeCommand;

internal sealed record PlayerTransferAttachRuntimeCommand(
    ConnectionHandle Connection,
    RuntimePlayerTransferState Transfer,
    short SpawnX,
    short SpawnY,
    bool PreserveWorldPosition,
    bool ForceRespawn,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

/// <summary>
/// Crosses the process/connection transfer coordinator into one world's authoritative queue. Capture+detach is one
/// source-world barrier, and attach is one destination-world barrier; no socket or console thread mutates player state.
/// </summary>
internal sealed class RuntimePlayerTransferIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimePlayerTransferIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public async ValueTask<RuntimePlayerTransferState?> DetachAsync(
        ConnectionHandle connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<RuntimePlayerTransferState?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<RuntimePlayerTransferState?>)state!).TrySetCanceled(),
            completion);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new PlayerTransferDetachRuntimeCommand(connection, completion)))
        {
            throw new InvalidOperationException("Source runtime rejected the transfer-detach barrier.");
        }
        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> AttachAsync(
        ConnectionHandle connection,
        RuntimePlayerTransferState transfer,
        short spawnX,
        short spawnY,
        bool preserveWorldPosition,
        bool forceRespawn,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            completion);
        if (!ingress.TryPost(
                GameCommandSourceId.System,
                new PlayerTransferAttachRuntimeCommand(
                    connection,
                    transfer,
                    spawnX,
                    spawnY,
                    preserveWorldPosition,
                    forceRespawn,
                    completion)))
        {
            throw new InvalidOperationException("Destination runtime rejected the transfer-attach barrier.");
        }
        return await completion.Task.ConfigureAwait(false);
    }
}
