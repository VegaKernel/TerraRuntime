using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedToolPlayerAuthority1458Tests
{
    [Fact]
    public void Ordered_tool_reports_preserve_player_clock_health_and_selection_ownership() =>
        ToolPlayerBoundaryChecks.Run();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Good_grounded_pickaxe_equipment_has_a_bounded_stationary_phase(bool joins_with_idle_rod) =>
        ToolPlayerBoundaryChecks.GoodGroundedPickaxeEquipment(joins_with_idle_rod);
}
