using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaTravelShopCodecTests
{
    [Fact]
    public void Packet72_roundtrips_exact_40_slot_payload()
    {
        short[] items = Enumerable.Range(0, TerrariaTravelShopCodec.SlotCount)
            .Select(static i => checked((short)(2200 + i)))
            .ToArray();

        Assert.Equal(TerrariaTravelShopEncodeResult.Encoded, TerrariaTravelShopCodec.TryEncode(items, out byte[] encoded));
        Assert.Equal(83, encoded.Length); // 2-byte length + packet id + 80-byte payload.
        Assert.Equal((byte)TerrariaMessageId.TravelShop, encoded[2]);

        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.True(sequence.IsEmpty);
        Span<short> decoded = stackalloc short[TerrariaTravelShopCodec.SlotCount];
        Assert.Equal(TerrariaTravelShopDecodeResult.Decoded, TerrariaTravelShopCodec.TryDecode(in frame, decoded));
        Assert.Equal(items, decoded.ToArray());
    }

    [Fact]
    public void Packet72_rejects_wrong_length_and_negative_item_identity()
    {
        Assert.Equal(
            TerrariaTravelShopEncodeResult.InvalidInventoryLength,
            TerrariaTravelShopCodec.TryEncode(new short[39], out byte[] shortFrame));
        Assert.Empty(shortFrame);

        short[] items = new short[TerrariaTravelShopCodec.SlotCount];
        items[7] = -1;
        Assert.Equal(
            TerrariaTravelShopEncodeResult.InvalidItemId,
            TerrariaTravelShopCodec.TryEncode(items, out byte[] invalidFrame));
        Assert.Empty(invalidFrame);
    }
}
