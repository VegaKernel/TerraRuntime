using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaPlayerZonesDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

/// <summary>Source packet36: byte player, five raw zone bytes, byte townNPCs.</summary>
public static class TerrariaPlayerZonesCodec1458
{
    public const int PayloadLength = 7;
    public const int FrameLength = TerrariaFrameDecoderOptions.MinimumFrameLength + PayloadLength;

    public static TerrariaPlayerZonesDecodeResult TryDecode(in TerrariaFrame frame, out byte claimedPlayer,
        out PlayerZoneSnapshot1458 zones)
    {
        claimedPlayer = 0;
        zones = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SyncPlayerZone)
            return TerrariaPlayerZonesDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerZonesDecodeResult.InvalidPayloadLength;
        Span<byte> payload = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (var segment in frame.Payload)
        {
            segment.Span.CopyTo(payload[offset..]);
            offset += segment.Length;
        }
        claimedPlayer = payload[0];
        zones = new(payload[1], payload[2], payload[3], payload[4], payload[5], payload[6]);
        return TerrariaPlayerZonesDecodeResult.Decoded;
    }

    public static byte[] Encode(byte player, in PlayerZoneSnapshot1458 zones)
    {
        ReadOnlySpan<byte> payload = [player, zones.Zone1, zones.Zone2, zones.Zone3, zones.Zone4, zones.Zone5, zones.TownNpcCount];
        byte[] encoded = new byte[FrameLength];
        _ = TerrariaFrameEncoder.TryWrite(encoded, (byte)TerrariaMessageId.SyncPlayerZone, payload);
        return encoded;
    }
}
