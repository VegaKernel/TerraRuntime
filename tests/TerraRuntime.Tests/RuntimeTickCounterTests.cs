namespace TerraRuntime.Tests;

public sealed class RuntimeTickCounterTests
{
    [Fact]
    public void AdvancesMonotonicallyOnlyWhenExplicitlyAdvanced()
    {
        var counter = new RuntimeTickCounter();

        Assert.Equal(0, counter.Current);
        counter.Advance();
        counter.Advance();
        Assert.Equal(2, counter.Current);
    }
}
