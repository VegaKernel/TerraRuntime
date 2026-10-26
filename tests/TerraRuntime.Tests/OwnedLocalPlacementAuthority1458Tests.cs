using Xunit;
namespace TerraRuntime.Tests.ToolPlayer;
public sealed class OwnedLocalPlacementAuthority1458Tests
{
    [Fact]
    public void Raw_placement_cannot_authorize_world_edits_or_restore_unknown_local_clocks()
        => ToolPlayerBoundaryChecks.PlacementRuntime();
}
