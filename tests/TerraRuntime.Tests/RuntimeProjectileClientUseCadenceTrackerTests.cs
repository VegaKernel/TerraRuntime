using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileClientUseCadenceTrackerTests
{
    [Fact]
    public void CooldownIsScopedToExactPlayerGeneration()
    {
        var tracker = new RuntimeProjectileClientUseCadenceTracker();
        var first = new PlayerHandle(new PlayerSlotId(7), new PlayerSessionGeneration(1));
        var reused = new PlayerHandle(new PlayerSlotId(7), new PlayerSessionGeneration(2));

        Assert.False(tracker.IsOnCooldown(first, tick: 100, useTimeTicks: 20));
        tracker.MarkUse(first, tick: 100);
        Assert.True(tracker.IsOnCooldown(first, tick: 119, useTimeTicks: 20));
        Assert.False(tracker.IsOnCooldown(first, tick: 120, useTimeTicks: 20));

        Assert.False(tracker.IsOnCooldown(reused, tick: 101, useTimeTicks: 20));
        tracker.MarkUse(reused, tick: 101);
        Assert.True(tracker.IsOnCooldown(reused, tick: 102, useTimeTicks: 20));
    }
}
