using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    public void PlayerLuckFactorsUpdated(ConnectionHandle connection, in VanillaPlayerLuckComponents1458 factors)
    {
        if (!factors.IsFinite ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle current) || current != connection.Player)
            return;
        byte[] encoded = TerrariaPlayerLuckFactorsCodec1458.Encode(current.Slot.Value, factors);
        // MessageBuffer case 134 relays every accepted update to peers, excluding the sender.
        BroadcastToPlayingExcept(connection.Source, encoded);
    }
}
