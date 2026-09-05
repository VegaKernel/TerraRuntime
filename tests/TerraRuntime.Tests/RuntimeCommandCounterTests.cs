namespace TerraRuntime.Tests;

public sealed class RuntimeCommandCounterTests
{
    [Fact]
    public void RecordsAuthoritativeCommandsMonotonically()
    {
        var counter = new RuntimeCommandCounter();

        Assert.Equal(0, counter.Current);
        counter.Record();
        counter.Record();
        Assert.Equal(2, counter.Current);
    }
}
