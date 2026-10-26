using Xunit;
namespace TerraRuntime.Tests.ToolPlayer;
public sealed class OwnedToolSingleCellFrame1458Tests
{
    [Fact]
    public void Original_single_cell_anchor_frames_preserve_failed_tool_roles_and_random()
        => ToolPlayerBoundaryChecks.RunSingleCellFrames();
}
