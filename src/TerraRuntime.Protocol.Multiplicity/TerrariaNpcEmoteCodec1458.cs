using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Source packet91 NPC-anchored emotes. Live source frames have no join replay.</summary>
public static class TerrariaNpcEmoteCodec1458
{
    public static bool TryEncode(int id, byte npcSlot, ushort lifetime, byte emote, out byte[] frame)
    {
        var packet = new EmoteBubble
        {
            BubbleId = id,
            AnchorType = EmoteBubbleAnchorType.Npc,
            AnchorMetaData = npcSlot,
            Lifetime = lifetime,
            Emote = emote
        };
        return packet.TrySerialize(out frame);
    }
}
