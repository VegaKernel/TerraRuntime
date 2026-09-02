using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface ISignNetworkIngress
{
    bool TryPostRead(ConnectionHandle connection, in TerrariaSignReadRequest request);

    bool TryPostUpdate(ConnectionHandle connection, in TerrariaSignState state);
}

/// <summary>
/// Socket-thread sign ingress. Decoded packet values cross the bounded authoritative command queue with the exact
/// authenticated connection handle; sign lookup and mutation remain game-thread decisions.
/// </summary>
internal sealed class RuntimeSignNetworkIngress : ISignNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeSignNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        this.ingress = ingress;
    }

    public bool TryPostRead(ConnectionHandle connection, in TerrariaSignReadRequest request) =>
        connection.IsAssigned &&
        ingress.TryPost(connection.Source, new ClientSignReadRuntimeCommand(connection, request));

    public bool TryPostUpdate(ConnectionHandle connection, in TerrariaSignState state) =>
        connection.IsAssigned &&
        ingress.TryPost(connection.Source, new ClientSignUpdateRuntimeCommand(connection, state));
}
