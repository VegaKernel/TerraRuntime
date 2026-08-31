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

        for (int y = 0; y < request.HeightTiles; y += 7)
        {
            for (int x = 0; x < request.WidthTiles; x += 7)
            {
                Assert.True(first.Candidate!.TryGetTile(x, y, out WorldGenerationTile a));
                Assert.True(second.Candidate!.TryGetTile(x, y, out WorldGenerationTile b));
                Assert.Equal(a, b);
            }
        }
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

    private static bool ContainsActiveTile(RuntimeWorldGenerationWorkspace workspace, ushort type)
    {
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type == type)
                {
                    return true;
                }
            }
        }

        return false;
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
}
