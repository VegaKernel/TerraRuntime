using TerraRuntime.Schematics;

namespace TerraRuntime.Tests;

public sealed class SchematicBinaryTests
{
    [Fact]
    public void Representative_scene_round_trips_deterministically()
    {
        SchematicDocument expected = CreateRepresentativeDocument();

        byte[] encoded = SchematicBinary.Serialize(expected);
        SchematicDocument actual = SchematicBinary.Deserialize(encoded);
        byte[] reencoded = SchematicBinary.Serialize(actual);

        Assert.Equal(encoded, reencoded);
        Assert.Equal(3, actual.Width);
        Assert.Equal(2, actual.Height);
        Assert.Equal(6, actual.Tiles.Length);
        Assert.Single(actual.Chests);
        Assert.Equal("loot", actual.Chests[0].Name);
        Assert.Equal(2, actual.Chests[0].Items.Length);
        Assert.Single(actual.Signs);
        Assert.Equal(2, actual.TileEntities.Length);
        Assert.Single(actual.Npcs);
        Assert.Equal("Guide", actual.Npcs[0].Name);
        Assert.False(actual.Npcs[0].Homeless);
        Assert.Equal(100, actual.Npcs[0].LifeOverride);
        Assert.Single(actual.WorldItems);
        Assert.Equal(2, actual.Markers.Length);
        Assert.Single(actual.Metadata);
    }

    [Fact]
    public void Corrupted_section_checksum_is_rejected()
    {
        byte[] encoded = SchematicBinary.Serialize(CreateRepresentativeDocument());
        encoded[^1] ^= 0x5A;

        Assert.Throws<SchematicFormatException>(() => SchematicBinary.Deserialize(encoded));
    }

    [Fact]
    public void Truncated_file_is_rejected()
    {
        byte[] encoded = SchematicBinary.Serialize(CreateRepresentativeDocument());
        byte[] truncated = encoded[..^1];

        Assert.Throws<SchematicFormatException>(() => SchematicBinary.Deserialize(truncated));
    }

    [Fact]
    public void Invalid_tile_count_is_rejected_before_serialization()
    {
        var invalid = new SchematicDocument
        {
            ContentVersion = 279,
            Width = 2,
            Height = 2,
            OriginX = 0,
            OriginY = 0,
            Tiles = [default]
        };

        Assert.Throws<SchematicFormatException>(() => SchematicBinary.Serialize(invalid));
    }

    [Fact]
    public void File_api_saves_and_loads_the_shared_binary_format()
    {
        SchematicDocument expected = CreateRepresentativeDocument();
        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-schematic-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, $"arena{SchematicFile.Extension}");

        try
        {
            SchematicFile.Save(path, expected);
            SchematicDocument actual = SchematicFile.Load(path);

            Assert.Equal(SchematicBinary.Serialize(expected), File.ReadAllBytes(path));
            Assert.Equal(SchematicBinary.Serialize(expected), SchematicBinary.Serialize(actual));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Async_stream_api_round_trips_without_file_system_coupling()
    {
        SchematicDocument expected = CreateRepresentativeDocument();
        await using var stream = new MemoryStream();

        await SchematicBinary.WriteAsync(stream, expected);
        stream.Position = 0;
        SchematicDocument actual = await SchematicBinary.ReadAsync(stream);

        Assert.Equal(SchematicBinary.Serialize(expected), SchematicBinary.Serialize(actual));
    }

    private static SchematicDocument CreateRepresentativeDocument() =>
        new()
        {
            ContentVersion = 279,
            Width = 3,
            Height = 2,
            OriginX = 1,
            OriginY = 1,
            Tiles =
            [
                new SchematicTile(1, 0, 0, 0, SchematicTileFlags.Active | SchematicTileFlags.WireRed, 0, 0, 0, 0, SchematicLiquidKind.Water),
                new SchematicTile(2, 1, 18, 0, SchematicTileFlags.Active | SchematicTileFlags.Actuator, 64, 3, 4, 1, SchematicLiquidKind.Lava),
                new SchematicTile(3, 2, 0, 18, SchematicTileFlags.Active | SchematicTileFlags.WireBlue | SchematicTileFlags.WireGreen, 255, 0, 0, 0, SchematicLiquidKind.Honey),
                new SchematicTile(4, 3, 36, 18, SchematicTileFlags.Active | SchematicTileFlags.WireYellow, 1, 1, 2, 2, SchematicLiquidKind.Shimmer),
                new SchematicTile(5, 4, 0, 0, SchematicTileFlags.Inactive | SchematicTileFlags.InvisibleBlock, 0, 0, 0, 0, SchematicLiquidKind.Water),
                new SchematicTile(6, 5, 0, 0, SchematicTileFlags.Active | SchematicTileFlags.FullbrightWall, 0, 0, 0, 0, SchematicLiquidKind.Water)
            ],
            Chests =
            [
                new SchematicChest(
                    0,
                    0,
                    "loot",
                    [new SchematicItemStack(0, 0), new SchematicItemStack(50, 12, 3)])
            ],
            Signs = [new SchematicSign(2, 1, "arena")],
            TileEntities =
            [
                new SchematicLogicSensorTileEntity(1, 0, LogicCheck: 2, On: true),
                new SchematicItemFrameTileEntity(2, 0, new SchematicItemStack(75, 1, 0))
            ],
            Npcs =
            [
                new SchematicNpc(
                    NpcType: 22,
                    X: 16f,
                    Y: 16f,
                    Direction: -1,
                    SpriteDirection: -1,
                    Name: "Guide",
                    Homeless: false,
                    HomeX: 1,
                    HomeY: 1,
                    LifeOverride: 100)
            ],
            WorldItems = [new SchematicWorldItem(new SchematicItemStack(8, 5), 24f, 8f)],
            Markers =
            [
                new SchematicMarker("spawn", SchematicMarkerKind.Point, 1, 1),
                new SchematicMarker("arena:bounds", SchematicMarkerKind.Region, 0, 0, 3, 2)
            ],
            Metadata = [new SchematicMetadataEntry("vega:mode", "ctf")]
        };
}
