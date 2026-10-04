using System.Buffers.Binary;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaNpcBuffState(short NpcSlot, ushort BuffType, short Duration);

/// <summary>Protocol-326 NPC AddBuff and current-list projection. Multiplicity owns outbound layouts.</summary>
public static class TerrariaNpcBuffCodec
{
    public const int AddPayloadLength = 6;
    public static bool IsValid(in TerrariaNpcBuffState state) =>
        (uint)state.NpcSlot < TerrariaNpcTalkCodec.MaximumNpcSlots &&
        state.BuffType == VanillaBuffIds.Stinky.Value && state.Duration >= 0;

    public static bool TryDecode(in TerrariaFrame frame, out TerrariaNpcBuffState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.AddNpcBuff || frame.Payload.Length != AddPayloadLength)
            return false;
        Span<byte> payload = stackalloc byte[AddPayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(payload[offset..]);
            offset += segment.Length;
        }
        state = new(BinaryPrimitives.ReadInt16LittleEndian(payload),
            BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]),
            BinaryPrimitives.ReadInt16LittleEndian(payload[4..]));
        return true;
    }

    public static bool TryEncodeAdd(in TerrariaNpcBuffState state, out byte[] frame)
    {
        frame = [];
        return IsValid(in state) && new NpcAddBuff
        {
            NpcId = state.NpcSlot, Buff = state.BuffType, Time = state.Duration
        }.TrySerialize(out frame);
    }

    public static bool TryEncodeCurrent(short npcSlot, int duration, out byte[] frame)
    {
        frame = [];
        if ((uint)npcSlot >= TerrariaNpcTalkCodec.MaximumNpcSlots || duration is < -1 or > short.MaxValue)
            return false;
        var packet = new NpcUpdateBuff { NpcId = npcSlot };
        if (duration > 0)
            packet.Buffs.Add(new NpcBuffEntry((ushort)VanillaBuffIds.Stinky.Value, (ushort)duration));
        return packet.TrySerialize(out frame);
    }
}
