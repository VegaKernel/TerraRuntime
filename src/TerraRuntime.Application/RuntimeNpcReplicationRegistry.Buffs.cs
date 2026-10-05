using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private RuntimeNpcBuffStatus1458? npcBuffStatus;
    internal void BindNpcBuffStatus(RuntimeNpcBuffStatus1458 status) =>
        npcBuffStatus = status ?? throw new ArgumentNullException(nameof(status));

    internal void PublishNpcBuffs(NpcHandle handle)
    {
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[RuntimeNpcBuffStatus1458.Capacity];
        if (npcBuffStatus is null || !npcBuffStatus.TryCopyWireBuffs(handle, buffs, out int count) ||
            !TerrariaNpcBuffCodec.TryEncodeCurrent(handle.Slot, buffs[..count], out byte[] encoded)) return;
        // MessageBuffer53 broadcasts54 to all playing peers, including the requesting connection.
        Broadcast(encoded);
    }

    private void ReplayNpcBuffs(Endpoint endpoint, int slot)
    {
        Span<TerrariaNpcBuffEntryState> buffs = stackalloc TerrariaNpcBuffEntryState[RuntimeNpcBuffStatus1458.Capacity];
        if (slot >= TerrariaNpcTalkCodec.MaximumNpcSlots || npcBuffStatus is null ||
            !npcBuffStatus.TryCopyActiveWireBuffs((byte)slot, buffs, out int count) ||
            !TerrariaNpcBuffCodec.TryEncodeCurrent((short)slot, buffs[..count], out byte[] encoded)) return;
        // The original join loop emits23 followed immediately by54 for each active NPC.
        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)
            Interlocked.Increment(ref baselineFrameCount);
        else
            Interlocked.Increment(ref rejectedFrames);
    }
}
