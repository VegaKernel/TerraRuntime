using System.Buffers;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectRequestDecoderTests
{
    [Fact]
    public void Decodes_the_Terraria_326_golden_handshake()
    {
        byte[] packet =
        [
            15, 0,
            (byte)TerrariaMessageId.Hello,
            11,
            (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
            (byte)'3', (byte)'2', (byte)'6'
        ];
        var buffer = new ReadOnlySequence<byte>(packet);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(ConnectRequestDecodeResult.Decoded, TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request));
        Assert.Equal(326, request.ProtocolRelease);
        Assert.True(request.IsCurrentProtocol);
    }

    [Fact]
    public void Parses_an_older_release_without_silently_accepting_it_as_current()
    {
        TerrariaFrame frame = CreateHello("Terraria325"u8);

        Assert.Equal(ConnectRequestDecodeResult.Decoded, TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request));
        Assert.Equal(325, request.ProtocolRelease);
        Assert.False(request.IsCurrentProtocol);
    }

    [Fact]
    public void Rejects_a_non_Terraria_banner()
    {
        TerrariaFrame frame = CreateHello("Notaria326"u8);

        Assert.Equal(ConnectRequestDecodeResult.InvalidVersionBanner, TerrariaConnectRequestDecoder.TryDecode(frame, out _));
    }

    [Fact]
    public void Rejects_non_decimal_protocol_suffixes()
    {
        TerrariaFrame frame = CreateHello("Terraria32x"u8);

        Assert.Equal(ConnectRequestDecodeResult.InvalidVersionBanner, TerrariaConnectRequestDecoder.TryDecode(frame, out _));
    }

    [Fact]
    public void Rejects_trailing_bytes_outside_the_declared_string()
    {
        byte[] payload = [11, .. "Terraria326"u8.ToArray(), 0xFF];
        var frame = new TerrariaFrame(
            (ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length),
            (byte)TerrariaMessageId.Hello,
            default,
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(ConnectRequestDecodeResult.MalformedPayload, TerrariaConnectRequestDecoder.TryDecode(frame, out _));
    }

    [Fact]
    public void Rejects_a_banner_above_the_hard_limit_before_allocating_for_it()
    {
        byte[] payload = [33, .. new byte[33]];
        var frame = new TerrariaFrame(
            (ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length),
            (byte)TerrariaMessageId.Hello,
            default,
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(ConnectRequestDecodeResult.MalformedPayload, TerrariaConnectRequestDecoder.TryDecode(frame, out _));
    }

    [Fact]
    public void Rejects_the_hello_decoder_for_other_message_ids()
    {
        TerrariaFrame frame = CreateHello("Terraria326"u8) with
        {
            MessageId = (byte)TerrariaMessageId.PlayerInfo
        };

        Assert.Equal(ConnectRequestDecodeResult.WrongMessageId, TerrariaConnectRequestDecoder.TryDecode(frame, out _));
    }

    private static TerrariaFrame CreateHello(ReadOnlySpan<byte> banner)
    {
        Assert.True(banner.Length < 128);
        byte[] payload = new byte[banner.Length + 1];
        payload[0] = (byte)banner.Length;
        banner.CopyTo(payload.AsSpan(1));

        return new TerrariaFrame(
            (ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length),
            (byte)TerrariaMessageId.Hello,
            default,
            new ReadOnlySequence<byte>(payload));
    }
}
