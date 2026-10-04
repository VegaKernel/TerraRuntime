using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed record PlayerLuckFactorsRuntimeCommand(ConnectionHandle Connection,
    VanillaPlayerLuckComponents1458 Factors) : RuntimeCommand;

internal interface IPlayerLuckFactorsNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in VanillaPlayerLuckComponents1458 factors);
}

internal sealed class RuntimePlayerLuckFactorsNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    : IPlayerLuckFactorsNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    public bool TryPost(ConnectionHandle connection, in VanillaPlayerLuckComponents1458 factors) =>
        connection.IsAssigned && factors.IsFinite &&
        ingress.TryPost(connection.Source, new PlayerLuckFactorsRuntimeCommand(connection, factors));
}
