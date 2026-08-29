using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldCreationPersistencePipelineTests
{
    [Fact]
    public void Pipeline_generates_composes_and_publishes_builtin_world()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "flat.wld");
        var source = new StartupWorldGeneratorSource(host: null);
        var pipeline = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 32_000_000);
        var request = new WorldGenerationRequest(
            new WorldGeneratorId("terraruntime:flat"),
            "Flat",
            Seed: 12345UL,
            WidthTiles: 128,
            HeightTiles: 96);
        long timestamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc).ToBinary();

        try
        {
            RuntimeWorldCreationPersistenceResult result = pipeline.TryCreateAndPersist(
                request,
                worldPath,
                Guid.Parse("0a4e31c0-dc25-47d5-b3e1-508574ba7ae9"),
                worldId: 987654321,
                gameMode: 0,
                crimson: false,
                creationTimeBinary: timestamp,
                lastPlayedBinary: timestamp,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.ToString());
            Assert.NotNull(result.Creation);
            Assert.NotNull(result.Composition);
            Assert.NotNull(result.Publication);
            Assert.Equal(Path.GetFullPath(worldPath), result.WorldPath);
            Assert.True(File.Exists(worldPath));

            byte[] bytes = File.ReadAllBytes(worldPath);
            WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
                bytes,
                CreateLimits(128L * 96L),
                out WorldFileData? world);
            Assert.True(load.IsLoaded, load.ToString());
            Assert.NotNull(world);
            Assert.Equal("Flat", world.Header.Name);
            Assert.Equal("12345", world.Header.SeedText);
            Assert.Equal((short)64, world.RuntimeMetadata.SpawnX);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pipeline_rejects_existing_destination_before_running_generator()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "existing.wld");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(worldPath, [7, 8, 9]);
        var source = new StartupWorldGeneratorSource(host: null);
        var pipeline = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 32_000_000);
        var request = new WorldGenerationRequest(
            new WorldGeneratorId("missing:generator"),
            "WouldNotRun",
            Seed: 1UL,
            WidthTiles: 128,
            HeightTiles: 96);

        try
        {
            RuntimeWorldCreationPersistenceResult result = pipeline.TryCreateAndPersist(
                request,
                worldPath,
                Guid.Parse("db6b7407-3127-4938-8ec0-a35e679667ae"),
                worldId: 1,
                gameMode: 0,
                crimson: false,
                creationTimeBinary: 0,
                lastPlayedBinary: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(RuntimeWorldCreationPersistenceStatus.AlreadyExists, result.Status);
            Assert.Null(result.Creation);
            Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(worldPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pipeline_rejects_tile_budget_before_resolving_generator()
    {
        var source = new StartupWorldGeneratorSource(host: null);
        var pipeline = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 10_000);
        var request = new WorldGenerationRequest(
            new WorldGeneratorId("missing:generator"),
            "Oversized",
            Seed: 1UL,
            WidthTiles: 101,
            HeightTiles: 100);

        RuntimeWorldCreationPersistenceResult result = pipeline.TryCreateAndPersist(
            request,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "oversized.wld"),
            Guid.Parse("d9b150ac-c39d-4bbb-b574-d376d055fd96"),
            worldId: 1,
            gameMode: 0,
            crimson: false,
            creationTimeBinary: 0,
            lastPlayedBinary: 0,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded, result.Status);
        Assert.Null(result.Creation);
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