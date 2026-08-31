using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedWorldGenerationProviderTests
{
    [Fact]
    public void Optimized_generator_is_registered_and_builds_required_progression_geography()
    {
        BuiltInWorldGeneratorSource source = BuiltInWorldGeneratorSource.Instance;
        Assert.Contains(
            OptimizedWorldGenerationProvider.GeneratorId,
            source.CaptureWorldGeneratorIds().Span.ToArray());
        Assert.True(
            source.TryResolveWorldGenerator(
                OptimizedWorldGenerationProvider.GeneratorId,
                out IWorldGenerationProvider? provider));
        Assert.NotNull(provider);
        Assert.IsType<OptimizedPlayableWorldGenerationProvider>(provider);

        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Optimized",
            Seed: 0x5EEDC0DEUL,
            WidthTiles: 640,
            HeightTiles: 320);
        var pipeline = new RuntimeWorldCreationPipeline(source);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.NotNull(result.Candidate);
        RuntimeWorldGenerationWorkspace world = result.Candidate!;

        Assert.Equal(320, result.Metadata.Spawn.X);
        AssertSpawnHasGround(world, result.Metadata.Spawn);
        Assert.True(result.Metadata.Dungeon.X < request.WidthTiles / 3 || result.Metadata.Dungeon.X > request.WidthTiles * 2 / 3);
        Assert.True(result.Metadata.Layers.WorldSurface > 0d);
        Assert.True(result.Metadata.Layers.RockLayer > result.Metadata.Layers.WorldSurface);

        Assert.True(ContainsActiveTile(world, 41), "Dungeon brick must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.LihzahrdBrick.Value)), "Jungle Temple must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Hive.Value)), "Hive must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.DemonAltar.Value)), "Evil altar must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Hellforge.Value)), "Hellforge must exist.");
        Assert.True(ContainsActiveTile(world, 58), "Hellstone must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Water), "Water must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Lava), "Lava must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Honey), "Honey must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Shimmer), "Shimmer must exist.");

        int skyLimit = Math.Max(1, (int)result.Metadata.Layers.WorldSurface - 20);
        Assert.True(CountActiveTilesAbove(world, skyLimit) >= 90, "Floating-island terrain must exist above the normal surface.");

        Assert.True(CountActiveTiles(world, 12) >= 32, "A 640x320 optimized world must contain at least eight complete Life Crystals.");
        Assert.True(world.GeneratedChestCount >= 7, "The optimized playability overlay must persist surface/underground/cavern cache budgets.");
        Assert.True(ContainsInteriorWaterBelow(world, (int)result.Metadata.Layers.RockLayer), "Organic cavern generation must include inland underground water.");
        Assert.Contains(
            world.CaptureGeneratedChests(),
            static chest => chest.Items.Any(static item => !item.IsEmpty));
    }

    [Fact]
    public void Optimized_generator_replays_deterministically_for_same_seed()
    {
        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Optimized deterministic",
            Seed: 123456789UL,
            WidthTiles: 512,
            HeightTiles: 240);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.Equal(first.Metadata, second.Metadata);
        Assert.NotNull(first.Candidate);
        Assert.NotNull(second.Candidate);
        Assert.Equal(first.Candidate!.GeneratedChestCount, second.Candidate!.GeneratedChestCount);

        for (int y = 0; y < request.HeightTiles; y += 7)
        {
            for (int x = 0; x < request.WidthTiles; x += 7)
            {
                Assert.True(first.Candidate.TryGetTile(x, y, out WorldGenerationTile a));
                Assert.True(second.Candidate.TryGetTile(x, y, out WorldGenerationTile b));
                Assert.Equal(a, b);
            }
        }

        AssertGeneratedChestsEqual(
            first.Candidate.CaptureGeneratedChests(),
            second.Candidate.CaptureGeneratedChests());
    }

    private static void AssertSpawnHasGround(
        RuntimeWorldGenerationWorkspace workspace,
        WorldGenerationPoint spawn)
    {
        Assert.True(workspace.TryGetTile(spawn.X, spawn.Y, out WorldGenerationTile spawnTile));
        Assert.Equal(WorldGenerationTileFlags.None, spawnTile.Flags & WorldGenerationTileFlags.Active);

        bool foundGround = false;
        for (int dy = 1; dy <= 3 && spawn.Y + dy < workspace.HeightTiles; dy++)
        {
            Assert.True(workspace.TryGetTile(spawn.X, spawn.Y + dy, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
            {
                foundGround = true;
                break;
            }
        }

        Assert.True(foundGround, "Spawn must have solid ground within three tiles below.");
    }

    private static bool ContainsActiveTile(RuntimeWorldGenerationWorkspace workspace, ushort type) =>
        CountActiveTiles(workspace, type) > 0;

    private static int CountActiveTiles(RuntimeWorldGenerationWorkspace workspace, ushort type)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type == type)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool ContainsLiquid(
        RuntimeWorldGenerationWorkspace workspace,
        WorldGenerationLiquidKind kind)
    {
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == kind)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsInteriorWaterBelow(
        RuntimeWorldGenerationWorkspace workspace,
        int minY)
    {
        int margin = Math.Clamp(workspace.WidthTiles / 8, 50, 120);
        for (int y = Math.Clamp(minY, 1, workspace.HeightTiles - 2); y < workspace.HeightTiles * 4 / 5; y++)
        {
            for (int x = margin; x < workspace.WidthTiles - margin; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == WorldGenerationLiquidKind.Water)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountActiveTilesAbove(RuntimeWorldGenerationWorkspace workspace, int yExclusive)
    {
        int count = 0;
        for (int y = 0; y < yExclusive; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertGeneratedChestsEqual(WorldChest[] expected, WorldChest[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            WorldChest a = expected[index];
            WorldChest b = actual[index];
            Assert.Equal(a.SlotId, b.SlotId);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Items.Length, b.Items.Length);
            for (int slot = 0; slot < a.Items.Length; slot++)
                Assert.Equal(a.Items[slot], b.Items[slot]);
        }
    }
}
