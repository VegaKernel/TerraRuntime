using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileFooterEncoderTests
{
    [Fact]
    public void Roundtrips_footer_through_current_decoder()
    {
        var dimensions = new WorldDimensions(40, 30);
        WorldFileHeader header = Header("footer-world", worldId: 123, dimensions);
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileFooterEncodeResult.Encoded,
            WorldFileFooterEncoder.TryEncode(header, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        Assert.Equal(
            WorldFileFooterDecodeResult.Decoded,
            WorldFileFooterDecoder.TryDecode(
                section,
                header,
                out WorldFileFooter footer,
                out int consumed));
        Assert.Equal(section.Length, consumed);
        Assert.True(footer.IsValid);
    }

    [Fact]
    public void Rejects_header_with_unencodable_world_name_before_writing()
    {
        var dimensions = new WorldDimensions(40, 30);
        using var invalidStream = new MemoryStream();

        Assert.Equal(
            WorldFileFooterEncodeResult.InvalidWorldName,
            WorldFileFooterEncoder.TryEncode(
                Header("\uD800", worldId: 1, dimensions),
                invalidStream,
                out long invalidBytes));
        Assert.Equal(0, invalidBytes);
        Assert.Equal(0, invalidStream.Length);

        using var oversizedStream = new MemoryStream();
        Assert.Equal(
            WorldFileFooterEncodeResult.WorldNameTooLarge,
            WorldFileFooterEncoder.TryEncode(
                Header(new string('a', 4097), worldId: 1, dimensions),
                oversizedStream,
                out long oversizedBytes));
        Assert.Equal(0, oversizedBytes);
        Assert.Equal(0, oversizedStream.Length);
    }

    private static WorldFileHeader Header(string name, ulong worldId, WorldDimensions dimensions) =>
        new(
            name,
            "seed",
            worldId,
            Guid.Empty,
            1,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);
}
