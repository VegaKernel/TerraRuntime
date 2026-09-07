using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaPlayerBuffCodec1458Tests
{
    [Fact]
    public void Encode_matches_TerrariaServer_1458_packet_50_golden_bytes()
    {
        byte[] encoded = TerrariaPlayerBuffCodec1458.Encode(
            player: 7,
            [VanillaBuffIds.OnFire, VanillaBuffIds.Slow]);

        Assert.Equal(
            new byte[]
            {
                10, 0, 50,
                7,
                24, 0,
                32, 0,
                0, 0
            },
            encoded);
    }

    [Fact]
    public void Decode_accepts_ordered_buff_types_and_explicit_zero_terminator()
    {
        TerrariaFrame frame = Frame(
            TerrariaMessageId.PlayerBuffs,
            [9, 24, 0, 32, 0, 0, 0]);

        TerrariaPlayerBuffDecodeResult result = TerrariaPlayerBuffCodec1458.TryDecode(
            in frame,
            out byte claimedPlayer,
            out BuffTypeId[] buffs);

        Assert.Equal(TerrariaPlayerBuffDecodeResult.Decoded, result);
        Assert.Equal((byte)9, claimedPlayer);
        Assert.Equal([VanillaBuffIds.OnFire, VanillaBuffIds.Slow], buffs);
    }

    [Fact]
    public void Decode_rejects_missing_terminator_embedded_terminator_and_unknown_buff()
    {
        TerrariaFrame missingTerminator = Frame(TerrariaMessageId.PlayerBuffs, [1, 24, 0]);
        TerrariaFrame embeddedTerminator = Frame(TerrariaMessageId.PlayerBuffs, [1, 24, 0, 0, 0, 32, 0]);
        TerrariaFrame unknownBuff = Frame(TerrariaMessageId.PlayerBuffs, [1, 145, 1, 0, 0]); // 401

        Assert.Equal(
            TerrariaPlayerBuffDecodeResult.Malformed,
            TerrariaPlayerBuffCodec1458.TryDecode(in missingTerminator, out _, out _));
        Assert.Equal(
            TerrariaPlayerBuffDecodeResult.Malformed,
            TerrariaPlayerBuffCodec1458.TryDecode(in embeddedTerminator, out _, out _));
        Assert.Equal(
            TerrariaPlayerBuffDecodeResult.InvalidBuffType,
            TerrariaPlayerBuffCodec1458.TryDecode(in unknownBuff, out _, out _));
    }

    [Fact]
    public void Decode_rejects_more_than_Player_maxBuffs_entries()
    {
        byte[] payload = new byte[1 + ((TerrariaPlayerBuffCodec1458.MaximumBuffs + 2) * sizeof(ushort))];
        payload[0] = 3;
        for (int i = 0; i < TerrariaPlayerBuffCodec1458.MaximumBuffs + 1; i++)
        {
            ushort buff = checked((ushort)((i % (VanillaBuffIds.Count - 1)) + 1));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1 + i * 2), buff);
        }
        // trailing ushort remains zero
        TerrariaFrame frame = Frame(TerrariaMessageId.PlayerBuffs, payload);

        Assert.Equal(
            TerrariaPlayerBuffDecodeResult.InvalidPayloadLength,
            TerrariaPlayerBuffCodec1458.TryDecode(in frame, out _, out _));
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));
}
