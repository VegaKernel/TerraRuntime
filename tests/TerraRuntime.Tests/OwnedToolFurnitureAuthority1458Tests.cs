using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedToolFurnitureAuthority1458Tests
{
    [Fact]
    public void Source_special_hammer_squares_preserve_wire_roles_and_joint_ownership() =>
        ToolPlayerBoundaryChecks.RunFurniture();
}
