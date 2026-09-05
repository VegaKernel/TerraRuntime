using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests;

public sealed class RuntimeInfoTests
{
    [Fact]
    public void Existing_host_constructor_produces_assigned_in_process_persistent_identity()
    {
        var info = new RuntimeInfo(
            "Primary",
            "Worlds/primary.wld",
            4200,
            1200,
            7777,
            32);

        Assert.True(info.RuntimeIdentity.IsAssigned);
        Assert.Equal(WorldIsolationLevel.InProcess, info.IsolationLevel);
        Assert.Equal(WorldPersistenceMode.Persistent, info.PersistenceMode);
    }

    [Fact]
    public void Separately_created_runtime_infos_do_not_share_world_identity()
    {
        var first = new RuntimeInfo("Arena", "", 640, 240, 0, 8);
        var second = new RuntimeInfo("Arena", "", 640, 240, 0, 8);

        Assert.NotEqual(first.RuntimeIdentity.RuntimeId, second.RuntimeIdentity.RuntimeId);
        Assert.NotEqual(first.RuntimeIdentity.SessionId, second.RuntimeIdentity.SessionId);
    }

    [Fact]
    public void Sandbox_policy_is_orthogonal_to_existing_deployment_fields()
    {
        var info = new RuntimeInfo("Arena", "", 640, 240, 0, 8)
        {
            IsolationLevel = WorldIsolationLevel.DedicatedProcess,
            PersistenceMode = WorldPersistenceMode.Ephemeral
        };

        Assert.True(info.RuntimeIdentity.IsAssigned);
        Assert.Equal(WorldIsolationLevel.DedicatedProcess, info.IsolationLevel);
        Assert.Equal(WorldPersistenceMode.Ephemeral, info.PersistenceMode);
    }
}
