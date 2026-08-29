using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.HostModuleFixture;

public sealed class FixtureHostModule : ITerraRuntimeHostModule
{
    public const string DashboardId = "fixture.dashboard";

    private string? dataDirectory;
    private ITerraRuntimeTerminalDashboardRegistry? terminalDashboards;

    public string Name => "FixtureHostModule";

    public async ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        dataDirectory = environment.DataDirectory;
        terminalDashboards = environment.TerminalDashboards;
        if (!terminalDashboards.TryRegister(new FixtureDashboardProvider()))
            throw new InvalidOperationException("The fixture terminal dashboard could not be registered.");

        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.started"),
            environment.ServerPluginsDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (dataDirectory is null)
            throw new InvalidOperationException("The fixture host module has not been started.");

        string runtimeSummary = $"{runtime.Info.WorldName}|{runtime.Info.Port}|{runtime.InterestManagement.IsEnabled}";
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.attached"),
            runtimeSummary,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.detached"),
            "detached",
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        terminalDashboards?.TryUnregister(DashboardId);
        terminalDashboards = null;

        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.stopped"),
            "stopped",
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class FixtureDashboardProvider : ITerraRuntimeTerminalDashboardProvider
    {
        public string Id => DashboardId;

        public string Title => "Fixture Dashboard";

        public View CreateDashboard()
        {
            var root = new View
            {
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            root.Add(new Label
            {
                X = 1,
                Y = 1,
                Text = "Fixture dashboard"
            });
            return root;
        }

        public void Refresh(View rootView)
        {
            ArgumentNullException.ThrowIfNull(rootView);
        }
    }
}
