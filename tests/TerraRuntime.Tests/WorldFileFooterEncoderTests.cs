using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileFooterEncoderTests
{
    [Fact]
    public void Encoded_footer_validates_against_current_footer_contract()
    {
        var dimensions = new WorldDimensions(40, 30);
        WorldFileHeader header = Header("Мир", worldId: 1234, dimensions);
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileFooterEncodeResult.Encoded,
            WorldFileFooterEncoder.TryEncode(header, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] footer = stream.ToArray();
        int[] offsets = new int[VanillaWorldFormat326.SectionCount];
        offsets[^1] = 0;
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: offsets,
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

        Assert.Equal(
            WorldFileFooterValidationResult.Valid,
            WorldFileFooterValidator.Validate(footer, envelope, header, out int consumed));
        Assert.Equal(footer.Length, consumed);
    }

    [Fact]
    public void Footer_validator_observes_world_identity_from_encoded_header_values()
    {
        var dimensions = new WorldDimensions(40, 30);
        WorldFileHeader encodedHeader = Header("world-a", worldId: 10, dimensions);
        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileFooterEncodeResult.Encoded,
            WorldFileFooterEncoder.TryEncode(encodedHeader, stream, out _));

        byte[] footer = stream.ToArray();
        int[] offsets = new int[VanillaWorldFormat326.SectionCount];
        offsets[^1] = 0;
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: offsets,
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

        Assert.Equal(
            WorldFileFooterValidationResult.WorldNameMismatch,
            WorldFileFooterValidator.Validate(
                footer,
                envelope,
                Header("world-b", worldId: 10, dimensions),
                out _));
        Assert.Equal(
            WorldFileFooterValidationResult.WorldIdMismatch,
            WorldFileFooterValidator.Validate(
                footer,
                envelope,
                Header("world-a", worldId: 11, dimensions),
                out _));
    }

    [Fact]
    public void Rejects_invalid_and_oversized_world_names_before_writing()
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

    private static WorldFileHeader Header(string name, int worldId, WorldDimensions dimensions) =>
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
