using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeDashboardAdminOperationsTests
{
    [Fact]
    public void Interest_management_changes_cross_the_authoritative_command_queue()
    {
        var control = new InterestManagementControl(enabled: false);
        var state = new ServerRuntimeState();
        using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var admission = new TerrariaConnectionAdmissionGate(maxConnections: 8);
        var operations = new LocalRuntimeDashboardOperations(
            loop,
            admission,
            control,
            worldName: "AdminTest",
            worldWidthTiles: 4200,
            worldHeightTiles: 1200,
            port: 7777,
            maxPlayers: 8,
            targetTicksPerSecond: 60);

        Assert.True(operations.TrySetInterestManagementEnabled(true));
        Assert.False(control.IsEnabled);

        loop.Start();
        Assert.True(SpinWait.SpinUntil(() => control.IsEnabled, TimeSpan.FromSeconds(2)));

        Assert.True(operations.TrySetInterestManagementEnabled(false));
        Assert.True(SpinWait.SpinUntil(() => !control.IsEnabled, TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => state.AppliedCommands >= 2, TimeSpan.FromSeconds(2)));
        Assert.True(loop.Stop(TimeSpan.FromSeconds(2)));
    }
}
