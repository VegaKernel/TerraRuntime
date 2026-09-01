using System.Globalization;
using System.Text;
using TerraRuntime.TerminalUI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace TerraRuntime.Tests;

public sealed class StartupProgressUiTests
{
    [Fact]
    public void Runtime_and_startup_ui_pumps_fit_inside_one_60hz_frame()
    {
        Assert.True(TerminalUiHost.UiPumpIntervalForTests <= TimeSpan.FromMilliseconds(16));
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
    public void Startup_screen_renders_into_terminal_gui_ansi_backbuffer()
    {
        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(100, 26);
        TerminalUiTheme.Apply();

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
            throw new InvalidOperationException("ANSI driver did not expose a framebuffer.");

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
            $"ANSI framebuffer did not contain '{expected}'.{Environment.NewLine}{screen}");
    }
}
