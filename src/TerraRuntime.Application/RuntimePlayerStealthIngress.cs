using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record PlayerStealthRuntimeCommand(ConnectionHandle Connection, float Stealth) : RuntimeCommand;

internal interface IPlayerStealthNetworkIngress
{
    bool TryPost(ConnectionHandle connection, float stealth);
}

internal sealed class RuntimePlayerStealthNetworkIngress : IPlayerStealthNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimePlayerStealthNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, float stealth) =>
        connection.IsAssigned && TerrariaPlayerStealthCodec1458.IsValid(stealth) &&
        ingress.TryPost(connection.Source, new PlayerStealthRuntimeCommand(connection, stealth));
}
