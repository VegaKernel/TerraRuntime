using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed record PlayerDisconnectRuntimeCommand(
    ConnectionHandle Connection,
    TaskCompletionSource<bool>? Completion = null) : RuntimeCommand;

internal sealed class RuntimePlayerDisconnectIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerDisconnectIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection)
    {
        if (!connection.IsAssigned)
            return false;

        return _ingress.TryPost(
            connection.Source,
            new PlayerDisconnectRuntimeCommand(connection));
    }
    public async ValueTask<bool> DisconnectAsync(ConnectionHandle connection, CancellationToken cancellationToken)
    {
        if (!connection.IsAssigned)
            throw new ArgumentException("An assigned connection is required.", nameof(connection));
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), completion);
        if (!_ingress.TryPost(GameCommandSourceId.System, new PlayerDisconnectRuntimeCommand(connection, completion)))
            throw new InvalidOperationException("Runtime rejected the final disconnect barrier.");
        return await completion.Task.ConfigureAwait(false);
    }

}
