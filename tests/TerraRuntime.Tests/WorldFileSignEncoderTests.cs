using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileSignEncoderTests
{
    [Fact]
    public void Roundtrips_canonical_sign_section_through_current_decoder()
    {
        var dimensions = new WorldDimensions(20, 20);
        WorldSign[] source =
        [
            new WorldSign(0, "alpha", 1, 2),
            new WorldSign(1, "βета", 4, 5)
        ];

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileSignEncodeResult.Encoded,
            WorldFileSignEncoder.TryEncode(
                source,
                dimensions,
                maxTextBytesPerSign: 64 * 1024,
                maxTotalTextBytes: 64L * 1024 * 1024,
                stream,
                out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var header = new WorldFileHeader(
            "test",
            "seed",
            1,
            Guid.Empty,
            1,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);

        Assert.Equal(
            WorldFileSignDecodeResult.Decoded,
            WorldFileSignDecoder.TryDecode(
                section,
                envelope,
                header,
                maxTextBytesPerSign: 64 * 1024,
                maxTotalTextBytes: 64L * 1024 * 1024,
                out WorldSign[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void Rejects_sparse_slot_identity_before_writing()
    {
        var dimensions = new WorldDimensions(20, 20);
        WorldSign[] source =
        [
            new WorldSign(0, "first", 1, 2),
            new WorldSign(2, "hole", 4, 5)
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileSignEncodeResult.NonCanonicalSlotOrder,
            WorldFileSignEncoder.TryEncode(source, dimensions, 1024, 4096, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Allows_duplicate_coordinates_and_rejects_text_budget_before_writing()
    {
        var dimensions = new WorldDimensions(20, 20);
        using var duplicateStream = new MemoryStream();
        WorldSign[] duplicates =
        [
            new WorldSign(0, "first", 1, 2),
            new WorldSign(1, "second", 1, 2)
        ];

        Assert.Equal(
            WorldFileSignEncodeResult.Encoded,
            WorldFileSignEncoder.TryEncode(duplicates, dimensions, 1024, 4096, duplicateStream, out long duplicateBytes));
        Assert.Equal(duplicateStream.Length, duplicateBytes);
        Assert.True(duplicateBytes > 0);

        byte[] section = duplicateStream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var header = new WorldFileHeader(
            "test",
            "seed",
            1,
            Guid.Empty,
            1,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);
        Assert.Equal(
            WorldFileSignDecodeResult.Decoded,
            WorldFileSignDecoder.TryDecode(
                section,
                envelope,
                header,
                maxTextBytesPerSign: 1024,
                maxTotalTextBytes: 4096,
                out WorldSign[] decoded,
                out int consumed));
        Assert.Equal(section.Length, consumed);
        WorldSign surviving = Assert.Single(decoded);
        Assert.Equal((short)0, surviving.SlotId);
        Assert.Equal("first", surviving.Text);

        using var budgetStream = new MemoryStream();
        WorldSign[] oversized = [new WorldSign(0, "12345", 1, 2)];
        Assert.Equal(
            WorldFileSignEncodeResult.TextBudgetExceeded,
            WorldFileSignEncoder.TryEncode(oversized, dimensions, 4, 4096, budgetStream, out long budgetBytes));
        Assert.Equal(0, budgetBytes);
        Assert.Equal(0, budgetStream.Length);
    }
}
