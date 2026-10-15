using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedFoodAuthority1458Tests
{
    [Fact]
    public void Paid_food_and_source_quick_buff_preserve_custody_and_provenance()
    {
        FoodAuthorityBoundaryChecks.IndependentCases();
        FoodAuthorityBoundaryChecks.SevenFoodBatch();
        FoodConsumerBoundaryChecks.Run();
    }

    [Fact]
    public void Owned_food_clocks_rebase_only_actual_clock_changes_and_expire()
        => FoodClockBoundaryChecks.Run();
}
