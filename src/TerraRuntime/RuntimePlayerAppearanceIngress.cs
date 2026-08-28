using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerAppearanceRuntimeCommand(
    ConnectionHandle Connection,
    PlayerAppearanceCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerAppearanceIngress : IPlayerAppearanceIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerAppearanceIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return false;

        if (!VanillaPlayerAppearanceNormalizer.TryNormalize(in request, out PlayerAppearanceCommitRequest normalized))
            return false;

        return _ingress.TryPost(
            connection.Source,
            new PlayerAppearanceRuntimeCommand(connection, normalized));
    }
}
