using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileChestEncoderTests
{
    [Fact]
    public void Roundtrips_canonical_chest_section_through_current_decoder()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldChest[] source =
        [
            new WorldChest(
                0,
                1,
                2,
                "alpha",
                [new WorldChestItem(5, 1, 3), default]),
            new WorldChest(
                1,
                4,
                5,
                "βeta",
                [new WorldChestItem(1, 1, 0)])
        ];

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileChestEncodeResult.Encoded,
            WorldFileChestEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var header = new WorldFileHeader(
            "test",
            "seed",
            1,
            Guid.Empty,
            1,
            0,
            160,
            0,
            160,
            dimensions);

        Assert.Equal(
            WorldFileChestDecodeResult.Decoded,
            WorldFileChestDecoder.TryDecode(
                section,
                envelope,
                header,
                maxItemsPerChest: byte.MaxValue + 1,
                maxTotalItems: 2 * (byte.MaxValue + 1L),
                out WorldChest[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source.Length, decoded.Length);
        for (int index = 0; index < source.Length; index++)
        {
            Assert.Equal(source[index].SlotId, decoded[index].SlotId);
            Assert.Equal(source[index].X, decoded[index].X);
            Assert.Equal(source[index].Y, decoded[index].Y);
            Assert.Equal(source[index].Name, decoded[index].Name);
            Assert.Equal(source[index].Items, decoded[index].Items);
        }
    }

    [Fact]
    public void Rejects_sparse_slot_identity_before_writing()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldChest[] source =
        [
            new WorldChest(0, 1, 2, "first", []),
            new WorldChest(2, 4, 5, "hole", [])
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileChestEncodeResult.NonCanonicalSlotOrder,
            WorldFileChestEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Rejects_noncanonical_item_state_before_writing()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldChest[] source =
        [
            new WorldChest(
                0,
                1,
                2,
                "bad-stack",
                [new WorldChestItem(short.MaxValue + 1, 1, 0)])
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileChestEncodeResult.InvalidItemState,
            WorldFileChestEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }
}
