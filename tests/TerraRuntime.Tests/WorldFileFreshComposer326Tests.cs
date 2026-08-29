using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileFreshComposer326Tests
{
    [Fact]
    public void Compose_roundtrips_complete_fresh_world_through_transactional_loader()
    {
        const int width = 128;
        const int height = 96;
        Guid uniqueId = Guid.Parse("2e58b67a-4ccf-4ad3-a3df-2dca1c15a0d3");
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Generated",
            "seed-1458",
            width,
            height,
            uniqueId,
            worldId: 123456789);

        var tiles = new WorldTileStore(header.Dimensions);
        for (int x = 0; x < width; x++)
        {
            for (int y = 48; y < height; y++)
            {
                tiles.Set(x, y, new WorldTile
                {
                    Type = y < 64 ? (ushort)0 : (ushort)1,
                    Flags = WorldTileFlags.Active
                });
            }
        }

        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(64, 40),
            new WorldGenerationPoint(12, 55),
            new WorldGenerationLayers(48d, 64d));

        WorldFileFreshCompose326Diagnostic result = WorldFileFreshComposer326.TryCompose(
            header,
            generation,
            tiles,
            gameMode: 0,
            crimson: false,
            creationTimeBinary: new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc).ToBinary(),
            lastPlayedBinary: new DateTime(2026, 8, 29, 12, 1, 0, DateTimeKind.Utc).ToBinary(),
            out byte[] file);

        Assert.True(result.Succeeded, result.ToString());
        Assert.True(result.Validation.IsLoaded, result.Validation.ToString());
        Assert.NotEmpty(file);

        WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
            file,
            CreateLimits(width * height),
            out WorldFileData? world);

        Assert.True(load.IsLoaded, load.ToString());
        Assert.NotNull(world);
        Assert.Equal(WorldFileFormatPolicy.CurrentVersion, world.Envelope.FormatVersion);
        Assert.Equal(VanillaWorldEnvelope326.FreshRevision, world.Envelope.Revision);
        Assert.Equal(VanillaFreshWorldHeader326.WorldGeneratorVersion, world.Header.WorldGeneratorVersion);
        Assert.Equal(header.Name, world.Header.Name);
        Assert.Equal(header.SeedText, world.Header.SeedText);
        Assert.Equal(uniqueId, world.Header.UniqueId);
        Assert.Equal(width * height, world.Tiles.Count);
        Assert.Empty(world.Chests);
        Assert.Empty(world.Signs);
        Assert.Empty(world.Npcs.TownNpcs);
        Assert.Empty(world.Npcs.PersistentNpcs);
        Assert.Empty(world.TileEntities);
        Assert.Empty(world.PressurePlates);
        Assert.Empty(world.TownRooms);
        Assert.Empty(world.Bestiary.Kills);
        Assert.Equal((short)generation.Spawn.X, world.RuntimeMetadata.SpawnX);
        Assert.Equal((short)generation.Spawn.Y, world.RuntimeMetadata.SpawnY);
        Assert.Equal((short)generation.Dungeon.X, world.RuntimeMetadata.DungeonX);
        Assert.Equal((short)generation.Dungeon.Y, world.RuntimeMetadata.DungeonY);
        Assert.Equal((short)generation.Layers.WorldSurface, world.RuntimeMetadata.WorldSurface);
        Assert.Equal((short)generation.Layers.RockLayer, world.RuntimeMetadata.RockLayer);
        Assert.Equal(WorldFileFreshRuntimeMetadata326Encoder.InitialTime, world.RuntimeMetadata.Time);
        Assert.True(world.RuntimeMetadata.DayTime);
        Assert.Equal(new WorldOreTiers(7, 6, 9, 8, -1, -1, -1), world.RuntimeMetadata.OreTiers);
    }

    [Fact]
    public void Compose_rejects_header_tile_dimension_mismatch_without_returning_bytes()
    {
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Generated",
            "seed",
            128,
            96,
            Guid.Parse("62c12e08-a4f1-4663-89bd-29b03218ad07"),
            worldId: 42);
        var tiles = new WorldTileStore(new WorldDimensions(64, 96));
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(32, 30),
            new WorldGenerationPoint(8, 40),
            new WorldGenerationLayers(32d, 64d));

        WorldFileFreshCompose326Diagnostic result = WorldFileFreshComposer326.TryCompose(
            header,
            generation,
            tiles,
            gameMode: 0,
            crimson: false,
            creationTimeBinary: 0,
            lastPlayedBinary: 0,
            out byte[] file);

        Assert.Equal(WorldFileFreshCompose326Result.InvalidDimensions, result.Result);
        Assert.Empty(file);
    }

    private static WorldFileLoadLimits CreateLimits(long tileCount) =>
        new(
            MaxTileCount: tileCount,
            MaxItemsPerChest: 0,
            MaxTotalChestItems: 0,
            MaxTextBytesPerSign: 0,
            MaxTotalSignTextBytes: 0,
            Npcs: new WorldFileNpcDecodeOptions(0, 0, 0, 0, 0, 0),
            MaxTileEntities: 0,
            MaxPressurePlates: 0,
            MaxTownRooms: 0,
            Bestiary: new WorldFileBestiaryLimits(0, 0, 0, 0, 0),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(4096, 12288, 0, 0, 0, 0));
}