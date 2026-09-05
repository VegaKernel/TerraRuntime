using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyblockWorldGenerationProviderTests
{
    [Fact]
    public void Built_in_skyblock_generates_progression_resources_structures_lowered_dungeon_and_persistent_chests()
    {
        const int width = 512;
        const int height = 256;
        var request = new WorldGenerationRequest(
            SkyblockProvider.GeneratorId,
            "Skyblock",
            Seed: 0x5A17B10CUL,
            WidthTiles: width,
            HeightTiles: height);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Workspace candidate = Assert.IsType<Workspace>(result.Candidate);
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
        var wallTypes = new HashSet<ushort>();
        var liquidKinds = new HashSet<WorldGenerationLiquidKind>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
                if (tile.LiquidAmount > 0)
                    liquidKinds.Add(tile.LiquidKind);
                if (tile.Wall != 0)
                    wallTypes.Add(tile.Wall);

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
        Assert.Contains(WorldGenerationLiquidKind.Water, liquidKinds);
        Assert.Contains(WorldGenerationLiquidKind.Lava, liquidKinds);
        Assert.Contains(WorldGenerationLiquidKind.Honey, liquidKinds);
        Assert.Contains(WorldGenerationLiquidKind.Shimmer, liquidKinds);

        Assert.Equal(6, CountActiveType(candidate, VanillaTileIds.DemonAltar.Value));
        Assert.Equal(6, CountActiveType(candidate, VanillaTileIds.Hellforge.Value));
        Assert.Equal(6, CountActiveType(candidate, VanillaTileIds.LihzahrdAltar.Value));

        WorldChest[] chests = candidate.CaptureGeneratedChests();
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
    public void Skyblock_evil_altar_frames_follow_world_evil()
    {
        Workspace corruption = Assert.IsType<Workspace>(
            Generate(seed: 0xA17A2UL, evil: WorldGenerationEvil.Corruption).Candidate);
        Workspace crimson = Assert.IsType<Workspace>(
            Generate(seed: 0xA17A2UL, evil: WorldGenerationEvil.Crimson).Candidate);

        Assert.Equal(new short[] { 0, 18, 36 }, CaptureFrameX(corruption, VanillaTileIds.DemonAltar.Value));
        Assert.Equal(new short[] { 54, 72, 90 }, CaptureFrameX(crimson, VanillaTileIds.DemonAltar.Value));
        Assert.Equal(new short[] { 0, 18 }, CaptureFrameY(corruption, VanillaTileIds.DemonAltar.Value));
        Assert.Equal(new short[] { 0, 18 }, CaptureFrameY(crimson, VanillaTileIds.DemonAltar.Value));
        Assert.Equal(new short[] { 0, 18, 36 }, CaptureFrameX(corruption, VanillaTileIds.LihzahrdAltar.Value));
        Assert.Equal(new short[] { 0, 18 }, CaptureFrameY(corruption, VanillaTileIds.LihzahrdAltar.Value));
    }

    [Fact]
    public void Skyblock_crimson_option_uses_crimson_evil_island_palette()
    {
        RuntimeWorldCreationPipelineResult result = Generate(
            seed: 0xC71A50UL,
            evil: WorldGenerationEvil.Crimson);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Workspace candidate = Assert.IsType<Workspace>(result.Candidate);
        var activeTypes = CaptureActiveTypes(candidate);

        Assert.Contains(checked((ushort)VanillaTileIds.CrimsonGrass.Value), activeTypes);
        Assert.Contains(checked((ushort)VanillaTileIds.Crimstone.Value), activeTypes);
        Assert.DoesNotContain(checked((ushort)VanillaTileIds.CorruptGrass.Value), activeTypes);
        Assert.DoesNotContain(checked((ushort)VanillaTileIds.Ebonstone.Value), activeTypes);
    }

    [Fact]
    public void Skyblock_same_seed_repeats_layout_metadata_chests_liquids_and_structures()
    {
        RuntimeWorldCreationPipelineResult first = Generate(seed: 123456789UL);
        RuntimeWorldCreationPipelineResult second = Generate(seed: 123456789UL);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.Equal(first.Metadata.Spawn, second.Metadata.Spawn);
        Assert.Equal(first.Metadata.Dungeon, second.Metadata.Dungeon);
        Assert.Equal(first.Metadata.Layers, second.Metadata.Layers);

        Workspace firstCandidate = Assert.IsType<Workspace>(first.Candidate);
        Workspace secondCandidate = Assert.IsType<Workspace>(second.Candidate);
        WorldChest[] firstChests = firstCandidate.CaptureGeneratedChests();
        WorldChest[] secondChests = secondCandidate.CaptureGeneratedChests();
        Assert.Equal(firstChests.Length, secondChests.Length);
        for (int index = 0; index < firstChests.Length; index++)
        {
            Assert.Equal(firstChests[index].X, secondChests[index].X);
            Assert.Equal(firstChests[index].Y, secondChests[index].Y);
            Assert.Equal(firstChests[index].Name, secondChests[index].Name);
            Assert.Equal(firstChests[index].Items, secondChests[index].Items);
        }

        Assert.Equal(CaptureLiquidTiles(firstCandidate), CaptureLiquidTiles(secondCandidate));
        Assert.Equal(CaptureProgressionStructureTiles(firstCandidate), CaptureProgressionStructureTiles(secondCandidate));
    }

    [Fact]
    public void Skyblock_compact_minimum_world_guarantees_biomes_resources_and_structures_across_seeds()
    {
        for (ulong seed = 0; seed < 32; seed++)
        {
            RuntimeWorldCreationPipelineResult result = Generate(seed, width: 256, height: 160);
            Assert.True(result.Succeeded, $"Seed {seed}: {result.Generation.Execution?.Error}");

            Workspace candidate = Assert.IsType<Workspace>(result.Candidate);
            HashSet<ushort> activeTypes = CaptureActiveTypes(candidate);
            HashSet<WorldGenerationLiquidKind> liquidKinds = CaptureLiquidKinds(candidate);

            Assert.Contains(checked((ushort)VanillaTileIds.Sand.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.SnowBlock.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.IceBlock.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.JungleGrass.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Mud.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.CorruptGrass.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Ebonstone.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.DemonAltar.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Hellforge.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Hive.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.LihzahrdBrick.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.LihzahrdAltar.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.MushroomGrass.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Marble.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Granite.Value), activeTypes);
            Assert.Contains(checked((ushort)VanillaTileIds.Cobweb.Value), activeTypes);
            Assert.Contains(WorldGenerationLiquidKind.Water, liquidKinds);
            Assert.Contains(WorldGenerationLiquidKind.Lava, liquidKinds);
            Assert.Contains(WorldGenerationLiquidKind.Honey, liquidKinds);
            Assert.Contains(WorldGenerationLiquidKind.Shimmer, liquidKinds);
            Assert.Equal(6, CountActiveType(candidate, VanillaTileIds.DemonAltar.Value));
            Assert.Equal(6, CountActiveType(candidate, VanillaTileIds.Hellforge.Value));
            Assert.Equal(6, CountActiveType(candidate, VanillaTileIds.LihzahrdAltar.Value));
        }
    }

    [Fact]
    public void Skyblock_reserves_dungeon_envelope_from_loot_islands_across_seeds()
    {
        for (ulong seed = 0; seed < 32; seed++)
        {
            RuntimeWorldCreationPipelineResult result = Generate(seed);
            Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());

            Workspace candidate = Assert.IsType<Workspace>(result.Candidate);
            WorldGenerationPoint dungeon = result.Metadata.Dungeon;
            WorldChest[] chests = candidate.CaptureGeneratedChests();
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
            SkyblockProvider.GeneratorId,
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
        WorldGenerationEvil evil = WorldGenerationEvil.Corruption,
        int width = 512,
        int height = 256)
    {
        var request = new WorldGenerationRequest(
            SkyblockProvider.GeneratorId,
            "Repeatable",
            seed,
            WidthTiles: width,
            HeightTiles: height)
        {
            Options = new WorldGenerationOptions(WorldGenerationGameMode.Classic, evil)
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        return pipeline.CreateCandidate(in request);
    }

    private static HashSet<ushort> CaptureActiveTypes(Workspace candidate)
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

    private static int CountActiveType(Workspace candidate, int type)
    {
        int count = 0;
        for (int x = 0; x < candidate.WidthTiles; x++)
        for (int y = 0; y < candidate.HeightTiles; y++)
        {
            Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type)
                count++;
        }
        return count;
    }

    private static short[] CaptureFrameX(Workspace candidate, int type) =>
        CaptureProgressionStructureTiles(candidate)
            .Where(tile => tile.Type == type)
            .Select(tile => tile.FrameX)
            .Distinct()
            .Order()
            .ToArray();

    private static short[] CaptureFrameY(Workspace candidate, int type) =>
        CaptureProgressionStructureTiles(candidate)
            .Where(tile => tile.Type == type)
            .Select(tile => tile.FrameY)
            .Distinct()
            .Order()
            .ToArray();

    private static List<(int X, int Y, ushort Type, ushort Wall, short FrameX, short FrameY)> CaptureProgressionStructureTiles(
        Workspace candidate)
    {
        var structureTypes = new HashSet<int>
        {
            VanillaTileIds.DemonAltar.Value,
            VanillaTileIds.Hellforge.Value,
            VanillaTileIds.Hive.Value,
            VanillaTileIds.LihzahrdBrick.Value,
            VanillaTileIds.LihzahrdAltar.Value,
            VanillaTileIds.MushroomGrass.Value,
            VanillaTileIds.Marble.Value,
            VanillaTileIds.Granite.Value,
            VanillaTileIds.Cobweb.Value
        };
        var tiles = new List<(int X, int Y, ushort Type, ushort Wall, short FrameX, short FrameY)>();
        for (int x = 0; x < candidate.WidthTiles; x++)
        for (int y = 0; y < candidate.HeightTiles; y++)
        {
            Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
            if (structureTypes.Contains(tile.Type) ||
                tile.Wall == VanillaWallIds.SpiderUnsafe.Value ||
                tile.Wall == VanillaWallIds.HiveUnsafe.Value ||
                tile.Wall == VanillaWallIds.LihzahrdBrickUnsafe.Value)
            {
                tiles.Add((x, y, tile.Type, tile.Wall, tile.FrameX, tile.FrameY));
            }
        }
        return tiles;
    }

    private static HashSet<WorldGenerationLiquidKind> CaptureLiquidKinds(Workspace candidate)
    {
        var kinds = new HashSet<WorldGenerationLiquidKind>();
        foreach (var tile in CaptureLiquidTiles(candidate))
            kinds.Add(tile.Kind);
        return kinds;
    }

    private static List<(int X, int Y, byte Amount, WorldGenerationLiquidKind Kind)> CaptureLiquidTiles(
        Workspace candidate)
    {
        var tiles = new List<(int X, int Y, byte Amount, WorldGenerationLiquidKind Kind)>();
        for (int x = 0; x < candidate.WidthTiles; x++)
        {
            for (int y = 0; y < candidate.HeightTiles; y++)
            {
                Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
                if (tile.LiquidAmount > 0)
                    tiles.Add((x, y, tile.LiquidAmount, tile.LiquidKind));
            }
        }

        return tiles;
    }
}
