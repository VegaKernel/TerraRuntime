using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal interface ITileNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state);
    bool TryPostLiquid(ConnectionHandle connection, in TerrariaLiquidState state);
}

/// <summary>
/// Connection-authenticated packet-17 ingress. The socket thread only carries immutable decoded state across the
/// bounded command queue; all current-session, world-bounds and gameplay-authority decisions remain on the single
/// authoritative writer thread.
/// </summary>
internal class RuntimeTileNetworkIngress : ITileNetworkIngress
{
    protected IGameCommandIngress<RuntimeCommand> Ingress { get; }

    public RuntimeTileNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        Ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state)
    {
        if (!connection.IsAssigned)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientTileManipulationRuntimeCommand(connection, state));
    }

    public bool TryPostLiquid(ConnectionHandle connection, in TerrariaLiquidState state)
    {
        if (!connection.IsAssigned)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientLiquidRuntimeCommand(connection, state));
    }
}
