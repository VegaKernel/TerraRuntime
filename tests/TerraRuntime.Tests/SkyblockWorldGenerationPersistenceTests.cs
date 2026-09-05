using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyblockWorldGenerationPersistenceTests
{
    [Fact]
    public void Skyblock_persists_and_reloads_progression_liquids_structures_and_chests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "skyblock.wld");
        var pipeline = new RuntimeWorldCreationPersistencePipeline(
            new StartupWorldGeneratorSource(host: null),
            maxTileCount: 32_000_000);
        var request = new WorldGenerationRequest(
            SkyblockProvider.GeneratorId,
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

            var liquidKinds = new HashSet<WorldLiquidKind>();
            var activeTypes = new HashSet<ushort>();
            var wallTypes = new HashSet<ushort>();
            var demonAltarFrameX = new HashSet<short>();
            var demonAltarFrameY = new HashSet<short>();
            var lihzahrdAltarFrameX = new HashSet<short>();
            var lihzahrdAltarFrameY = new HashSet<short>();
            int demonAltarTiles = 0;
            int hellforgeTiles = 0;
            int lihzahrdAltarTiles = 0;

            for (int x = 0; x < world.Header.Dimensions.WidthTiles; x++)
            {
                for (int y = 0; y < world.Header.Dimensions.HeightTiles; y++)
                {
                    WorldTile tile = world.Tiles.Get(x, y);
                    if (tile.LiquidAmount > 0)
                        liquidKinds.Add(tile.LiquidKind);
                    if (tile.Wall != 0)
                        wallTypes.Add(tile.Wall);
                    if (!tile.IsActive)
                        continue;

                    activeTypes.Add(tile.Type);
                    if (tile.Type == VanillaTileIds.DemonAltar.Value)
                    {
                        demonAltarTiles++;
                        demonAltarFrameX.Add(tile.FrameX);
                        demonAltarFrameY.Add(tile.FrameY);
                    }
                    else if (tile.Type == VanillaTileIds.Hellforge.Value)
                    {
                        hellforgeTiles++;
                    }
                    else if (tile.Type == VanillaTileIds.LihzahrdAltar.Value)
                    {
                        lihzahrdAltarTiles++;
                        lihzahrdAltarFrameX.Add(tile.FrameX);
                        lihzahrdAltarFrameY.Add(tile.FrameY);
                    }
                }
            }

            Assert.Contains(WorldLiquidKind.Water, liquidKinds);
            Assert.Contains(WorldLiquidKind.Lava, liquidKinds);
            Assert.Contains(WorldLiquidKind.Honey, liquidKinds);
            Assert.Contains(WorldLiquidKind.Shimmer, liquidKinds);

            Assert.Contains(checked((ushort)VanillaTileIds.DemonAltar.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Hellforge.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Hive.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.LihzahrdBrick.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.LihzahrdAltar.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.MushroomGrass.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Marble.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Granite.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Cobweb.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaWallIds.SpiderUnsafe.Value), wallTypes);
            Assert.Contains(checked((ushort)VanillaWallIds.HiveUnsafe.Value), wallTypes);
            Assert.Contains(checked((ushort)VanillaWallIds.LihzahrdBrickUnsafe.Value), wallTypes);

            Assert.Equal(6, demonAltarTiles);
            Assert.Equal(6, hellforgeTiles);
            Assert.Equal(6, lihzahrdAltarTiles);
            Assert.Equal(new short[] { 0, 18, 36 }, demonAltarFrameX.Order().ToArray());
            Assert.Equal(new short[] { 0, 18 }, demonAltarFrameY.Order().ToArray());
            Assert.Equal(new short[] { 0, 18, 36 }, lihzahrdAltarFrameX.Order().ToArray());
            Assert.Equal(new short[] { 0, 18 }, lihzahrdAltarFrameY.Order().ToArray());
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
            Npcs: new WorldFileNpcDecodeOptions(1, 2, 1, 1, 64, 64),
            MaxTileEntities: 0,
            MaxPressurePlates: 0,
            MaxTownRooms: 0,
            Bestiary: new WorldFileBestiaryLimits(0, 0, 0, 0, 0),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(4096, 12288, 0, 0, 0, 0));
}

