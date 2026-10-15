using Xunit;
namespace TerraRuntime.Tests;
public sealed class SelectedFoodConsumable1458Tests
{
    [Fact]
    public void Original_food_metadata_and_selected_use_preserve_source_tiers_clocks_and_random()
    {
        Assert.True(FoodSelectedConsumable1458Checks.RunMetadata() > 0);
        Assert.True(FoodSelectedBehavior1458Checks.Run() > 0);
        Assert.True(FoodQuickBuff1458Checks.Run() > 0);
        Assert.True(FoodConsumer1458Checks.Run() > 0);
    }
}
