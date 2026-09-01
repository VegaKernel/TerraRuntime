using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class WorldRuntimeIdentityTests
{
    [Fact]
    public void Runtime_and_session_ids_reject_empty_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldRuntimeId(Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldSessionId(Guid.Empty));
    }

    [Fact]
    public void Runtime_identity_distinguishes_logical_world_from_live_session()
    {
        WorldRuntimeId runtimeId = WorldRuntimeId.CreateNew();
        WorldSessionId firstSession = WorldSessionId.CreateNew();
        WorldSessionId restartedSession = WorldSessionId.CreateNew();

        var first = new WorldRuntimeIdentity(runtimeId, firstSession);
        var restarted = new WorldRuntimeIdentity(runtimeId, restartedSession);

        Assert.True(first.IsAssigned);
        Assert.True(restarted.IsAssigned);
        Assert.Equal(first.RuntimeId, restarted.RuntimeId);
        Assert.NotEqual(first.SessionId, restarted.SessionId);
        Assert.NotEqual(first, restarted);
    }

    [Fact]
    public void Default_identity_is_unassigned()
    {
        Assert.False(default(WorldRuntimeId).IsAssigned);
        Assert.False(default(WorldSessionId).IsAssigned);
        Assert.False(default(WorldRuntimeIdentity).IsAssigned);
    }
}
