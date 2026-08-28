using System.IO;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Adapts Multiplicity's protocol-326 packet views into TerraRuntime-owned join requests.
/// Multiplicity remains the source of truth for packet 8/12 wire layout; this layer only
/// handles bounded frame segmentation and converts parsed fields into protocol-neutral values.
/// </summary>
public static class TerrariaJoinRequestDecoder
{
    public const int WorldRequestPayloadLength = 0;
    public const int SectionRequestPayloadLength = 9;
    public const int PlayerSpawnPayloadLength = 15;

    public static TerrariaJoinDecodeResult TryDecodeWorldRequest(in TerrariaFrame frame)
    {
        if (frame.MessageId != (byte)TerrariaMessageId.RequestWorldData)
            return TerrariaJoinDecodeResult.WrongMessageId;

        // Packet 6 is payload-free in protocol 326. Avoid ContinueConnecting2View here because
        // that generic materializer allocates an object/array for a packet with no fields.
        return frame.Payload.Length == WorldRequestPayloadLength
            ? TerrariaJoinDecodeResult.Decoded
            : TerrariaJoinDecodeResult.InvalidPayloadLength;
    }

    public static TerrariaJoinDecodeResult TryDecodeSectionRequest(
        in TerrariaFrame frame,
        out TerrariaSectionBootstrapRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SpawnTileData)
            return TerrariaJoinDecodeResult.WrongMessageId;
        if (frame.Payload.Length != SectionRequestPayloadLength)
            return TerrariaJoinDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeSectionPayload(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[SectionRequestPayloadLength];
        CopyPayload(frame, scratch);
        return DecodeSectionPayload(scratch, out request);
    }

    public static TerrariaJoinDecodeResult TryDecodePlayerSpawn(
        in TerrariaFrame frame,
        out TerrariaPlayerSpawnRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerSpawn)
            return TerrariaJoinDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PlayerSpawnPayloadLength)
            return TerrariaJoinDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeSpawnPayload(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[PlayerSpawnPayloadLength];
        CopyPayload(frame, scratch);
        return DecodeSpawnPayload(scratch, out request);
    }

    private static TerrariaJoinDecodeResult DecodeSectionPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaSectionBootstrapRequest request)
    {
        try
        {
            var view = TileGetSectionView.FromPayload(payload);
            request = new TerrariaSectionBootstrapRequest(view.X, view.Y, view.Team);
            return TerrariaJoinDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            request = default;
            return TerrariaJoinDecodeResult.Truncated;
        }
    }

    private static TerrariaJoinDecodeResult DecodeSpawnPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlayerSpawnRequest request)
    {
        try
        {
            var view = PlayerSpawnView.FromPayload(payload);
            request = new TerrariaPlayerSpawnRequest(
                view.PlayerId,
                view.SpawnX,
                view.SpawnY,
                view.RespawnTimer,
                view.NumberOfDeathsPVE,
                view.NumberOfDeathsPVP,
                view.Team,
                view.SpawnContext);
            return TerrariaJoinDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            request = default;
            return TerrariaJoinDecodeResult.Truncated;
        }
    }

    private static void CopyPayload(in TerrariaFrame frame, Span<byte> destination)
    {
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(destination[offset..]);
            offset += segment.Length;
        }
    }
}
