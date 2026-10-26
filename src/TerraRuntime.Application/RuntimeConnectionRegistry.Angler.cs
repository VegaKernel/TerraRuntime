using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    internal void PublishAnglerState(ConnectionHandle connection, byte quest, bool completed, int count)
    {
        if (!_endpoints.TryGetValue(connection.Source, out var endpoint) ||
            !endpoint.TryGetPlayingPlayer(out var player) || player != connection.Player) return;
        // Official NetMessage74: byte quest + bool name-in-today-list. Packet76
        // carries two Int32 values; this runtime has no accepted golf producer.
        byte[] daily = [5, 0, 74, quest, completed ? (byte)1 : (byte)0];
        var progress = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(progress, 12);
        progress[2] = 76; progress[3] = player.Slot.Value;
        BinaryPrimitives.WriteInt32LittleEndian(progress.AsSpan(4), count);
        endpoint.Outbound.TryEnqueue(new OutboundFrame(daily));
        endpoint.Outbound.TryEnqueue(new OutboundFrame(progress));
    }
}
