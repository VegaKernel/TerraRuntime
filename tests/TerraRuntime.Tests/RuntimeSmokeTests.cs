namespace TerraRuntime.Tests;

public sealed class RuntimeSmokeTests
{
    [Fact]
    public void Test_project_is_running_on_net11()
    {
        Assert.StartsWith(".NET 11.", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    }
}
