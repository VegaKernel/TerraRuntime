using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    public void PlayerItemAnimationUpdated(ConnectionHandle connection, float rotation, short animation)
    {
        if (!TerrariaPlayerItemAnimationCodec1458.IsValid(rotation, animation) ||
            !_endpoints.TryGetValue(connection.Source, out RuntimeConnectionEndpoint? endpoint) ||
            !endpoint.TryGetPlayingPlayer(out PlayerHandle current) || current != connection.Player)
            return;
        byte[] encoded = TerrariaPlayerItemAnimationCodec1458.Encode(current.Slot.Value, rotation, animation);
        // MessageBuffer case 41 relays every accepted update to peers, excluding the sender.
        BroadcastToPlayingExcept(connection.Source, encoded);
    }
}
