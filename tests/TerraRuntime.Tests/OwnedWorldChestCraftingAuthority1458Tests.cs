using TerraRuntime.Tests.WorldChestCrafting;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class OwnedWorldChestCraftingAuthority1458Tests
{
    [Fact]
    public void Original_world_transfers_and_remote_craft_costs_require_joint_funded_results()
    {
        Assert.True(WorldChestTransferChecks.Run() > 0);
        Assert.True(RemoteCraftOutputChecks.Run() > 0);
        Assert.True(RemoteCraftSecurityChecks.Run() > 0);
        Assert.True(RemoteCraftLoopChecks.Run() > 0);
    }
}
