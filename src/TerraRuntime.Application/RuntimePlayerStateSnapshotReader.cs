using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed record PlayerStateSnapshotRuntimeCommand(
    PlayerHandle Player,
    TaskCompletionSource<PlayerStateSnapshot?> Completion) : RuntimeCommand;

internal sealed class RuntimePlayerStateSnapshotReader : IPlayerStateSnapshotReader
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerStateSnapshotReader(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    internal IGameCommandIngress<RuntimeCommand> CommandIngress => _ingress;

    public async ValueTask<PlayerStateSnapshot?> CaptureAsync(
        PlayerHandle player,
        CancellationToken cancellationToken = default)
    {
        if (!player.IsAssigned)
            throw new ArgumentException("An assigned player handle is required.", nameof(player));

        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<PlayerStateSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration cancellation = cancellationToken.Register(
            static state => ((TaskCompletionSource<PlayerStateSnapshot?>)state!).TrySetCanceled(),
            completion);

        if (!_ingress.TryPost(
                GameCommandSourceId.System,
                new PlayerStateSnapshotRuntimeCommand(player, completion)))
        {
            throw new InvalidOperationException("The authoritative command queue rejected the snapshot request.");
        }

        return await completion.Task.ConfigureAwait(false);
    }
}
