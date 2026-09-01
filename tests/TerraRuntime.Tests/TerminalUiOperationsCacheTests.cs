using TerraRuntime.Operations;
using TerraRuntime.TerminalUI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Tests;

public sealed class TerminalUiOperationsCacheTests
{
    [Fact]
    public async Task Ui_reads_keep_previous_snapshot_while_background_capture_is_blocked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var source = new BlockingOperations();
        var cache = new TerminalUiOperationsCache(
            source,
            source,
            source,
            source,
            source,
            source);
        IRuntimeDashboardOperations dashboard = cache;

        Assert.Equal(1, dashboard.CaptureSnapshot().Tick);
        Assert.Equal(0, cache.Version);

        source.BlockDashboardCapture();
        Task refresh = Task.Run(cache.Refresh, cancellationToken);
        Assert.True(source.WaitUntilDashboardCaptureStarted(TimeSpan.FromSeconds(2), cancellationToken));

        // The TUI read must not wait for the in-progress source capture. It consumes the last atomically
        // published detached state and remains free to process keyboard/mouse input.
        Assert.Equal(1, dashboard.CaptureSnapshot().Tick);
        Assert.False(refresh.IsCompleted);

        source.ReleaseDashboardCapture();
        await refresh.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.Equal(2, dashboard.CaptureSnapshot().Tick);
        Assert.Equal(1, cache.Version);
    }

    [Fact]
    public void Focused_panel_scheme_is_visibly_distinct_from_base_panel()
    {
        var baseScheme = TerminalUiTheme.CreateBaseScheme();
        var accentScheme = TerminalUiTheme.CreateAccentScheme();

        Assert.NotEqual(baseScheme.Normal.Background, accentScheme.Normal.Background);
        Assert.NotEqual(baseScheme.Normal, accentScheme.Normal);
    }

    [Fact]
    public void Text_selection_uses_a_distinct_high_contrast_background()
    {
        var baseScheme = TerminalUiTheme.CreateBaseScheme();
        var accentScheme = TerminalUiTheme.CreateAccentScheme();

        Assert.NotEqual(baseScheme.Active.Background, baseScheme.ReadOnly.Background);
        Assert.NotEqual(baseScheme.Active.Background, baseScheme.Normal.Background);
        Assert.NotEqual(baseScheme.Active.Foreground, baseScheme.Active.Background);
        Assert.Equal(baseScheme.Active, accentScheme.Active);
    }

    [Fact]
    public void Overview_console_is_selectable_and_exposes_command_input()
    {
        using var dashboard = new RuntimeOverviewDashboard();

        Assert.True(dashboard.ConsoleSupportsSelectionForSmoke);
        Assert.True(dashboard.CommandInputVisibleForSmoke);
        Assert.True(dashboard.CommandInputFrameUsesAccentForSmoke);
        Assert.Contains("TPS", dashboard.GetPanelTitleForSmoke("TPS"));
        Assert.Contains("Network", dashboard.GetPanelTitleForSmoke("Network"));
    }

    [Fact]
    public void Details_use_selectable_read_only_text_surface()
    {
        using var source = new BlockingOperations();
        using var workspace = new DashboardWorkspaceWindow(
            source,
            source,
            source,
            source,
            source,
            source,
            terminalDashboards: null);

        Assert.True(workspace.DetailTextSupportsSelectionForSmoke);
    }

    [Fact]
    public void Overview_maximize_hides_other_tiles_and_title_has_real_double_click_binding()
    {
        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(120, 28);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var dashboard = new RuntimeOverviewDashboard
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        window.Add(dashboard);

        SessionToken token = app.Begin(window)!;
        try
        {
            app.LayoutAndDraw();
            Assert.Equal(4, dashboard.GetVisiblePanelCountForSmoke());
            Assert.True(dashboard.HasTitleDoubleClickBindingForSmoke("Console"));
            Assert.True(dashboard.HasTitleDoubleClickBindingForSmoke("TPS"));
            Assert.True(dashboard.HasTitleDoubleClickBindingForSmoke("Network"));

            dashboard.TogglePanelForSmoke("Network");
            app.LayoutAndDraw();
            Assert.Equal(1, dashboard.GetVisiblePanelCountForSmoke());

            dashboard.TogglePanelForSmoke("Network");
            app.LayoutAndDraw();
            Assert.Equal(4, dashboard.GetVisiblePanelCountForSmoke());
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Network_graph_uses_snapshot_counter_deltas_for_inbound_and_outbound_rates()
    {
        using var dashboard = new RuntimeOverviewDashboard();
        DateTimeOffset startedAt = new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
        RuntimeNetworkSnapshot first = default(RuntimeNetworkSnapshot) with
        {
            CapturedAtUtc = startedAt,
            MessageInboundFrames = 1_000,
            MessageInboundBytes = 100L * 1024,
            MessageOutboundFrames = 2_000,
            MessageOutboundBytes = 200L * 1024
        };
        RuntimeNetworkSnapshot second = first with
        {
            CapturedAtUtc = startedAt.AddSeconds(2),
            MessageInboundFrames = 1_020,
            MessageInboundBytes = 102L * 1024,
            MessageOutboundFrames = 2_040,
            MessageOutboundBytes = 204L * 1024
        };

        dashboard.Refresh(default, first, default, default, default, default, status: null);
        dashboard.Refresh(default, second, default, default, default, default, status: null);

        string legend = dashboard.GetNetworkLegendForSmoke();
        Assert.Contains("IN 10.0p/s 1.0K", legend);
        Assert.Contains("OUT 20.0p/s 2.0K", legend);
    }

    private sealed class BlockingOperations :
        IRuntimeDashboardOperations,
        IPlayerOperations,
        INpcOperations,
        INetworkOperations,
        IWorldOperations,
        ILogOperations,
        IDisposable
    {
        private readonly ManualResetEventSlim dashboardCaptureStarted = new(false);
        private readonly ManualResetEventSlim dashboardCaptureRelease = new(false);
        private int blockDashboardCapture;
        private long dashboardTick;

        public RuntimeDashboardSnapshot CaptureSnapshot()
        {
            if (Volatile.Read(ref blockDashboardCapture) != 0)
            {
                dashboardCaptureStarted.Set();
                dashboardCaptureRelease.Wait();
            }

            long tick = Interlocked.Increment(ref dashboardTick);
            return default(RuntimeDashboardSnapshot) with { Tick = tick };
        }

        public bool TrySetInterestManagementEnabled(bool enabled) => true;

        RuntimePlayersSnapshot IPlayerOperations.CaptureSnapshot() => default;

        RuntimeNpcsSnapshot INpcOperations.CaptureSnapshot() => default;

        RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() => default;

        RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() => default;

        RuntimeLogSnapshot ILogOperations.CaptureSnapshot(RuntimeLogQuery query) => default;

        ReadOnlyMemory<string> ILogOperations.CaptureSources(int maxSources) => ReadOnlyMemory<string>.Empty;

        public void BlockDashboardCapture() => Volatile.Write(ref blockDashboardCapture, 1);

        public bool WaitUntilDashboardCaptureStarted(TimeSpan timeout, CancellationToken cancellationToken) =>
            dashboardCaptureStarted.Wait(timeout, cancellationToken);

        public void ReleaseDashboardCapture()
        {
            Volatile.Write(ref blockDashboardCapture, 0);
            dashboardCaptureRelease.Set();
        }

        public void Dispose()
        {
            dashboardCaptureRelease.Set();
            dashboardCaptureStarted.Dispose();
            dashboardCaptureRelease.Dispose();
        }
    }
}
