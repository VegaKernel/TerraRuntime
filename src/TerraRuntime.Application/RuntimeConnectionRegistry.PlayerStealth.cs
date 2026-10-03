using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    public void PlayerStealthUpdated(ConnectionHandle connection, float stealth)
    {
        if (!TerrariaPlayerStealthCodec1458.IsValid(stealth) ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle current) || current != connection.Player)
            return;
        byte[] encoded = TerrariaPlayerStealthCodec1458.Encode(current.Slot.Value, stealth);
        // MessageBuffer case 84 relays every accepted update to peers, excluding the sender.
        BroadcastToPlayingExcept(connection.Source, encoded);
    }
}
