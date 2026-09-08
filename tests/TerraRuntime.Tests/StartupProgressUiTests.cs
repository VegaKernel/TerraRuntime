using System.Globalization;
using System.Text;
using TerraRuntime.Application.Diagnostics;
using TerraRuntime.Application.Operations;
using TerraRuntime.Application.TerminalUI;
using TerraRuntime.Contracts.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace TerraRuntime.Tests;

[CollectionDefinition("Startup terminal ownership", DisableParallelization = true)]
public sealed class StartupTerminalOwnershipCollection;

[Collection("Startup terminal ownership")]
public sealed class StartupProgressUiTests
{
    private const string LiquidFailure =
        "Post-load liquid preparation failed: result=UnsupportedLiquidDeathTile, x=174, y=1944, tile=12.";

    [Fact]
    public async Task Buffered_startup_error_is_replayed_once_after_terminal_release_with_its_real_cause()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        bool? ownedWhenReported = null;
        using var host = CreateHost(message =>
        {
            ownedWhenReported = StartupProgressTelemetry.IsTerminalOwned;
            standardError.WriteLine(message);
        });
        StartupProgressTelemetry.Attach(host);
        var log = new RuntimeHostLog(new RuntimeLogBuffer(8), standardOutput, standardError,
            new RuntimeHostLoggingOptions { JsonLinesEnabled = false });
        try
        {
            log.Log(OperationsLogLevel.Error, RuntimeLogEventIds.WorldLoadFailed,
                RuntimeLogCategory.World, "World", LiquidFailure, useStandardError: true);
            Assert.True(host.Snapshot.Failed);
            Assert.Equal(LiquidFailure, host.Snapshot.Detail);
            // A subsequent progress event must not erase the terminal failure's retained cause.
            log.Log(OperationsLogLevel.Information, RuntimeLogEventIds.WorldCacheMiss,
                RuntimeLogCategory.World, "World", "cache miss");
            await log.DisposeAsync();
            Assert.Equal(string.Empty, standardError.ToString());

            host.ReportServerExit(26);
            host.ReportServerExit(26);
            Assert.False(ownedWhenReported);
            Assert.False(host.OwnsTerminal);
            Assert.Equal($"Server startup stopped with exit code 26: {LiquidFailure}{Environment.NewLine}",
                standardError.ToString());
        }
        finally
        {
            await log.DisposeAsync();
        }
    }

    [Fact]
    public async Task Startup_without_terminal_keeps_single_existing_console_error()
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        Assert.False(StartupProgressTelemetry.IsTerminalOwned);
        var log = new RuntimeHostLog(new RuntimeLogBuffer(8), standardOutput, standardError,
            new RuntimeHostLoggingOptions { JsonLinesEnabled = false });
        log.Log(OperationsLogLevel.Error, RuntimeLogEventIds.WorldLoadFailed,
            RuntimeLogCategory.World, "World", LiquidFailure, useStandardError: true);
        await log.DisposeAsync();
        Assert.Equal(LiquidFailure + Environment.NewLine, standardError.ToString());
    }

    [Fact]
    public void Recovered_startup_does_not_replay_resolved_error_on_later_server_exit()
    {
        var output = new List<string>();
        using var host = CreateHost(output.Add);
        StartupProgressTelemetry.Attach(host);
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Error, RuntimeLogEventIds.WorldLoadFailed, LiquidFailure);
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Information, RuntimeLogEventIds.WorldCheckpointRecovered,
            "Checkpoint restored");
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Information, RuntimeLogEventIds.NetworkListenerReady,
            "Listening");
        host.ReportServerExit(26);
        Assert.Empty(output);
    }

    [Fact]
    public void Successful_exit_does_not_report_transient_startup_error()
    {
        var output = new List<string>();
        using var host = CreateHost(output.Add);
        StartupProgressTelemetry.Attach(host);
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Error, RuntimeLogEventIds.WorldLoadFailed, LiquidFailure);
        host.ReportServerExit(0);
        Assert.Empty(output);
    }

    [Fact]
    public void Last_error_is_bounded_sanitized_and_not_replaced_by_warning()
    {
        var output = new List<string>();
        using var host = CreateHost(output.Add);
        StartupProgressTelemetry.Attach(host);
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Error, RuntimeLogEventIds.WorldLoadFailed, LiquidFailure);
        string finalError = "final\n\u001b" + new string('x', 1200);
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Critical, RuntimeLogEventIds.LifecycleError, finalError);
        StartupProgressTelemetry.Observe(RuntimeLogLevel.Warning, RuntimeLogEventIds.WorldCacheMiss, "warning");
        host.ReportServerExit(25);
        string expected = "final  " + new string('x', 1017);
        Assert.Equal($"Server startup stopped with exit code 25: {expected}", Assert.Single(output));
    }

    [Fact]
    public void Failed_exit_without_diagnostic_still_reports_exit_code_after_release()
    {
        var output = new List<string>();
        using var host = CreateHost(output.Add);
        StartupProgressTelemetry.Attach(host);
        host.ReportServerExit(25);
        Assert.Equal("Server startup stopped with exit code 25.", Assert.Single(output));
        Assert.False(host.OwnsTerminal);
    }

    private static StartupProgressUiHost CreateHost(Action<string> report) =>
        new(StartupProgressOperation.ServerStartup, "Startup regression", "Preparing runtime", "Validating world", report);

    [Fact]
    public void Runtime_and_startup_ui_pumps_fit_inside_one_60hz_frame()
    {
        Assert.True(Host.UiPumpIntervalForTests <= TimeSpan.FromMilliseconds(16));
        Assert.True(Host.SnapshotRefreshIntervalForTests <= TimeSpan.FromMilliseconds(100));
        Assert.True(StartupProgressUiHost.PumpIntervalForTests <= TimeSpan.FromMilliseconds(16));
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, 0d)]
    [InlineData(0.67d, 67d)]
    [InlineData(1d, 100d)]
    [InlineData(2d, 100d)]
    public void Progress_bar_clamps_fraction_and_preserves_fixed_width(double fraction, double expectedPercent)
    {
        string rendered = StartupProgressWindow.RenderProgressBar(fraction, 20);
        Assert.StartsWith("[", rendered, StringComparison.Ordinal);
        Assert.Contains(expectedPercent.ToString("F1", CultureInfo.InvariantCulture) + "%", rendered);

        int close = rendered.IndexOf(']');
        Assert.Equal(21, close);
    }

    [Fact]
    public void Startup_screen_renders_into_terminal_gui_headless_backbuffer()
    {
        using IApplication app = Terminal.Gui.App.Application.Create().Init(DriverRegistry.Names.DOTNET);
        app.Driver!.SetScreenSize(100, 26);
        Theme.Apply();

        using var window = new StartupProgressWindow();
        SessionToken token = app.Begin(window)!;
        try
        {
            var snapshot = new StartupProgressSnapshot(
                StartupProgressOperation.ServerStartup,
                "Designer-Smoke",
                "Preparing persistence",
                "Canonical save template is ready",
                StageIndex: 5,
                StageCount: 8,
                Fraction: 0.67d,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            window.Refresh(snapshot, TimeSpan.FromSeconds(7), animationFrame: 1);
            app.LayoutAndDraw();

            Assert.NotNull(app.Driver.Contents);
            AssertRendered(app.Driver, "TERRARUNTIME");
            AssertRendered(app.Driver, "SERVER RUNTIME · STARTUP");
            AssertRendered(app.Driver, "Designer-Smoke");
            AssertRendered(app.Driver, "Preparing persistence");
            AssertRendered(app.Driver, "67.0%");
            AssertRendered(app.Driver, "step 5/8");
            AssertRendered(app.Driver, "Terminal.Gui framebuffer");
        }
        finally
        {
            app.End(token);
        }
    }

    private static void AssertRendered(IDriver driver, string expected)
    {
        if (driver.Contents is null)
            throw new InvalidOperationException("Terminal.Gui driver did not expose a framebuffer.");

        int height = driver.Contents.GetLength(0);
        int width = driver.Contents.GetLength(1);
        var screen = new StringBuilder(width * height);
        for (int row = 0; row < height; row++)
        {
            var line = new StringBuilder(width);
            for (int column = 0; column < width; column++)
                line.Append(driver.Contents[row, column]!.Grapheme);

            string renderedRow = line.ToString();
            if (renderedRow.Contains(expected, StringComparison.Ordinal))
                return;
            screen.AppendLine(renderedRow.TrimEnd());
        }

        throw new InvalidOperationException(
            $"Terminal.Gui framebuffer did not contain '{expected}'.{Environment.NewLine}{screen}");
    }
}
