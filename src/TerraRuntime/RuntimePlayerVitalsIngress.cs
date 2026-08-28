using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed class PlayerHealthRuntimeCommand : RuntimeCommand
{
    public PlayerHealthRuntimeCommand(
        ConnectionHandle connection,
        PlayerHealthCommitRequest request)
    {
        Connection = connection;
        Request = request;
    }

    public ConnectionHandle Connection { get; }

    public readonly PlayerHealthCommitRequest Request;
}

internal sealed record PlayerManaRuntimeCommand(
    ConnectionHandle Connection,
    PlayerManaCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerHealthIngress : IPlayerHealthIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerHealthIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return false;

        PlayerHealthCommitRequest normalized = VanillaPlayerHealthNormalizer.Normalize(in request);
        return _ingress.TryPost(
            connection.Source,
            new PlayerHealthRuntimeCommand(connection, normalized));
    }
}

internal sealed class RuntimePlayerManaIngress : IPlayerManaIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerManaIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return false;

        return _ingress.TryPost(
            connection.Source,
            new PlayerManaRuntimeCommand(connection, request));
    }
}
