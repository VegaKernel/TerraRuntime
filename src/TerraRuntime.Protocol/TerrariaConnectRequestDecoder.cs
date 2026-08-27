using System.Buffers;

namespace TerraRuntime.Protocol;

public static class TerrariaConnectRequestDecoder
{
    private static ReadOnlySpan<byte> VersionPrefix => "Terraria"u8;

    public static ConnectRequestDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaConnectRequest request)
    {
        request = default;

        if (frame.MessageId != (byte)TerrariaMessageId.Hello)
        {
            return ConnectRequestDecodeResult.WrongMessageId;
        }

        var reader = new SequenceReader<byte>(frame.Payload);
        if (!TryRead7BitEncodedInt(ref reader, out int bannerByteLength) ||
            bannerByteLength <= VersionPrefix.Length ||
            bannerByteLength > TerrariaProtocolVersion.MaximumVersionBannerByteLength ||
            reader.Remaining != bannerByteLength)
        {
            return ConnectRequestDecodeResult.MalformedPayload;
        }

        foreach (byte expected in VersionPrefix)
        {
            if (!reader.TryRead(out byte actual) || actual != expected)
            {
                return ConnectRequestDecodeResult.InvalidVersionBanner;
            }
        }

        int protocol = 0;
        int digitCount = bannerByteLength - VersionPrefix.Length;
        for (int i = 0; i < digitCount; i++)
        {
            if (!reader.TryRead(out byte value) || value is < (byte)'0' or > (byte)'9')
            {
                return ConnectRequestDecodeResult.InvalidVersionBanner;
            }

            int digit = value - (byte)'0';
            if (protocol > (int.MaxValue - digit) / 10)
            {
                return ConnectRequestDecodeResult.InvalidVersionBanner;
            }

            protocol = (protocol * 10) + digit;
        }

        if (reader.Remaining != 0)
        {
            return ConnectRequestDecodeResult.MalformedPayload;
        }

        request = new TerrariaConnectRequest(protocol);
        return ConnectRequestDecodeResult.Decoded;
    }

    private static bool TryRead7BitEncodedInt(ref SequenceReader<byte> reader, out int value)
    {
        uint result = 0;

        for (int shift = 0; shift < 35; shift += 7)
        {
            if (!reader.TryRead(out byte current))
            {
                value = 0;
                return false;
            }

            if (shift == 28 && current > 0x07)
            {
                value = 0;
                return false;
            }

            result |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                value = (int)result;
                return true;
            }
        }

        value = 0;
        return false;
    }
}
