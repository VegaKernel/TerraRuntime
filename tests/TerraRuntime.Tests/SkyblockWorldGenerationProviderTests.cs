using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyblockWorldGenerationProviderTests
{
    [Fact]
    public void Built_in_skyblock_generates_void_biome_islands_lowered_dungeon_and_persistent_chests()
    {
        const int width = 512;
        const int height = 256;
        var request = new WorldGenerationRequest(
            SkyblockWorldGenerationProvider.GeneratorId,
            "Skyblock",
            Seed: 0x5A17B10CUL,
            WidthTiles: width,
            HeightTiles: height);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        RuntimeWorldGenerationWorkspace candidate = Assert.IsType<RuntimeWorldGenerationWorkspace>(result.Candidate);
        Assert.Equal(width / 2, result.Metadata.Spawn.X);
        Assert.True(result.Metadata.Dungeon.Y > result.Metadata.Spawn.Y + height / 4);
        Assert.InRange(result.Metadata.Layers.WorldSurface, height * 0.60d, height * 0.64d);
        Assert.InRange(result.Metadata.Layers.RockLayer, height * 0.78d, height * 0.82d);

        Assert.True(candidate.TryGetTile(result.Metadata.Spawn.X, result.Metadata.Spawn.Y, out WorldGenerationTile spawnTile));
        Assert.Equal(WorldGenerationTileFlags.None, spawnTile.Flags & WorldGenerationTileFlags.Active);
        Assert.True(candidate.TryGetTile(result.Metadata.Spawn.X, result.Metadata.Spawn.Y + 1, out WorldGenerationTile spawnFloor));
        Assert.True((spawnFloor.Flags & WorldGenerationTileFlags.Active) != 0);

        long activeTiles = 0;
        var activeTypes = new HashSet<ushort>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
                if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
                    continue;

                activeTiles++;
                activeTypes.Add(tile.Type);
            }
        }

        Assert.InRange(activeTiles, 1, (long)width * height / 5);
        Assert.Contains(checked((ushort)VanillaTileIds.Sand.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.SnowBlock.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.IceBlock.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.JungleGrass.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.Mud.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.CorruptGrass.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.Ebonstone.Value), activeTypes);

        WorldChest[] chests = result.Metadata.GeneratedChests ?? [];
        Assert.True(chests.Length >= 3);
        Assert.Contains(chests, static chest => chest.Name == "Skyblock Starter");
        Assert.Contains(chests, static chest => chest.Name == "Dungeon Cache");

        WorldChest starter = Assert.Single(chests, static chest => chest.Name == "Skyblock Starter");
        Assert.Equal(VanillaItemIds.CopperPickaxe.Value, starter.Items[0].ItemType);
        Assert.Equal(1, starter.Items[0].Stack);
        Assert.Equal(VanillaItemIds.DirtBlock.Value, starter.Items[1].ItemType);
        Assert.Equal(100, starter.Items[1].Stack);
    }

    [Fact]
    public void Skyblock_crimson_option_uses_crimson_evil_island_palette()
    {
        RuntimeWorldCreationPipelineResult result = Generate(
            seed: 0xC71A50UL,
            evil: WorldGenerationEvil.Crimson);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        RuntimeWorldGenerationWorkspace candidate = Assert.IsType<RuntimeWorldGenerationWorkspace>(result.Candidate);
        var activeTypes = CaptureActiveTypes(candidate);

        Assert.Contains(checked((ushort)VanillaTileIds.CrimsonGrass.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.Crimstone.Value), activeTypes);
        Assert.DoesNotContain(checked((ushort)VanillaTileIds.CorruptGrass.Value), activeTypes);
        Assert.DoesNotContain(checked((ushort)VanillaTileIds.Ebonstone.Value), activeTypes);
    }

    [Fact]
    public void Skyblock_same_seed_repeats_layout_metadata_and_chest_contents()
    {
        RuntimeWorldCreationPipelineResult first = Generate(seed: 123456789UL);
        RuntimeWorldCreationPipelineResult second = Generate(seed: 123456789UL);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.Equal(first.Metadata.Spawn, second.Metadata.Spawn);
        Assert.Equal(first.Metadata.Dungeon, second.Metadata.Dungeon);
        Assert.Equal(first.Metadata.Layers, second.Metadata.Layers);

        WorldChest[] firstChests = first.Metadata.GeneratedChests ?? [];
        WorldChest[] secondChests = second.Metadata.GeneratedChests ?? [];
        Assert.Equal(firstChests.Length, secondChests.Length);
        for (int index = 0; index < firstChests.Length; index++)
        {
            Assert.Equal(firstChests[index].X, secondChests[index].X);
            Assert.Equal(firstChests[index].Y, secondChests[index].Y);
            Assert.Equal(firstChests[index].Name, secondChests[index].Name);
            Assert.Equal(firstChests[index].Items, secondChests[index].Items);
        }
    }

    [Fact]
    public void Skyblock_reserves_dungeon_envelope_from_loot_islands_across_seeds()
    {
        for (ulong seed = 0; seed < 32; seed++)
        {
            RuntimeWorldCreationPipelineResult result = Generate(seed);
            Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());

            WorldGenerationPoint dungeon = result.Metadata.Dungeon;
            WorldChest[] chests = result.Metadata.GeneratedChests ?? [];
            foreach (WorldChest chest in chests)
            {
                if (chest.Name == "Dungeon Cache")
                    continue;

                int centerX = chest.X + 1;
                int surfaceY = chest.Y + 2;
                int horizontalDistance = Math.Abs(centerX - dungeon.X);
                int verticalDistance = Math.Abs(surfaceY - (dungeon.Y + 1));
                Assert.True(
                    horizontalDistance >= 54 || verticalDistance >= 38,
                    $"Seed {seed} placed chest island too close to dungeon: chest=({chest.X},{chest.Y}) dungeon=({dungeon.X},{dungeon.Y}).");
            }
        }
    }

    [Fact]
    public void Skyblock_rejects_workspace_too_small_for_separated_island_field()
    {
        var request = new WorldGenerationRequest(
            SkyblockWorldGenerationProvider.GeneratorId,
            "TinySkyblock",
            Seed: 1,
            WidthTiles: 255,
            HeightTiles: 160);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(RuntimeWorldCreationPipelineStatus.GenerationFailed, result.Status);
    }

    private static RuntimeWorldCreationPipelineResult Generate(
        ulong seed,
        WorldGenerationEvil evil = WorldGenerationEvil.Corruption)
    {
        var request = new WorldGenerationRequest(
            SkyblockWorldGenerationProvider.GeneratorId,
            "Repeatable",
            seed,
            WidthTiles: 512,
            HeightTiles: 256)
        {
            Options = new WorldGenerationOptions(WorldGenerationGameMode.Classic, evil)
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        return pipeline.CreateCandidate(in request);
    }

    private static HashSet<ushort> CaptureActiveTypes(RuntimeWorldGenerationWorkspace candidate)
    {
        var activeTypes = new HashSet<ushort>();
        for (int x = 0; x < candidate.WidthTiles; x++)
        {
            for (int y = 0; y < candidate.HeightTiles; y++)
            {
                Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    activeTypes.Add(tile.Type);
            }
        }

        return activeTypes;
    }
}
