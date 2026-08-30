using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ObjectPlacementRateLimitTests
{
    [Fact]
    public void Hard_abuse_profile_bounds_packet79_fanout_requests()
    {
        Assert.True(ConnectionMessageRateLimits.HardAbuse.TryGet(
            (byte)TerrariaMessageId.PlaceObject,
            out ConnectionRateBudgetOptions budget));

        Assert.Equal(TimeSpan.FromSeconds(1), budget.Window);
        Assert.Equal(240, budget.MaxFrames);
        Assert.Equal(32 * 1024, budget.MaxBytes);
    }
}
