using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerSectionStreamingStateTests
{
    [Fact]
    public void Crossing_one_horizontal_section_plans_only_the_new_three_section_fringe()
    {
        var dimensions = new WorldDimensions(4200, 1200);
        const int spawnX = 2100;
        const int spawnY = 300;
        var state = new PlayerSectionStreamingState(dimensions);
        Span<WorldSectionId> bootstrap = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int bootstrapCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            dimensions,
            spawnX,
            spawnY,
            bootstrap);
        state.ObserveBootstrap(bootstrap[..bootstrapCount], -1, -1);

        WorldSectionId center = TerrariaSectionGeometry.FromTile(dimensions, spawnX, spawnY);
        int nextCenterTileX = (center.X + 1) * TerrariaSectionGeometry.WidthTiles + 10;
        Span<WorldSectionId> planned = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];
        int count = state.PlanUnsent(nextCenterTileX * 16f, spawnY * 16f, planned);

        Assert.Equal(3, count);
        Assert.All(planned[..count].ToArray(), section => Assert.Equal(center.X + 3, section.X));
        for (int i = 0; i < count; i++)
            state.MarkSent(planned[i]);
        Assert.Equal(0, state.PlanUnsent(nextCenterTileX * 16f, spawnY * 16f, planned));
    }

    [Fact]
    public void Walking_far_across_canonical_small_world_keeps_planning_valid_unseen_windows()
    {
        var dimensions = new WorldDimensions(4200, 1200);
        var state = new PlayerSectionStreamingState(dimensions);
        Span<WorldSectionId> bootstrap = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int bootstrapCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(
            dimensions,
            2100,
            300,
            bootstrap);
        state.ObserveBootstrap(bootstrap[..bootstrapCount], -1, -1);

        Span<WorldSectionId> planned = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];
        for (int tileX = 2400; tileX <= 4000; tileX += 200)
        {
            int count = state.PlanUnsent(tileX * 16f, 300 * 16f, planned);
            Assert.InRange(count, 0, PlayerSectionStreamingState.MaximumWindowSectionCount);
            for (int i = 0; i < count; i++)
            {
                Assert.InRange(planned[i].X, 0, dimensions.SectionColumns - 1);
                Assert.InRange(planned[i].Y, 0, dimensions.SectionRows - 1);
                state.MarkSent(planned[i]);
            }
        }

        WorldSectionId farSection = TerrariaSectionGeometry.FromTile(dimensions, 4000, 300);
        Assert.True(state.SentSectionCount > bootstrapCount);
        Assert.Equal(0, state.PlanUnsent(4000 * 16f, 300 * 16f, planned));
        Assert.InRange(farSection.X, dimensions.SectionColumns - 2, dimensions.SectionColumns - 1);
    }

    [Fact]
    public void Requested_packet8_window_is_not_restreamed_on_first_movement()
    {
        var dimensions = new WorldDimensions(4200, 1200);
        var state = new PlayerSectionStreamingState(dimensions);
        Span<WorldSectionId> bootstrap = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int bootstrapCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, 2100, 300, bootstrap);
        state.ObserveBootstrap(bootstrap[..bootstrapCount], 2100, 300);

        Span<WorldSectionId> planned = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];
        Assert.Equal(0, state.PlanUnsent(2100 * 16f, 300 * 16f, planned));
    }

    [Fact]
    public void Section_ownership_only_covers_transferred_sections_and_never_leaves_the_world()
    {
        var dimensions = new WorldDimensions(4200, 1200);
        var state = new PlayerSectionStreamingState(dimensions);
        Span<WorldSectionId> bootstrap = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int bootstrapCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, 2100, 300, bootstrap);
        state.ObserveBootstrap(bootstrap[..bootstrapCount], 2100, 300);

        // Vanilla gates every world-cell relay on TileSections[x / 200, y / 150]; a spawn-window client owns its
        // own section and nothing on the far side of the world.
        Assert.True(state.OwnsSectionAtTile(2100, 300));
        Assert.False(state.OwnsSectionAtTile(10, 1100));
        Assert.False(state.OwnsSectionAtTile(-1, 300));
        Assert.False(state.OwnsSectionAtTile(2100, 1200));

        WorldSectionId far = TerrariaSectionGeometry.FromTile(dimensions, 10, 1100);
        state.MarkSent(far);
        Assert.True(state.OwnsSectionAtTile(10, 1100));
    }

    [Fact]
    public void Invalid_world_positions_do_not_request_sections()
    {
        var state = new PlayerSectionStreamingState(new WorldDimensions(4200, 1200));
        Span<WorldSectionId> planned = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];

        Assert.Equal(0, state.PlanUnsent(float.NaN, 0f, planned));
        Assert.Equal(0, state.PlanUnsent(-16f, 100f, planned));
        Assert.Equal(0, state.PlanUnsent(4200 * 16f, 100f, planned));
    }
}
