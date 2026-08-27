using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InitialSectionBootstrapPlannerTests
{
    [Fact]
    public void Plans_vanilla_five_by_three_window_around_normal_spawn()
    {
        var dimensions = new WorldDimensions(4_200, 1_200);
        Span<WorldSectionId> sections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];

        int count = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            dimensions,
            spawnTileX: 2_000,
            spawnTileY: 600,
            sections);

        Assert.Equal(15, count);
        Assert.Equal(new WorldSectionId(8, 3), sections[0]);
        Assert.Equal(new WorldSectionId(8, 5), sections[2]);
        Assert.Equal(new WorldSectionId(12, 3), sections[12]);
        Assert.Equal(new WorldSectionId(12, 5), sections[14]);
    }

    [Fact]
    public void Preserves_vanilla_pre_clamp_window_shrink_at_world_edge()
    {
        var dimensions = new WorldDimensions(4_200, 1_200);
        Span<WorldSectionId> sections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];

        int count = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            dimensions,
            spawnTileX: 0,
            spawnTileY: 0,
            sections);

        Assert.Equal(6, count);
        Assert.Equal(new WorldSectionId(0, 0), sections[0]);
        Assert.Equal(new WorldSectionId(0, 1), sections[1]);
        Assert.Equal(new WorldSectionId(2, 0), sections[4]);
        Assert.Equal(new WorldSectionId(2, 1), sections[5]);
    }

    [Fact]
    public void Rejects_invalid_spawn_and_short_destination()
    {
        var dimensions = new WorldDimensions(400, 300);
        Span<WorldSectionId> enough = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        Span<WorldSectionId> shortBuffer = stackalloc WorldSectionId[1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, -1, 0, enough));
        Assert.Throws<ArgumentException>(() =>
            InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, 0, 0, shortBuffer));
    }
}
