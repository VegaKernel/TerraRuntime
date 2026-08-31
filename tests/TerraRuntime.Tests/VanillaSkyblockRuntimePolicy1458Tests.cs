using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaSkyblockRuntimePolicy1458Tests
{
    [Theory]
    [InlineData(true, 99, 1000, true)]
    [InlineData(true, 100, 1000, false)]
    [InlineData(true, 100, 1001, true)]
    [InlineData(false, 0, 1000, false)]
    public void Low_tiles_matches_strict_ten_percent_and_world_flag_gate(
        bool skyblockWorld,
        int activeTiles,
        int totalTiles,
        bool expected)
    {
        Assert.Equal(
            expected,
            VanillaSkyblockRuntimePolicy1458.IsLowTiles(skyblockWorld, activeTiles, totalTiles));
    }

    [Fact]
    public void Low_tile_state_selects_vanilla_snow_desert_and_hardmode_rules()
    {
        VanillaSkyblockRuntimeState1458 low =
            VanillaSkyblockRuntimePolicy1458.Create(true, activeTileCount: 299, totalTileCount: 3000);
        VanillaSkyblockRuntimeState1458 dense =
            VanillaSkyblockRuntimePolicy1458.Create(true, activeTileCount: 300, totalTileCount: 3000);

        Assert.True(low.LowTiles);
        Assert.Equal(300, low.SnowTileThreshold);
        Assert.Equal(300, low.DesertTileThreshold);
        Assert.True(low.SkipHardmodeConversion);

        Assert.False(dense.LowTiles);
        Assert.Equal(1500, dense.SnowTileThreshold);
        Assert.Equal(1500, dense.DesertTileThreshold);
        Assert.False(dense.SkipHardmodeConversion);
    }

    [Fact]
    public void Tile_store_evaluation_uses_current_world_state_not_generator_identity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 10));
        for (int x = 0; x < 19; x++)
        {
            tiles.Set(x, 0, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }

        var skyblock = new WorldFileRuntimeMetadata { SkyblockWorld = true };
        var ordinary = new WorldFileRuntimeMetadata { SkyblockWorld = false };

        VanillaSkyblockRuntimeState1458 skyblockState = VanillaSkyblockRuntimePolicy1458.Evaluate(skyblock, tiles);
        VanillaSkyblockRuntimeState1458 ordinaryState = VanillaSkyblockRuntimePolicy1458.Evaluate(ordinary, tiles);

        Assert.Equal(19, skyblockState.ActiveTileCount);
        Assert.Equal(200, skyblockState.TotalTileCount);
        Assert.True(skyblockState.LowTiles);
        Assert.False(ordinaryState.LowTiles);
    }

    [Fact]
    public void Builtin_skyblock_persists_vanilla_skyblock_world_flag()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "skyblock-runtime-policy.wld");
        var pipeline = new RuntimeWorldCreationPersistencePipeline(
            new StartupWorldGeneratorSource(host: null),
            maxTileCount: 32_000_000);
        var request = new WorldGenerationRequest(
            SkyblockWorldGenerationProvider.GeneratorId,
            "SkyblockRuntimePolicy",
            Seed: 1458UL,
            WidthTiles: 512,
            HeightTiles: 256);
        long timestamp = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc).ToBinary();

        try
        {
            RuntimeWorldCreationPersistenceResult creation = pipeline.TryCreateAndPersist(
                request,
                worldPath,
                Guid.Parse("97d19e0a-c773-4d5a-9009-ef4112b73057"),
                worldId: 1458,
                creationTimeBinary: timestamp,
                lastPlayedBinary: timestamp,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(creation.Succeeded, creation.ToString());
            WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
                File.ReadAllBytes(worldPath),
                CreateLimits(512L * 256L),
                out WorldFileData? world);

            Assert.True(load.IsLoaded, load.ToString());
            Assert.NotNull(world);
            Assert.True(world.RuntimeMetadata.SkyblockWorld);
            Assert.True(VanillaSkyblockRuntimePolicy1458.Evaluate(world).LowTiles);
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

