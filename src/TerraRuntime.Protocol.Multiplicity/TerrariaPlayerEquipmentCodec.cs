using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Multiplicity-backed packet 5 adapter. TerraRuntime owns framing and authoritative identity;
/// Multiplicity owns the Terraria wire layout.
/// </summary>
public static class TerrariaPlayerEquipmentCodec
{
    public const int PayloadLength = 9;

    public static TerrariaPlayerEquipmentDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaPlayerEquipmentState equipment)
    {
        equipment = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SyncEquipment)
            return TerrariaPlayerEquipmentDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerEquipmentDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out equipment);

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodePayload(scratch, out equipment);
    }

    public static byte[] Encode(in TerrariaPlayerEquipmentState equipment) =>
        (new PlayerSlot
        {
            PlayerId = equipment.PlayerId,
            SlotId = equipment.SlotId,
            Stack = equipment.Stack,
            Prefix = equipment.Prefix,
            ItemNetId = equipment.ItemNetId,
            ItemFlags = equipment.ItemFlags
        }).ToArray();

    private static TerrariaPlayerEquipmentDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlayerEquipmentState equipment)
    {
        try
        {
            var view = PlayerSlotView.FromPayload(payload);
            equipment = new TerrariaPlayerEquipmentState(
                view.PlayerId,
                view.SlotId,
                view.Stack,
                view.Prefix,
                view.ItemNetId,
                view.ItemFlags);
            return TerrariaPlayerEquipmentDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            equipment = default;
            return TerrariaPlayerEquipmentDecodeResult.Malformed;
        }
    }
}
