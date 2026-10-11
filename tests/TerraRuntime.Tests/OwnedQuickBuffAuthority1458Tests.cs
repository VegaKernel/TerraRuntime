using Xunit;
namespace TerraRuntime.Tests;
public sealed class OwnedQuickBuffAuthority1458Tests
{
    [Fact]
    public void Original_sync_batches_require_joint_funded_quick_buff_reconciliation()
        => QuickBuffOwnedBoundary1458Checks.Run();
}
