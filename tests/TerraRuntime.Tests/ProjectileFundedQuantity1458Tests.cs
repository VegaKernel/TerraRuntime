namespace TerraRuntime.Tests;

public sealed class ProjectileFundedQuantity1458Tests
{
    [Xunit.Fact]
    public void Validated_projectile_consumption_debits_existing_custody_once_without_rejecting_imported_zero_budget()
    {
        Xunit.Assert.Equal(119, ProjectileFundedQuantity1458Checks.Run());
    }
}
