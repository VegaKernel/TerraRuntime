using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    public void PlayerZonesUpdated(ConnectionHandle connection, in PlayerZoneSnapshot1458 zones)
    {
        if (!_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle current) || current != connection.Player) return;
        BroadcastToPlayingExcept(connection.Source, TerrariaPlayerZonesCodec1458.Encode(current.Slot.Value, in zones));
    }
}
