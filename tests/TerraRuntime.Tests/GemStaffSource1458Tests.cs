using Xunit;
namespace TerraRuntime.Tests;
public sealed class GemStaffSource1458Tests
{
    [Fact]
    public void Original_staff_prefixes_robe_features_and_item_checks_preserve_payment_and_ordered_births() =>
        Assert.True(GemStaffSource1458Checks.Run() > 0);
}
