using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedTorchGodFavorAuthority1458Tests
{
    [Fact]
    public void Paid_favor_adopts_inventory_unlock_and_scripted_credit_together()
    {
        TorchFavorBoundaryChecks.RunTorch();
        TorchFavorBoundaryChecks.RunBoundaries();
        TorchFavorBoundaryChecks.RunCrafting();
    }
}
