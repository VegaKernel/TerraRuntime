using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFilePressurePlateEncoderTests
{
    [Fact]
    public void Roundtrips_canonical_pressure_plate_section_through_current_decoder()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldPressurePlate[] source =
        [
            new WorldPressurePlate(1, 2),
            new WorldPressurePlate(8, 9)
        ];

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFilePressurePlateEncodeResult.Encoded,
            WorldFilePressurePlateEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, 0, 0, 0, section.Length],
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
            WorldFilePressurePlateDecodeResult.Decoded,
            WorldFilePressurePlateDecoder.TryDecode(
                section,
                envelope,
                header,
                maxPressurePlates: 4,
                out WorldPressurePlate[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void Rejects_duplicate_coordinates_before_writing()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldPressurePlate[] source =
        [
            new WorldPressurePlate(3, 4),
            new WorldPressurePlate(3, 4)
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFilePressurePlateEncodeResult.DuplicateCoordinates,
            WorldFilePressurePlateEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Rejects_out_of_bounds_coordinates_before_writing()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldPressurePlate[] source = [new WorldPressurePlate(10, 0)];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFilePressurePlateEncodeResult.InvalidCoordinates,
            WorldFilePressurePlateEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }
}
