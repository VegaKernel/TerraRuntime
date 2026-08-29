using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileHeaderPrefixEncoderTests
{
    [Fact]
    public void Encode_matches_binarywriter_header_prefix_contract()
    {
        string name = new('W', 128);
        var header = new WorldFileHeader(
            name,
            "seed-1458",
            0x0102030405060708UL,
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            314159,
            0,
            67200,
            0,
            19200,
            new WorldDimensions(4200, 1200));
        using var encoded = new MemoryStream();

        WorldFileHeaderPrefixEncodeResult result =
            WorldFileHeaderPrefixEncoder.TryEncode(header, encoded, out long bytesWritten);

        Assert.Equal(WorldFileHeaderPrefixEncodeResult.Encoded, result);
        Assert.Equal(encoded.Length, bytesWritten);

        encoded.Position = 0;
        using var reader = new BinaryReader(encoded, new UTF8Encoding(false, true), leaveOpen: true);
        Assert.Equal(name, reader.ReadString());
        Assert.Equal(header.SeedText, reader.ReadString());
        Assert.Equal(header.WorldGeneratorVersion, reader.ReadUInt64());
        Assert.Equal(header.UniqueId, new Guid(reader.ReadBytes(16)));
        Assert.Equal(header.WorldId, reader.ReadInt32());
        Assert.Equal(header.LeftWorld, reader.ReadInt32());
        Assert.Equal(header.RightWorld, reader.ReadInt32());
        Assert.Equal(header.TopWorld, reader.ReadInt32());
        Assert.Equal(header.BottomWorld, reader.ReadInt32());
        Assert.Equal(header.Dimensions.HeightTiles, reader.ReadInt32());
        Assert.Equal(header.Dimensions.WidthTiles, reader.ReadInt32());
        Assert.Equal(encoded.Length, encoded.Position);
    }

    [Fact]
    public void Encode_rejects_strings_larger_than_parser_budget()
    {
        var header = new WorldFileHeader(
            new string('x', (4 * 1024) + 1),
            "seed",
            0,
            Guid.Empty,
            1,
            0,
            16,
            0,
            16,
            new WorldDimensions(1, 1));
        using var encoded = new MemoryStream();

        WorldFileHeaderPrefixEncodeResult result =
            WorldFileHeaderPrefixEncoder.TryEncode(header, encoded, out long bytesWritten);

        Assert.Equal(WorldFileHeaderPrefixEncodeResult.StringTooLarge, result);
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, encoded.Length);
    }

    [Fact]
    public void Encode_rejects_invalid_utf16_without_writing()
    {
        var header = new WorldFileHeader(
            "bad\ud800",
            "seed",
            0,
            Guid.Empty,
            1,
            0,
            16,
            0,
            16,
            new WorldDimensions(1, 1));
        using var encoded = new MemoryStream();

        WorldFileHeaderPrefixEncodeResult result =
            WorldFileHeaderPrefixEncoder.TryEncode(header, encoded, out long bytesWritten);

        Assert.Equal(WorldFileHeaderPrefixEncodeResult.InvalidString, result);
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, encoded.Length);
    }
}
