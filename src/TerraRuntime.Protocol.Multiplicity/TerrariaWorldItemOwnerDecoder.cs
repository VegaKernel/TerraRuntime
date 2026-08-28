using System.IO;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Decodes Terraria packet 22 through Multiplicity's protocol-326 ItemOwner view. Variable-length
/// reservation timers stay inside the protocol adapter; runtime consumers receive primitive state only.
/// </summary>
public static class TerrariaWorldItemOwnerDecoder
{
    public const int MinimumPayloadLength = 14;
    public const int MaximumPayloadLength = 22;

    public static TerrariaWorldItemOwnerDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaWorldItemOwnerState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.WorldItemOwner)
            return TerrariaWorldItemOwnerDecodeResult.WrongMessageId;

        long payloadLength = frame.Payload.Length;
        if (payloadLength is < MinimumPayloadLength or > MaximumPayloadLength)
            return TerrariaWorldItemOwnerDecodeResult.InvalidPayloadLength;

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

    private static TerrariaWorldItemOwnerDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaWorldItemOwnerState state)
    {
        try
        {
            var view = ItemOwnerView.FromPayload(payload);
            state = new TerrariaWorldItemOwnerState(
                ItemIndex: view.ItemId,
                OwnerPlayerId: view.PlayerId,
                TimeToKeepReservation: view.TimeToKeepReservation,
                GrabDelayPlayer: view.GrabDelayPlayer,
                GrabDelayTime: view.GrabDelayTime,
                PositionX: view.PositionX,
                PositionY: view.PositionY);

            return state.IsValid
                ? TerrariaWorldItemOwnerDecodeResult.Decoded
                : TerrariaWorldItemOwnerDecodeResult.InvalidState;
        }
        catch (Exception exception) when (exception is IOException or FormatException or ArgumentOutOfRangeException)
        {
            state = default;
            return TerrariaWorldItemOwnerDecodeResult.Malformed;
        }
    }
}
