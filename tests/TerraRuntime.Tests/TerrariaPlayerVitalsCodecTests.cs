using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerVitalsCodecTests
{
    [Fact]
    public void Round_trips_packet16_through_multiplicity()
    {
        var health = new TerrariaPlayerHealthState(PlayerId: 9, Life: 123, MaxLife: 400);

        byte[] encoded = TerrariaPlayerVitalsCodec.EncodeHealth(in health);
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.True(input.IsEmpty);
        Assert.Equal((byte)TerrariaMessageId.PlayerHp, frame.MessageId);
        Assert.Equal(
            TerrariaPlayerHealthDecodeResult.Decoded,
            TerrariaPlayerVitalsCodec.TryDecodeHealth(frame, out TerrariaPlayerHealthState decoded));
        Assert.Equal(health, decoded);
    }

    [Fact]
    public void Round_trips_packet42_through_multiplicity()
    {
        var mana = new TerrariaPlayerManaState(PlayerId: 7, Mana: 81, MaxMana: 200);

        byte[] encoded = TerrariaPlayerVitalsCodec.EncodeMana(in mana);
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        Assert.True(input.IsEmpty);
        Assert.Equal((byte)TerrariaMessageId.PlayerMana, frame.MessageId);
        Assert.Equal(
            TerrariaPlayerManaDecodeResult.Decoded,
            TerrariaPlayerVitalsCodec.TryDecodeMana(frame, out TerrariaPlayerManaState decoded));
        Assert.Equal(mana, decoded);
    }

    [Theory]
    [InlineData(TerrariaMessageId.PlayerHp)]
    [InlineData(TerrariaMessageId.PlayerMana)]
    public void Rejects_vitals_payloads_with_trailing_bytes(TerrariaMessageId messageId)
    {
        byte[] encoded = new byte[TerrariaPlayerVitalsCodec.PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength + 1];
        int frameLength = encoded.Length;
        encoded[0] = (byte)frameLength;
        encoded[1] = (byte)(frameLength >> 8);
        encoded[2] = (byte)messageId;
        var input = new ReadOnlySequence<byte>(encoded);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        if (messageId == TerrariaMessageId.PlayerHp)
        {
            Assert.Equal(
                TerrariaPlayerHealthDecodeResult.InvalidPayloadLength,
                TerrariaPlayerVitalsCodec.TryDecodeHealth(frame, out _));
        }
        else
        {
            Assert.Equal(
                TerrariaPlayerManaDecodeResult.InvalidPayloadLength,
                TerrariaPlayerVitalsCodec.TryDecodeMana(frame, out _));
        }
    }
}
