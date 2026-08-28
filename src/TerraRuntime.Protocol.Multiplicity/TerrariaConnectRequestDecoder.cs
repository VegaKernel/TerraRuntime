using System.Buffers;
using System.IO;
using global::Multiplicity.Packets.Views;

namespace TerraRuntime.Protocol;

/// <summary>
/// Bounded protocol-326 handshake adapter. Multiplicity owns the wire layout;
/// TerraRuntime only validates protocol-banner semantics and projects the release number.
/// </summary>
public static class TerrariaConnectRequestDecoder
{
    private const int MaximumPayloadLength = TerrariaProtocolVersion.MaximumVersionBannerByteLength + 1;
    private const string VersionPrefix = "Terraria";

    public static ConnectRequestDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaConnectRequest request)
    {
        request = default;

        if (frame.MessageId != (byte)TerrariaMessageId.Hello)
            return ConnectRequestDecodeResult.WrongMessageId;

        long payloadLength = frame.Payload.Length;
        if (payloadLength is <= 0 or > MaximumPayloadLength)
            return ConnectRequestDecodeResult.MalformedPayload;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[MaximumPayloadLength];
        frame.Payload.CopyTo(scratch);
        return DecodePayload(scratch[..checked((int)payloadLength)], out request);
    }

    private static ConnectRequestDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaConnectRequest request)
    {
        request = default;

        string version;
        try
        {
            version = ConnectRequestView.FromPayload(payload).Version;
        }
        catch (InvalidDataException)
        {
            return ConnectRequestDecodeResult.MalformedPayload;
        }

        if (version.Length <= VersionPrefix.Length ||
            !version.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return ConnectRequestDecodeResult.InvalidVersionBanner;
        }

        int protocol = 0;
        foreach (char value in version.AsSpan(VersionPrefix.Length))
        {
            if (value is < '0' or > '9')
                return ConnectRequestDecodeResult.InvalidVersionBanner;

            int digit = value - '0';
            if (protocol > (int.MaxValue - digit) / 10)
                return ConnectRequestDecodeResult.InvalidVersionBanner;

            protocol = (protocol * 10) + digit;
        }

        request = new TerrariaConnectRequest(protocol);
        return ConnectRequestDecodeResult.Decoded;
    }
}
