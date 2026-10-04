using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private int nextEmoteId;

    // Authoritative source EmoteBubble.NextID advances even when no playing endpoint exists.
    internal void PublishNpcEmote(byte slot, ushort lifetime, byte emote)
    {
        int id = nextEmoteId;
        nextEmoteId = unchecked(id + 1);
        if (TerrariaNpcEmoteCodec1458.TryEncode(id, slot, lifetime, emote, out var frame)) Broadcast(frame);
    }
}
