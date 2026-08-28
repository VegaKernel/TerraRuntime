using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class InterestManagementControlTests
{
    [Fact]
    public void Control_is_disabled_by_default_and_reports_real_state_changes()
    {
        IInterestManagementControl control = new InterestManagementControl();

        Assert.False(control.IsEnabled);
        Assert.True(control.SetEnabled(true));
        Assert.True(control.IsEnabled);
        Assert.False(control.SetEnabled(true));
        Assert.True(control.SetEnabled(false));
        Assert.False(control.IsEnabled);
    }

    [Fact]
    public void Host_options_enable_interest_management_from_startup_flag()
    {
        bool parsed = ServerHostOptions.TryParse(
            ["--world", "test.wld", "--interest-management"],
            out ServerHostOptions? options,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.True(options.InterestManagementEnabled);
    }
}
