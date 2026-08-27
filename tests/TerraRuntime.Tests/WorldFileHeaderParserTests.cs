using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileHeaderParserTests
{
    [Fact]
    public void Parses_Terraria_1458_world_header_prefix_in_vanilla_order()
    {
        Guid uniqueId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        byte[] file = CreateCurrentWorld(
            name: "TerraRuntime Test",
            seed: "1458-seed",
            generatorVersion: 0x0102030405060708UL,
            uniqueId,
            worldId: 123456,
            widthTiles: 8400,
            heightTiles: 2400);

        WorldFileHeaderParseResult result = WorldFileHeaderParser.TryParse(file, out WorldFileHeader? header);

        Assert.Equal(WorldFileHeaderParseResult.Parsed, result);
        Assert.NotNull(header);
        Assert.Equal("TerraRuntime Test", header.Name);
        Assert.Equal("1458-seed", header.SeedText);
        Assert.Equal(0x0102030405060708UL, header.WorldGeneratorVersion);
        Assert.Equal(uniqueId, header.UniqueId);
        Assert.Equal(123456, header.WorldId);
        Assert.Equal(8400, header.Dimensions.WidthTiles);
        Assert.Equal(2400, header.Dimensions.HeightTiles);
        Assert.Equal(134400, header.RightWorld);
        Assert.Equal(38400, header.BottomWorld);
    }

    [Fact]
    public void Refuses_legacy_layout_until_that_header_version_is_verified()
    {
        byte[] file = CreateCurrentWorld("Legacy", "seed", 1, Guid.Empty, 1, 4200, 1200, formatVersion: 325);

        Assert.Equal(
            WorldFileHeaderParseResult.UnsupportedVersion,
            WorldFileHeaderParser.TryParse(file, out _));
    }

    [Fact]
    public void Rejects_invalid_utf8_in_length_prefixed_world_name()
    {
        byte[] file = CreateCurrentWorld("A", "seed", 1, Guid.Empty, 1, 4200, 1200);
        const int HeaderStart = 64;
        file[HeaderStart] = 1;
        file[HeaderStart + 1] = 0xFF;

        Assert.Equal(
            WorldFileHeaderParseResult.InvalidUtf8,
            WorldFileHeaderParser.TryParse(file, out _));
    }

    private static byte[] CreateCurrentWorld(
        string name,
        string seed,
        ulong generatorVersion,
        Guid uniqueId,
        int worldId,
        int widthTiles,
        int heightTiles,
        int formatVersion = WorldFileFormatPolicy.CurrentVersion)
    {
        const int HeaderStart = 64;
        const int TilesStart = 192;
        var file = new byte[256];

        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), formatVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 7);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), 4);
        offset += sizeof(short);
        foreach (int pointer in new[] { HeaderStart, TilesStart, 224, 240 })
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), 0);

        using var stream = new MemoryStream(file, writable: true);
        stream.Position = HeaderStart;
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(name);
        writer.Write(seed);
        writer.Write(generatorVersion);
        writer.Write(uniqueId.ToByteArray());
        writer.Write(worldId);
        writer.Write(0);
        writer.Write(checked(widthTiles * 16));
        writer.Write(0);
        writer.Write(checked(heightTiles * 16));
        writer.Write(heightTiles);
        writer.Write(widthTiles);
        writer.Flush();

        Assert.True(stream.Position <= TilesStart, "Header fixture exceeded its declared section boundary.");
        return file;
    }
}
