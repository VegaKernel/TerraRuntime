namespace TerraRuntime.Tests;

public sealed class RuntimeSmokeTests
{
    [Fact]
    public void Test_project_is_running_on_net11()
    {
        Assert.StartsWith(".NET 11.", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    }

    [Fact]
    public void Runtime_actor_and_commerce_smoke_passes()
    {
        Assert.True(RuntimeActorCommerceSmoke.Run(out string failure), failure);
    }
}
