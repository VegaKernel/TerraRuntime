using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyblockWorldGenerationPersistenceTests
{
    [Fact]
    public void Skyblock_persists_and_reloads_island_chests_with_loot()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "skyblock.wld");
        var pipeline = new RuntimeWorldCreationPersistencePipeline(
            new StartupWorldGeneratorSource(host: null),
            maxTileCount: 32_000_000);
        var request = new WorldGenerationRequest(
            SkyblockWorldGenerationProvider.GeneratorId,
            "SkyblockRoundTrip",
            Seed: 0x51A7B10CUL,
            WidthTiles: 512,
            HeightTiles: 256);
        long timestamp = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc).ToBinary();

        try
        {
            RuntimeWorldCreationPersistenceResult creation = pipeline.TryCreateAndPersist(
                request,
                worldPath,
                Guid.Parse("2eb98abe-dd68-4a52-af67-e43a84f37011"),
                worldId: 246813579,
                creationTimeBinary: timestamp,
                lastPlayedBinary: timestamp,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(creation.Succeeded, creation.ToString());
            Assert.True(File.Exists(worldPath));

            byte[] bytes = File.ReadAllBytes(worldPath);
            WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
                bytes,
                CreateLimits(512L * 256L),
                out WorldFileData? world);
            Assert.True(load.IsLoaded, load.ToString());
            Assert.NotNull(world);
            Assert.Equal("SkyblockRoundTrip", world.Header.Name);
            Assert.Equal(512, world.Header.Dimensions.WidthTiles);
            Assert.Equal(256, world.Header.Dimensions.HeightTiles);
            Assert.True(world.RuntimeMetadata.DungeonY > world.RuntimeMetadata.SpawnY);
            Assert.True(world.Chests.Length >= 3);

            WorldChest starter = Assert.Single(world.Chests, static chest => chest.Name == "Skyblock Starter");
            Assert.Equal(VanillaItemIds.CopperPickaxe.Value, starter.Items[0].ItemType);
            Assert.Equal(1, starter.Items[0].Stack);
            Assert.Equal(VanillaItemIds.DirtBlock.Value, starter.Items[1].ItemType);
            Assert.Equal(100, starter.Items[1].Stack);

            Assert.True(world.Tiles.Get(starter.X, starter.Y).IsActive);
            Assert.Equal(VanillaTileIds.Containers.Value, world.Tiles.Get(starter.X, starter.Y).Type);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static WorldFileLoadLimits CreateLimits(long tileCount) =>
        new(
            MaxTileCount: tileCount,
            MaxItemsPerChest: WorldGenerationChestRules.VanillaItemSlotCount,
            MaxTotalChestItems: (long)VanillaWorldFormat326.MaximumChestSlots * WorldGenerationChestRules.VanillaItemSlotCount,
            MaxTextBytesPerSign: 0,
            MaxTotalSignTextBytes: 0,
            Npcs: new WorldFileNpcDecodeOptions(0, 0, 0, 0, 0, 0),
            MaxTileEntities: 0,
            MaxPressurePlates: 0,
            MaxTownRooms: 0,
            Bestiary: new WorldFileBestiaryLimits(0, 0, 0, 0, 0),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(4096, 12288, 0, 0, 0, 0));
}
