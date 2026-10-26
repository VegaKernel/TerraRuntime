using Xunit;

namespace TerraRuntime.Tests.Tools1458;

public sealed class AllLocalTools1458Tests
{
    [Fact]
    public void Original_tool_metadata_clocks_hit_cache_power_and_projectile_stages_match()
        => AllLocalToolSourceChecks.Run();
}
