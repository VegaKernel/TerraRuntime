using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private RuntimeNpcDeathPrelude1458? deathPrelude;
    public void BindDeathPrelude(RuntimeNpcDeathPrelude1458 owner) => deathPrelude = owner ?? throw new ArgumentNullException(nameof(owner));
    public void BroadcastDeathPrelude(byte[] frame) => Broadcast(frame);
    public bool IsDeathPreludePlayerCurrent(ConnectionHandle connection) => endpoints.TryGetValue(connection.Source, out var endpoint) &&
        endpoint.MatchesPlayer(connection.Player);
    public bool TrySendDeathPrelude(PlayerHandle player, byte[] bytes)
    {
        foreach (Endpoint endpoint in endpoints.Values)
        {
            if (!endpoint.MatchesPlayer(player)) continue;
            if (endpoint.Outbound.TryEnqueue(new OutboundFrame(bytes)) == OutboundEnqueueResult.Enqueued)
            { Interlocked.Increment(ref relayedFrames); return true; }
            Interlocked.Increment(ref rejectedFrames); return false;
        }
        return false;
    }
}
