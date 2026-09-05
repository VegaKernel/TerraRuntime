using System.Buffers;
using System.IO;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaBossSummonRequest(
    short ClaimedPlayerId,
    short NpcType);

public readonly record struct TerrariaTeleportRequest(byte Subtype);

public enum TerrariaRuntimeActionRequestDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}

/// <summary>
/// Adapts the fixed-size protocol-326 boss/invasion and player-teleport requests through Multiplicity's typed packet
/// views. The application layer receives owned values and never reads wire offsets directly.
/// </summary>
public static class TerrariaRuntimeActionRequestDecoder
{
    public const int BossSummonPayloadLength = 4;
    public const int TeleportRequestPayloadLength = 1;

    public static TerrariaRuntimeActionRequestDecodeResult TryDecodeBossSummon(
        in TerrariaFrame frame,
        out TerrariaBossSummonRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SpawnBoss)
            return TerrariaRuntimeActionRequestDecodeResult.WrongMessageId;
        if (frame.Payload.Length != BossSummonPayloadLength)
            return TerrariaRuntimeActionRequestDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeBossSummonPayload(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[BossSummonPayloadLength];
        CopyPayload(frame.Payload, scratch);
        return DecodeBossSummonPayload(scratch, out request);
    }

    public static TerrariaRuntimeActionRequestDecodeResult TryDecodeTeleportRequest(
        in TerrariaFrame frame,
        out TerrariaTeleportRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.TeleportRequest)
            return TerrariaRuntimeActionRequestDecodeResult.WrongMessageId;
        if (frame.Payload.Length != TeleportRequestPayloadLength)
            return TerrariaRuntimeActionRequestDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeTeleportRequestPayload(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[TeleportRequestPayloadLength];
        CopyPayload(frame.Payload, scratch);
        return DecodeTeleportRequestPayload(scratch, out request);
    }

    private static TerrariaRuntimeActionRequestDecodeResult DecodeTeleportRequestPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaTeleportRequest request)
    {
        try
        {
            var view = TeleportationPotionView.FromPayload(payload);
            request = new TerrariaTeleportRequest(view.Subtype);
            return TerrariaRuntimeActionRequestDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            request = default;
            return TerrariaRuntimeActionRequestDecodeResult.Malformed;
        }
    }

    private static TerrariaRuntimeActionRequestDecodeResult DecodeBossSummonPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaBossSummonRequest request)
    {
        try
        {
            var view = SpawnBossorInvasionView.FromPayload(payload);
            request = new TerrariaBossSummonRequest(view.PlayerId, view.Type);
            return TerrariaRuntimeActionRequestDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            request = default;
            return TerrariaRuntimeActionRequestDecodeResult.Malformed;
        }
    }

    private static void CopyPayload(in ReadOnlySequence<byte> payload, Span<byte> destination)
    {
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in payload)
        {
            segment.Span.CopyTo(destination[offset..]);
            offset += segment.Length;
        }
    }
}
