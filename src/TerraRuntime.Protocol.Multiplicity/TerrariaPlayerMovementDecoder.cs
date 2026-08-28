using System.IO;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Bounded protocol-326 decoder for packet 13. Multiplicity owns the wire layout;
/// TerraRuntime receives a protocol-library-neutral value for authoritative validation.
/// </summary>
public static class TerrariaPlayerMovementDecoder
{
    public const int MinimumPayloadLength = 14;
    public const int MaximumPayloadLength = 48;

    public static TerrariaPlayerMovementDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaPlayerMovementRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerControls)
            return TerrariaPlayerMovementDecodeResult.WrongMessageId;

        long payloadLength = frame.Payload.Length;
        if (payloadLength is < MinimumPayloadLength or > MaximumPayloadLength)
            return TerrariaPlayerMovementDecodeResult.Malformed;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[MaximumPayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodePayload(scratch[..checked((int)payloadLength)], out request);
    }

    private static TerrariaPlayerMovementDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlayerMovementRequest request)
    {
        try
        {
            var view = PlayerUpdateView.FromPayload(payload);
            request = new TerrariaPlayerMovementRequest(
                view.PlayerId,
                (byte)view.ControlFlags,
                (byte)view.MovementFlags,
                (byte)view.MiscFlags1,
                (byte)view.MiscFlags2,
                view.SelectedItem,
                view.PositionX,
                view.PositionY,
                view.HasVelocity,
                view.VelocityX,
                view.VelocityY,
                view.HasMount,
                view.MountType,
                view.HasPotionOfReturnPositions,
                view.PotionOfReturnOriginalPositionX,
                view.PotionOfReturnOriginalPositionY,
                view.PotionOfReturnHomePositionX,
                view.PotionOfReturnHomePositionY,
                view.HasCameraTarget,
                view.CameraTargetX,
                view.CameraTargetY);
        }
        catch (InvalidDataException)
        {
            request = default;
            return TerrariaPlayerMovementDecodeResult.Malformed;
        }

        if (!float.IsFinite(request.PositionX) || !float.IsFinite(request.PositionY) ||
            request.HasVelocity && (!float.IsFinite(request.VelocityX) || !float.IsFinite(request.VelocityY)) ||
            request.HasPotionOfReturnPositions &&
                (!float.IsFinite(request.PotionOfReturnOriginalPositionX) ||
                 !float.IsFinite(request.PotionOfReturnOriginalPositionY) ||
                 !float.IsFinite(request.PotionOfReturnHomePositionX) ||
                 !float.IsFinite(request.PotionOfReturnHomePositionY)) ||
            request.HasCameraTarget &&
                (!float.IsFinite(request.CameraTargetX) || !float.IsFinite(request.CameraTargetY)))
        {
            request = default;
            return TerrariaPlayerMovementDecodeResult.NonFiniteValue;
        }

        return TerrariaPlayerMovementDecodeResult.Decoded;
    }
}
