using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    internal void PublishAnglerState(ConnectionHandle connection, byte quest, bool completed, int count, int golferScore = 0)
    {
        if (!_endpoints.TryGetValue(connection.Source, out var endpoint) ||
            !endpoint.TryGetPlayingPlayer(out var player) || player != connection.Player) return;
        // Official NetMessage74: byte quest + bool name-in-today-list. Packet76 retains both character counters.
        byte[] daily = [5, 0, 74, quest, completed ? (byte)1 : (byte)0];
        byte[] progress = TerrariaPlayerCharacterProgressCodec1458.Encode(new(player.Slot.Value, count, golferScore));
        endpoint.Outbound.TryEnqueue(new OutboundFrame(daily));
        endpoint.Outbound.TryEnqueue(new OutboundFrame(progress));
    }

    internal void PublishCharacterProgress(ConnectionHandle connection, int anglerQuestsFinished, int golferScoreAccumulated)
    {
        if (!_endpoints.TryGetValue(connection.Source, out var endpoint) ||
            !endpoint.TryGetPlayingPlayer(out var player) || player != connection.Player) return;
        // MessageBuffer76 relays the authenticated sender to peers and excludes the source client.
        byte[] frame = TerrariaPlayerCharacterProgressCodec1458.Encode(
            new(player.Slot.Value, anglerQuestsFinished, golferScoreAccumulated));
        BroadcastToPlayingExcept(connection.Source, frame);
    }
}
