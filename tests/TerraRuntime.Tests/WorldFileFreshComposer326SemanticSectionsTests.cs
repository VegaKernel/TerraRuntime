using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileFreshComposer326SemanticSectionsTests
{
    [Fact]
    public void Compose_roundtrips_nonempty_semantic_side_sections()
    {
        const int width = 128;
        const int height = 96;
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Semantic sections",
            "seed-sections",
            width,
            height,
            Guid.Parse("b87a96eb-553b-4421-991a-f3709475a6c1"),
            worldId: 90210);
        var tiles = new WorldTileStore(header.Dimensions);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(64, 40),
            new WorldGenerationPoint(10, 55),
            new WorldGenerationLayers(48d, 64d));

        var creative = new WorldCreativePowersData(
            FreezeTime: true,
            TimeRateSlider: 0.25f,
            FreezeRain: false,
            FreezeWind: true,
            DifficultySlider: 0.75f,
            StopBiomeSpread: true);
        var sections = new WorldFileFreshSections326(
            Chests: [],
            Signs: [new WorldSign(0, "fresh sign", 8, 9)],
            Npcs: new WorldNpcPersistence([], [], []),
            TileEntities:
            [
                new WorldTileEntity(
                    PersistedId: 7,
                    X: 14,
                    Y: 15,
                    Kind: WorldTileEntityKind.TeleportationPylon,
                    Payload: WorldEmptyTileEntityPayload.Instance)
            ],
            PressurePlates: [new WorldPressurePlate(18, 19)],
            TownRooms: [new WorldTownRoom(22, 24, 25)],
            Bestiary: new WorldBestiaryData(
                [new WorldBestiaryKill("Zombie", 17)],
                ["Zombie"],
                ["Guide"]),
            CreativePowers: creative);

        WorldFileFreshCompose326Diagnostic result = WorldFileFreshComposer326.TryCompose(
            header,
            generation,
            tiles,
            sections,
            gameMode: 3,
            crimson: true,
            creationTimeBinary: new DateTime(2026, 9, 1, 4, 0, 0, DateTimeKind.Utc).ToBinary(),
            lastPlayedBinary: new DateTime(2026, 9, 1, 4, 1, 0, DateTimeKind.Utc).ToBinary(),
            out byte[] file);

        Assert.True(result.Succeeded, result.ToString());
        Assert.True(result.Validation.IsLoaded, result.Validation.ToString());
        Assert.NotEmpty(file);

        WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
            file,
            CreateLimits(width * height),
            out WorldFileData? loaded);

        Assert.True(load.IsLoaded, load.ToString());
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        Assert.Equal(sections.Signs, world.Signs);
        Assert.Single(world.TileEntities);
        Assert.Equal(7, world.TileEntities[0].PersistedId);
        Assert.Equal((short)14, world.TileEntities[0].X);
        Assert.Equal((short)15, world.TileEntities[0].Y);
        Assert.Equal(WorldTileEntityKind.TeleportationPylon, world.TileEntities[0].Kind);
        Assert.IsType<WorldEmptyTileEntityPayload>(world.TileEntities[0].Payload);
        Assert.Equal(sections.PressurePlates, world.PressurePlates);
        Assert.Equal(sections.TownRooms, world.TownRooms);
        Assert.Equal(sections.Bestiary.Kills, world.Bestiary.Kills);
        Assert.Equal(sections.Bestiary.Sightings, world.Bestiary.Sightings);
        Assert.Equal(sections.Bestiary.Chats, world.Bestiary.Chats);
        Assert.Equal(creative, world.CreativePowers);
    }

    [Fact]
    public void Compose_rejects_invalid_semantic_section_without_returning_partial_file()
    {
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Invalid section",
            "seed-invalid",
            128,
            96,
            Guid.Parse("b8579622-bc63-427c-b397-a2da31f51773"),
            worldId: 90211);
        var tiles = new WorldTileStore(header.Dimensions);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(64, 40),
            new WorldGenerationPoint(10, 55),
            new WorldGenerationLayers(48d, 64d));
        var sections = new WorldFileFreshSections326(
            Chests: [],
            Signs: [],
            Npcs: new WorldNpcPersistence([], [], []),
            TileEntities: [],
            PressurePlates: [],
            TownRooms: [],
            Bestiary: new WorldBestiaryData([], [], []),
            CreativePowers: new WorldCreativePowersData(
                FreezeTime: false,
                TimeRateSlider: float.NaN,
                FreezeRain: false,
                FreezeWind: false,
                DifficultySlider: 0.5f,
                StopBiomeSpread: false));

        WorldFileFreshCompose326Diagnostic result = WorldFileFreshComposer326.TryCompose(
            header,
            generation,
            tiles,
            sections,
            gameMode: 0,
            crimson: false,
            creationTimeBinary: 0,
            lastPlayedBinary: 0,
            out byte[] file);

        Assert.Equal(WorldFileFreshCompose326Result.CreativePowersEncodeFailed, result.Result);
        Assert.Equal((int)WorldFileCreativePowersEncodeResult.InvalidSliderValue, result.StageResultCode);
        Assert.Empty(file);
    }

    private static WorldFileLoadLimits CreateLimits(long tileCount) =>
        new(
            MaxTileCount: tileCount,
            MaxItemsPerChest: 0,
            MaxTotalChestItems: 0,
            MaxTextBytesPerSign: 64,
            MaxTotalSignTextBytes: 256,
            Npcs: new WorldFileNpcDecodeOptions(0, 0, 0, 0, 0, 0),
            MaxTileEntities: 4,
            MaxPressurePlates: 4,
            MaxTownRooms: 4,
            Bestiary: new WorldFileBestiaryLimits(4, 4, 4, 64, 256),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(4096, 12288, 0, 0, 0, 0));
}
