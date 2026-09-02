using TerraRuntime.World;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldTimeSemanticsTests
{
    [Theory]
    [InlineData(0, VanillaMoonPhase.Full)]
    [InlineData(1, VanillaMoonPhase.ThreeQuartersAtLeft)]
    [InlineData(4, VanillaMoonPhase.Empty)]
    [InlineData(7, VanillaMoonPhase.ThreeQuartersAtRight)]
    public void Raw_moon_phases_cross_through_the_versioned_catalog(
        byte rawValue,
        VanillaMoonPhase expected)
    {
        Assert.True(VanillaMoonPhases.TryCreate(rawValue, out VanillaMoonPhase phase));
        Assert.Equal(expected, phase);
    }

    [Fact]
    public void Unknown_raw_phase_fails_and_cycle_wraps_to_full()
    {
        Assert.False(VanillaMoonPhases.TryCreate(VanillaMoonPhases.Count, out _));
        Assert.Equal(
            VanillaMoonPhase.Full,
            VanillaMoonPhases.Next(VanillaMoonPhase.ThreeQuartersAtRight));
    }
}
