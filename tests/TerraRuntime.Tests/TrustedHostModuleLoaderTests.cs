using TerraRuntime.Contracts.Runtime;
using TerraRuntime.ExtensibleHost;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostModuleFixture;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;

namespace TerraRuntime.Tests;

public sealed class TrustedHostModuleLoaderTests
{
    [Fact]
    public async Task StartAttachDetachStop_LoadsDropInModule_ThroughContractBoundary()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), $"terraruntime-host-{Guid.NewGuid():N}");
        string hostModules = Path.Combine(root, "HostModules");
        string serverPlugins = Path.Combine(root, "ServerPlugins");
        string worlds = Path.Combine(root, "Worlds");
        string config = Path.Combine(root, "config");
        string data = Path.Combine(root, "data");
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(hostModules);
        Directory.CreateDirectory(serverPlugins);
        Directory.CreateDirectory(worlds);
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);

        string modulePath = Path.Combine(hostModules, "Vega.dll");
        File.Copy(typeof(FixtureHostModule).Assembly.Location, modulePath);

        var loader = new TrustedHostModuleLoader(hostModules);
        var environment = new TestHostEnvironment(
            root,
            hostModules,
            serverPlugins,
            worlds,
            config,
            data,
            logs,
            loader.TerminalDashboards);
        var interestManagement = new TestInterestManagementControl();
        var runtime = new TestHostRuntime(
            new TerraRuntimeHostRuntimeInfo(
                "Fixture World",
                Path.Combine(worlds, "fixture.wld"),
                8400,
                2400,
                7777,
                32),
            interestManagement,
            new TestPlayerStateSnapshotReader());

        try
        {
            int loaded = await loader.StartAllAsync(environment, cancellationToken);
            await loader.AttachRuntimeAsync(runtime, cancellationToken);

            Assert.Equal(1, loaded);
            string startedMarker = Path.Combine(data, "fixture-host-module.started");
            Assert.True(File.Exists(startedMarker));
            Assert.Equal(
                serverPlugins,
                await File.ReadAllTextAsync(startedMarker, cancellationToken));

            ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> dashboardProviders = loader.CaptureDashboards();
            Assert.Single(dashboardProviders.ToArray());
            ITerraRuntimeTerminalDashboardProvider provider = dashboardProviders.Span[0];
            Assert.Equal(FixtureHostModule.DashboardId, provider.Id);
            Assert.Equal("Fixture Dashboard", provider.Title);

            using (IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI))
            using (View dashboard = provider.CreateDashboard())
            {
                Assert.NotNull(dashboard);
                provider.Refresh(dashboard);
            }

            string attachedMarker = Path.Combine(data, "fixture-host-module.attached");
            Assert.True(File.Exists(attachedMarker));
            Assert.Equal(
                "Fixture World|7777|False",
                await File.ReadAllTextAsync(attachedMarker, cancellationToken));

            await loader.DetachRuntimeAsync(cancellationToken);
            Assert.True(File.Exists(Path.Combine(data, "fixture-host-module.detached")));
        }
        finally
        {
            await loader.DisposeAsync();
        }

        Assert.Empty(loader.CaptureDashboards().ToArray());
        Assert.True(File.Exists(Path.Combine(data, "fixture-host-module.stopped")));

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A collectible AssemblyLoadContext becomes reclaimable asynchronously; cleanup is best-effort on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort cleanup rule as above.
        }
    }

    private sealed record TestHostEnvironment(
        string RootDirectory,
        string HostModulesDirectory,
        string ServerPluginsDirectory,
        string WorldsDirectory,
        string ConfigDirectory,
        string DataDirectory,
        string LogsDirectory,
        ITerraRuntimeTerminalDashboardRegistry TerminalDashboards) : ITerraRuntimeHostEnvironment;

    private sealed record TestHostRuntime(
        TerraRuntimeHostRuntimeInfo Info,
        IInterestManagementControl InterestManagement,
        IPlayerStateSnapshotReader PlayerStates) : ITerraRuntimeHostRuntime;

    private sealed class TestInterestManagementControl : IInterestManagementControl
    {
        public bool IsEnabled { get; private set; }

        public bool SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled)
                return false;

            IsEnabled = enabled;
            return true;
        }
    }

    private sealed class TestPlayerStateSnapshotReader : IPlayerStateSnapshotReader
    {
        public ValueTask<PlayerStateSnapshot?> CaptureAsync(
            PlayerHandle player,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<PlayerStateSnapshot?>(null);
        }
    }
}
