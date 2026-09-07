using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerPvpBuffCodec1458Tests
{
    [Fact]
    public void Encode_matches_TerrariaServer_1458_packet_55_golden_bytes()
    {
        byte[] encoded = TerrariaPlayerPvpBuffCodec1458.Encode(
            targetPlayer: 7,
            VanillaBuffIds.OnFire,
            durationTicks: 180);

        Assert.Equal(
            new byte[]
            {
                10, 0, 55,
                7,
                24, 0,
                180, 0, 0, 0
            },
            encoded);
    }

    [Fact]
    public void Decode_accepts_exact_target_buff_and_duration()
    {
        TerrariaFrame frame = Frame(TerrariaMessageId.AddPlayerBuffPvp, [9, 20, 0, 88, 2, 0, 0]);

        TerrariaPlayerPvpBuffDecodeResult result = TerrariaPlayerPvpBuffCodec1458.TryDecode(
            in frame,
            out byte target,
            out BuffTypeId buff,
            out int duration);

        Assert.Equal(TerrariaPlayerPvpBuffDecodeResult.Decoded, result);
        Assert.Equal((byte)9, target);
        Assert.Equal(VanillaBuffIds.Poisoned, buff);
        Assert.Equal(600, duration);
    }

    [Fact]
    public void Decode_rejects_wrong_size_unknown_buff_and_nonpositive_duration()
    {
        TerrariaFrame wrongSize = Frame(TerrariaMessageId.AddPlayerBuffPvp, [1, 24, 0]);
        TerrariaFrame unknownBuff = Frame(TerrariaMessageId.AddPlayerBuffPvp, [1, 145, 1, 1, 0, 0, 0]); // 401
        TerrariaFrame zeroDuration = Frame(TerrariaMessageId.AddPlayerBuffPvp, [1, 24, 0, 0, 0, 0, 0]);

        Assert.Equal(
            TerrariaPlayerPvpBuffDecodeResult.InvalidPayloadLength,
            TerrariaPlayerPvpBuffCodec1458.TryDecode(in wrongSize, out _, out _, out _));
        Assert.Equal(
            TerrariaPlayerPvpBuffDecodeResult.InvalidBuffType,
            TerrariaPlayerPvpBuffCodec1458.TryDecode(in unknownBuff, out _, out _, out _));
        Assert.Equal(
            TerrariaPlayerPvpBuffDecodeResult.InvalidDuration,
            TerrariaPlayerPvpBuffCodec1458.TryDecode(in zeroDuration, out _, out _, out _));
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));
}
