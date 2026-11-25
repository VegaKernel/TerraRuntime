using System.Buffers.Binary;
using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Protocol 326 packet 91 NPC emotes, including the legacy item-display metadata.</summary>
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
        if (!packet.TrySerialize(out frame)) return false;
        if (emote == byte.MaxValue)
        {
            Array.Resize(ref frame, frame.Length + sizeof(short));
            BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        }

        return true;
    }
}
