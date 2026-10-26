using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedToolReplantAuthority1458Tests
{
    [Fact]
    public void Source_regrowth_replant_keeps_ordered_roles_and_joint_custody() =>
        ToolPlayerBoundaryChecks.RunReplant();
}
