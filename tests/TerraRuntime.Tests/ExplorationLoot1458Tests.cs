using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedExplorationLoot1458Tests
{
    private static readonly HashSet<int> SkywarePrimary = [159, 65, 158, 2219];
    private static readonly HashSet<int> IcePrimary = [670, 724, 950, 1319, 987, 1579, 6153];
    private static readonly HashSet<int> JunglePrimary = [211, 212, 213, 964, 2292, 3017];
    private static readonly HashSet<int> DesertPrimary = [4056, 4055, 4262, 4263, 4061, 4062, 4276];
    private static readonly HashSet<int> OceanPrimary = [863, 186, 277, 187, 4404];
    private static readonly HashSet<int> OrdinaryPrimary =
    [
        280, 281, 284, 285, 953, 946, 3068, 3069, 3084, 4341, 6165,
        49, 50, 53, 54, 5011, 975,
        670, 724, 950, 1319, 987, 1579, 6153,
        211, 212, 213, 964, 2292, 3017,
        4056, 4055, 4262, 4263, 4061, 4062, 4276
    ];

    [Fact]
    public void Optimized_world_uses_source_backed_skyware_and_representative_biome_loot_families()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Source-backed exploration loot",
            Seed: 0x10_07_1458UL,
            WidthTiles: 640,
            HeightTiles: 320);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} " +
            $"validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.NotNull(result.Candidate);
        WorldChest[] chests = result.Candidate!.CaptureGeneratedChests();

        WorldChest[] sky = chests
            .Where(static chest => chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal))
            .OrderBy(static chest => chest.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(sky);
        for (int i = 0; i < sky.Length; i++)
        {
            int primary = Primary(sky[i]);
            Assert.Contains(primary, SkywarePrimary);
            if (i < 4)
                Assert.Equal(new[] { 159, 65, 158, 2219 }[i], primary);
        }

        AssertPrimary(chests, "Snow Biome Cache", IcePrimary);
        AssertPrimary(chests, "Jungle Biome Cache", JunglePrimary);
        AssertPrimary(chests, "Desert Biome Cache", DesertPrimary);
        AssertPrimary(chests, "Ocean Cache Left", OceanPrimary);
        AssertPrimary(chests, "Ocean Cache Right", OceanPrimary);

        WorldChest[] ordinary = chests.Where(static chest =>
            chest.Name.StartsWith("Surface Cache ", StringComparison.Ordinal) ||
            chest.Name.StartsWith("Underground Cache ", StringComparison.Ordinal) ||
            chest.Name.StartsWith("Cavern Cache ", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(ordinary);
        foreach (WorldChest chest in ordinary)
        {
            Assert.Contains(Primary(chest), OrdinaryPrimary);
            Assert.True(chest.Items.Count(static item => !item.IsEmpty) >= 4, $"{chest.Name} lost useful common chest loot.");
        }
    }

    [Fact]
    public void Exploration_loot_runs_after_surface_life_and_before_final_progression_validation()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Exploration loot order",
            Seed: 1458,
            WidthTiles: 640,
            HeightTiles: 320);
        var builder = new CaptureBuilder();
        new SurfaceDecorationProvider().BuildPlan(in request, builder);

        CapturedPass surfaceLife = Assert.Single(builder.Entries, static x => x.Id.Value == "terraruntime:optimized/surface-life");
        CapturedPass loot = Assert.Single(builder.Entries, static x => x.Id.Value == "terraruntime:optimized/exploration-loot-v2");
        CapturedPass validation = Assert.Single(builder.Entries, static x => x.Id.Value == "terraruntime:optimized/progression-validation");

        Assert.Contains(surfaceLife.Id, loot.RequiredAfter);
        Assert.Contains(loot.Id, validation.RequiredAfter);
    }

    private static void AssertPrimary(WorldChest[] chests, string name, HashSet<int> family)
    {
        WorldChest chest = Assert.Single(chests, chest => chest.Name == name);
        Assert.Contains(Primary(chest), family);
    }

    private static int Primary(WorldChest chest) =>
        chest.Items.First(static item => !item.IsEmpty).ItemType;

    private readonly record struct CapturedPass(
        WorldGenerationPassId Id,
        WorldGenerationPassId[] RequiredAfter);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CapturedPass> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            _ = pass;
            Entries.Add(new CapturedPass(descriptor.Id, descriptor.RequiredAfter.ToArray()));
        }
    }
}
