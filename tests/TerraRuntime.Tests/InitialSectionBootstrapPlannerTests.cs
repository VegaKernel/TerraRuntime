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
    public void Plans_optional_requested_window_with_same_section_geometry()
    {
        var dimensions = new WorldDimensions(4_200, 1_200);
        Span<WorldSectionId> sections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumRequestedSectionCount];

        int count = InitialSectionBootstrapPlanner.PlanRequestedSections(
            dimensions,
            tileX: 3_000,
            tileY: 750,
            sections);

        Assert.Equal(15, count);
        Assert.Equal(new WorldSectionId(13, 4), sections[0]);
        Assert.Equal(new WorldSectionId(17, 6), sections[14]);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(9, 100)]
    [InlineData(100, 9)]
    [InlineData(4_191, 100)]
    [InlineData(100, 1_191)]
    public void Ignores_missing_or_edge_guarded_requested_position(int tileX, int tileY)
    {
        var dimensions = new WorldDimensions(4_200, 1_200);
        Span<WorldSectionId> sections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumRequestedSectionCount];

        int count = InitialSectionBootstrapPlanner.PlanRequestedSections(dimensions, tileX, tileY, sections);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Rejects_invalid_spawn_and_short_destination()
    {
        var dimensions = new WorldDimensions(400, 300);
        var enough = new WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        var shortBuffer = new WorldSectionId[1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, -1, 0, enough));
        Assert.Throws<ArgumentException>(() =>
            InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, 0, 0, shortBuffer));
        Assert.Throws<ArgumentException>(() =>
            InitialSectionBootstrapPlanner.PlanRequestedSections(dimensions, 100, 100, shortBuffer));
    }
}
