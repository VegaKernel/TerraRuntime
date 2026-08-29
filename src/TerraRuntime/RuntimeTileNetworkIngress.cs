using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface ITileNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state);
}

/// <summary>
/// Connection-authenticated packet-17 ingress. The socket thread only carries immutable decoded state across the
/// bounded command queue; all current-session, world-bounds and gameplay-authority decisions remain on the single
/// authoritative writer thread.
/// </summary>
internal sealed class RuntimeTileNetworkIngress : ITileNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeTileNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        this.ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state)
    {
        if (!connection.IsAssigned)
            return false;

        return ingress.TryPost(
            connection.Source,
            new ClientTileManipulationRuntimeCommand(connection, state));
    }
}
