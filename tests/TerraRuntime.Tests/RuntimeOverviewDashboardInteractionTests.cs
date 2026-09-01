using TerraRuntime.Operations;
using TerraRuntime.TerminalUI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Tests;

public sealed class RuntimeOverviewDashboardInteractionTests
{
    [Fact]
    public void Dashboard_initialization_repairs_focus_chain_and_focuses_command_input()
    {
        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(80, 20);
        using var window = new Window
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var host = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        var dashboard = new RuntimeOverviewDashboard
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        host.Add(dashboard);
        window.Add(host);

        SessionToken token = app.Begin(window)!;
        try
        {
            app.LayoutAndDraw();

            Assert.True(host.CanFocus);
            Assert.True(dashboard.DashboardCanFocusForSmoke);
            Assert.True(dashboard.CommandInputHasFocusForSmoke);
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Console_and_chat_follow_tail_but_preserve_manual_history_scroll()
    {
        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(80, 20);
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
            RuntimeLogSnapshot logs = CreateLogs(32, "Runtime");
            RuntimeLogSnapshot chat = CreateLogs(24, "Chat");
            RuntimeDashboardSnapshot first = default(RuntimeDashboardSnapshot) with
            {
                Tick = 100,
                TargetTicksPerSecond = 60,
                ObservedTicksPerSecond = 60d,
                SlowestPhase = "Update"
            };

            dashboard.Refresh(first, default, default, default, logs, chat, status: null);
            app.LayoutAndDraw();

            Assert.True(dashboard.ConsoleLinesForSmoke > 1);
            Assert.True(dashboard.ChatLinesForSmoke > 1);
            Assert.True(dashboard.ConsoleViewportYForSmoke > 0);
            Assert.True(dashboard.ChatViewportYForSmoke > 0);

            dashboard.ScrollConsoleToTopForSmoke();
            Assert.Equal(0, dashboard.ConsoleViewportYForSmoke);

            RuntimeDashboardSnapshot second = first with { Tick = 101 };
            dashboard.Refresh(second, default, default, default, logs, chat, status: null);
            app.LayoutAndDraw();

            Assert.Equal(0, dashboard.ConsoleViewportYForSmoke);
        }
        finally
        {
            app.End(token);
        }
    }

    private static RuntimeLogSnapshot CreateLogs(int count, string source)
    {
        DateTimeOffset startedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        RuntimeLogEntry[] entries = Enumerable.Range(1, count)
            .Select(index => new RuntimeLogEntry(
                index,
                startedAt.AddSeconds(index),
                RuntimeLogLevel.Information,
                source,
                $"message-{index:D3}"))
            .ToArray();

        return new RuntimeLogSnapshot(
            entries.AsMemory(),
            PublishedEntries: count,
            OverwrittenEntries: 0,
            MinimumLevel: RuntimeLogLevel.Information,
            CapturedAtUtc: startedAt.AddSeconds(count));
    }
}
