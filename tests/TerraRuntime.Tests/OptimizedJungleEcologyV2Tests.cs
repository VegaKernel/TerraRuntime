using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedJungleEcologyV2Tests
{
    [Theory]
    [InlineData(640, 1, 1)]
    [InlineData(4200, 1, 2)]
    [InlineData(4201, 2, 3)]
    [InlineData(6400, 2, 3)]
    [InlineData(6401, 3, 4)]
    [InlineData(8400, 3, 4)]
    public void Ecology_budgets_scale_with_world_width(int width, int hives, int mushrooms)
    {
        Assert.Equal(hives, OptimizedJungleEcologyV2.ResolveHiveTarget(width));
        Assert.Equal(mushrooms, OptimizedJungleEcologyV2.ResolveMushroomTarget(width));
    }

    [Fact]
    public void Final_plan_orders_jungle_ecology_before_progression_content_and_final_loot()
    {
        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Jungle ecology pass order",
            Seed: 0xEC0106UL,
            WidthTiles: 640,
            HeightTiles: 320);
        var builder = new CaptureBuilder();

        new OptimizedSurfaceDecorationWorldGenerationProvider().BuildPlan(in request, builder);

        int landmarkValidation = builder.IndexOf("terraruntime:optimized/landmark-validation");
        int ecology = builder.IndexOf("terraruntime:optimized/jungle-ecology-v2");
        int progressionContent = builder.IndexOf("terraruntime:optimized/progression-content");
        int surfaceShaping = builder.IndexOf("terraruntime:optimized/surface-shaping");
        int surfaceLife = builder.IndexOf("terraruntime:optimized/surface-life");
        int explorationLoot = builder.IndexOf("terraruntime:optimized/exploration-loot-v2");
        int progressionValidation = builder.IndexOf("terraruntime:optimized/progression-validation");

        Assert.True(landmarkValidation < ecology);
        Assert.True(ecology < progressionContent);
        Assert.True(progressionContent < surfaceShaping);
        Assert.True(surfaceShaping < surfaceLife);
        Assert.True(surfaceLife < explorationLoot);
        Assert.True(explorationLoot < progressionValidation);
        Assert.Contains(builder.Entries[progressionContent].RequiredAfter,
            static id => id.Value == "terraruntime:optimized/jungle-ecology-v2");
    }

    [Fact]
    public void Canonical_medium_builds_distinct_queen_bee_ready_hives_and_mushroom_pockets()
    {
        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Jungle ecology multi-hive",
            Seed: 0xBEE70UL,
            WidthTiles: 6400,
            HeightTiles: 1800);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} " +
            $"validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.NotNull(result.Candidate);
        RuntimeWorldGenerationWorkspace world = result.Candidate!;
        int requiredLifeCrystalTiles = checked(
            Math.Clamp(
                (int)Math.Ceiling((long)request.WidthTiles * request.HeightTiles / 145_000d),
                8,
                160) * 4);
        Assert.True(
            CountActiveTiles(world, 12) >= requiredLifeCrystalTiles,
            $"Final optimized candidate lost Life Crystal budget: {CountActiveTiles(world, 12)}/{requiredLifeCrystalTiles} tiles.");

        OptimizedJungleEcologyV2.HiveComponent[] hives = OptimizedJungleEcologyV2.CaptureHiveComponents(world);
        Assert.Equal(2, hives.Length);
        foreach (OptimizedJungleEcologyV2.HiveComponent hive in hives)
        {
            OptimizedJungleEcologyV2.HiveQuality quality = OptimizedJungleEcologyV2.InspectHive(world, hive);
            Assert.True(quality.DryInteriorCells >= 80,
                $"Hive at {hive.CenterX},{hive.CenterY} has only {quality.DryInteriorCells} dry interior cells.");
            Assert.True(quality.HoneyCells >= 16,
                $"Hive at {hive.CenterX},{hive.CenterY} has only {quality.HoneyCells} Honey cells.");
        }

        int larvaTiles = CountActiveTiles(world, checked((ushort)VanillaTileIds.Larva.Value));
        Assert.True(larvaTiles >= OptimizedProgressionContentWorldGenerationProvider.ResolveLarvaTarget(request.WidthTiles) * 9,
            $"Expected at least two complete 3x3 Larva anchors, found {larvaTiles} Larva tiles.");

        int mushroomComponents = CountMaterialComponents(
            world,
            checked((ushort)VanillaTileIds.MushroomGrass.Value),
            minimumCells: 8);
        Assert.True(mushroomComponents >= OptimizedJungleEcologyV2.ResolveMushroomTarget(request.WidthTiles),
            $"Expected at least {OptimizedJungleEcologyV2.ResolveMushroomTarget(request.WidthTiles)} glowing-mushroom material components, found {mushroomComponents}.");
    }

    [Fact]
    public void Algorithm_version_is_explicit() => Assert.Equal(2, OptimizedJungleEcologyV2.AlgorithmVersion);

    private static int CountActiveTiles(RuntimeWorldGenerationWorkspace world, ushort type)
    {
        int count = 0;
        for (int y = 0; y < world.HeightTiles; y++)
        for (int x = 0; x < world.WidthTiles; x++)
        {
            if (world.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type)
                count++;
        }
        return count;
    }

    private static int CountMaterialComponents(RuntimeWorldGenerationWorkspace world, ushort type, int minimumCells)
    {
        int width = world.WidthTiles;
        int height = world.HeightTiles;
        var visited = new bool[checked(width * height)];
        var queue = new Queue<int>();
        int components = 0;

        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            int index = y * width + x;
            if (visited[index] || !IsType(x, y))
                continue;
            visited[index] = true;
            queue.Enqueue(index);
            int cells = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cy = current / width;
                int cx = current - cy * width;
                cells++;
                Visit(cx - 1, cy);
                Visit(cx + 1, cy);
                Visit(cx, cy - 1);
                Visit(cx, cy + 1);
            }
            if (cells >= minimumCells)
                components++;

            void Visit(int nx, int ny)
            {
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    return;
                int next = ny * width + nx;
                if (visited[next] || !IsType(nx, ny))
                    return;
                visited[next] = true;
                queue.Enqueue(next);
            }
        }
        return components;

        bool IsType(int x, int y) =>
            world.TryGetTile(x, y, out WorldGenerationTile tile) &&
            (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type;
    }

    private readonly record struct CapturedPass(WorldGenerationPassId Id, WorldGenerationPassId[] RequiredAfter);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CapturedPass> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            _ = pass;
            Entries.Add(new CapturedPass(descriptor.Id, descriptor.RequiredAfter.ToArray()));
        }

        public int IndexOf(string id)
        {
            int index = Entries.FindIndex(entry => entry.Id.Value == id);
            Assert.True(index >= 0, $"Pass '{id}' is missing.");
            return index;
        }
    }
}