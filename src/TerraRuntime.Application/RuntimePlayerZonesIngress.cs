using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed record PlayerZonesRuntimeCommand(ConnectionHandle Connection, PlayerZoneSnapshot1458 Zones) : RuntimeCommand;

internal interface IPlayerZonesNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerZoneSnapshot1458 zones);
}

internal sealed class RuntimePlayerZonesNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) : IPlayerZonesNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, in PlayerZoneSnapshot1458 zones) =>
        connection.IsAssigned && ingress.TryPost(connection.Source, new PlayerZonesRuntimeCommand(connection, zones));
}
