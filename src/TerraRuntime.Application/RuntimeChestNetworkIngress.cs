using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface IChestNetworkIngress
{
    bool TryPostOpen(ConnectionHandle connection, in TerrariaChestOpenRequest request);

    bool TryPostItem(ConnectionHandle connection, in TerrariaChestItemState state);

    bool TryPostActiveState(ConnectionHandle connection, in TerrariaActiveChestState state);

    bool TryPostNameLookup(ConnectionHandle connection, in TerrariaChestNameLookupRequest request);
}

/// <summary>
/// Socket-thread chest ingress. Decoded packet values cross the bounded command queue with the exact authenticated
/// connection handle; chest lookup, ownership and mutations remain authoritative-thread decisions.
/// </summary>
internal sealed class RuntimeChestNetworkIngress : IChestNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeChestNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        this.ingress = ingress;
    }

    public bool TryPostOpen(ConnectionHandle connection, in TerrariaChestOpenRequest request) =>
        connection.IsAssigned &&
        ingress.TryPost(connection.Source, new ClientChestOpenRuntimeCommand(connection, request));

    public bool TryPostItem(ConnectionHandle connection, in TerrariaChestItemState state) =>
        connection.IsAssigned &&
        ingress.TryPost(connection.Source, new ClientChestItemRuntimeCommand(connection, state));

    public bool TryPostActiveState(ConnectionHandle connection, in TerrariaActiveChestState state) =>
        connection.IsAssigned &&
        ingress.TryPost(connection.Source, new ClientActiveChestRuntimeCommand(connection, state));

    public bool TryPostNameLookup(ConnectionHandle connection, in TerrariaChestNameLookupRequest request) =>
        connection.IsAssigned &&
        ingress.TryPost(connection.Source, new ClientChestNameLookupRuntimeCommand(connection, request));
}
