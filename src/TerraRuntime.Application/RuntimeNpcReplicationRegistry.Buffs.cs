using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private RuntimeNpcStinkyStatus1458? npcBuffStatus;
    internal void BindNpcBuffStatus(RuntimeNpcStinkyStatus1458 status) =>
        npcBuffStatus = status ?? throw new ArgumentNullException(nameof(status));

    internal void PublishNpcBuffs(NpcHandle handle)
    {
        if (npcBuffStatus is null || !npcBuffStatus.TryGetWireDuration(handle, out int duration) ||
            !TerrariaNpcBuffCodec.TryEncodeCurrent(handle.Slot, duration, out byte[] encoded)) return;
        // MessageBuffer53 broadcasts54 to all playing peers, including the requesting connection.
        Broadcast(encoded);
    }

    private void ReplayNpcBuffs(Endpoint endpoint, int slot)
    {
        if (slot >= TerrariaNpcTalkCodec.MaximumNpcSlots || npcBuffStatus is null ||
            !npcBuffStatus.TryGetActiveWireDuration((byte)slot, out int duration) ||
            !TerrariaNpcBuffCodec.TryEncodeCurrent((short)slot, duration, out byte[] encoded)) return;
        // The original join loop emits23 followed immediately by54 for each active NPC.
        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
            Interlocked.Increment(ref baselineFrameCount);
        else
            Interlocked.Increment(ref rejectedFrames);
    }
}
