using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Application;

internal sealed record SetPlayerGodModeRuntimeCommand(
    PlayerHandle Player,
    bool Enabled,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record GetPlayerGodModeRuntimeCommand(
    PlayerHandle Player,
    TaskCompletionSource<bool?> Completion) : RuntimeCommand;

internal sealed class RuntimePlayerAdministrativeOperations : IPlayerAdministrativeOperations
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimePlayerAdministrativeOperations(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public async ValueTask<bool> SetGodModeAsync(
        PlayerHandle player,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!player.IsAssigned)
            throw new ArgumentException("An assigned player handle is required.", nameof(player));
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(GameCommandSourceId.System, new SetPlayerGodModeRuntimeCommand(player, enabled, completion)))
            throw new InvalidOperationException("The authoritative command queue rejected the godmode command.");
        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool?> GetGodModeAsync(
        PlayerHandle player,
        CancellationToken cancellationToken = default)
    {
        if (!player.IsAssigned)
            throw new ArgumentException("An assigned player handle is required.", nameof(player));
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ingress.TryPost(GameCommandSourceId.System, new GetPlayerGodModeRuntimeCommand(player, completion)))
            throw new InvalidOperationException("The authoritative command queue rejected the godmode query.");
        return await completion.Task.ConfigureAwait(false);
    }
}
