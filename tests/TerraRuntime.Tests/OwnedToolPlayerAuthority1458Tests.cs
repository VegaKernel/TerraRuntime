using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedToolPlayerAuthority1458Tests
{
    [Fact]
    public void Ordered_tool_reports_preserve_player_clock_health_and_selection_ownership() =>
        ToolPlayerBoundaryChecks.Run();
}
