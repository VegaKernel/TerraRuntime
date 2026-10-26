using TerraRuntime.Application;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

internal static class ToolPhaseWorldFixture1458
{
    internal static void BindFreshInactiveEvent(ServerRuntimeState state, WorldTileStore tiles)
    {
        // These original probes initialize a fresh non-DD2 scene. Supply that
        // explicit before-movement owner, independently from client buff199.
        var permission = new RuntimeToolBuildingPermission1458(tiles);
        Assert.True(permission.TrySetSourceEvent(false, null));
        state.BindToolBuildingPermission(permission);
    }
}
