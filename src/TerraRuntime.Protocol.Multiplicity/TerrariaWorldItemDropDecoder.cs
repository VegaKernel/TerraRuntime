using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Decodes Terraria packet 21 through Multiplicity's protocol-326 world-item view. The adapter preserves
/// the wire-level new-item request sentinel and does not manufacture packet-22 ownership/reservation state.
/// </summary>
public static class TerrariaWorldItemDropDecoder
{
    public const int MinimumPayloadLength = 24;
    public const int MaximumPayloadLength = 30;

    public static TerrariaWorldItemDropDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaWorldItemDropState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.WorldItemDrop)
            return TerrariaWorldItemDropDecodeResult.WrongMessageId;

        long payloadLength = frame.Payload.Length;
        if (payloadLength is < MinimumPayloadLength or > MaximumPayloadLength)
            return TerrariaWorldItemDropDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out state);

        int length = checked((int)payloadLength);
        Span<byte> scratch = stackalloc byte[MaximumPayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodePayload(scratch[..length], out state);
    }

    private static TerrariaWorldItemDropDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaWorldItemDropState state)
    {
        try
        {
            var view = WorldItemSyncView.FromPayload(PacketTypes.ItemDrop, payload);
            state = new TerrariaWorldItemDropState(
                ItemIndex: view.ItemIndex,
                PositionX: view.PositionX,
                PositionY: view.PositionY,
                VelocityX: view.VelocityX,
                VelocityY: view.VelocityY,
                Stack: view.Stack,
                Prefix: view.Prefix,
                ItemNetId: view.ItemNetId,
                Ownership: (TerrariaWorldItemOwnership)(byte)view.Ownership,
                Shimmered: view.Shimmered,
                ShimmerTime: view.ShimmerTime,
                EnemyGrabDelayTime: view.EnemyGrabDelayTime);

            return state.IsValid
                ? TerrariaWorldItemDropDecodeResult.Decoded
                : TerrariaWorldItemDropDecodeResult.InvalidState;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            state = default;
            return TerrariaWorldItemDropDecodeResult.Malformed;
        }
    }
}
