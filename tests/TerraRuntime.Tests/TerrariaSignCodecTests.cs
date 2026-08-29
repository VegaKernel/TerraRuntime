using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaSignCodecTests
{
    [Fact]
    public void Read_request_decodes_exact_protocol_326_payload()
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0, 2), 1234);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2, 2), -321);
        TerrariaFrame frame = Frame(TerrariaMessageId.RequestSign, payload);

        TerrariaSignDecodeResult result = TerrariaSignCodec.TryDecodeReadRequest(
            in frame,
            out TerrariaSignReadRequest request);

        Assert.Equal(TerrariaSignDecodeResult.Decoded, result);
        Assert.Equal(new TerrariaSignReadRequest(1234, -321), request);
    }

    [Fact]
    public void Sign_state_roundtrips_exact_fields_and_utf8_text()
    {
        var expected = new TerrariaSignState(31999, 240, 481, "табличка λ", 7, 1);

        byte[] encoded = TerrariaSignCodec.EncodeState(in expected);
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.True(buffer.IsEmpty);

        TerrariaSignDecodeResult result = TerrariaSignCodec.TryDecodeState(in frame, out TerrariaSignState actual);

        Assert.Equal(TerrariaSignDecodeResult.Decoded, result);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Read_request_rejects_non_exact_payload_length()
    {
        TerrariaFrame frame = Frame(TerrariaMessageId.RequestSign, new byte[5]);

        TerrariaSignDecodeResult result = TerrariaSignCodec.TryDecodeReadRequest(in frame, out _);

        Assert.Equal(TerrariaSignDecodeResult.InvalidPayloadLength, result);
    }

    [Fact]
    public void Sign_state_rejects_trailing_bytes()
    {
        var state = new TerrariaSignState(2, 10, 20, "x", 3, 0);
        byte[] encoded = TerrariaSignCodec.EncodeState(in state);
        byte[] payload = new byte[encoded.Length - TerrariaFrameDecoderOptions.MinimumFrameLength + 1];
        encoded.AsSpan(TerrariaFrameDecoderOptions.MinimumFrameLength).CopyTo(payload);
        payload[^1] = 0x7F;
        TerrariaFrame frame = Frame(TerrariaMessageId.SignNew, payload);

        TerrariaSignDecodeResult result = TerrariaSignCodec.TryDecodeState(in frame, out _);

        Assert.Equal(TerrariaSignDecodeResult.Malformed, result);
    }

    [Fact]
    public void Sign_codec_rejects_wrong_message_ids()
    {
        TerrariaFrame read = Frame(TerrariaMessageId.PlayerControls, new byte[4]);
        TerrariaFrame state = Frame(TerrariaMessageId.PlayerControls, new byte[9]);

        Assert.Equal(
            TerrariaSignDecodeResult.WrongMessageId,
            TerrariaSignCodec.TryDecodeReadRequest(in read, out _));
        Assert.Equal(
            TerrariaSignDecodeResult.WrongMessageId,
            TerrariaSignCodec.TryDecodeState(in state, out _));
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));
}
