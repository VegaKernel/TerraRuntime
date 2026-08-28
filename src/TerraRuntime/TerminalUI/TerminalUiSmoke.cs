using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace TerraRuntime.TerminalUI;

internal static class TerminalUiSmoke
{
    public static int Run()
    {
        try
        {
            using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
            app.StopAfterFirstIteration = true;
            using var window = new DashboardWindow(new SmokeDashboardOperations());
            window.RefreshSnapshot();
            app.Run(window);
            Console.WriteLine("Terminal UI smoke passed: Terminal.Gui initialized and rendered the dashboard once.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Terminal UI smoke failed: {exception}");
            return 29;
        }
    }

    private sealed class SmokeDashboardOperations : IRuntimeDashboardOperations
    {
        public RuntimeDashboardSnapshot CaptureSnapshot() =>
            new(
                Lifecycle: RuntimeLifecycleState.Running,
                WorldName: "NativeAOT-Smoke",
                WorldWidthTiles: 4200,
                WorldHeightTiles: 1200,
                Port: ServerHostOptions.DefaultPort,
                MaxPlayers: ServerHostOptions.DefaultMaxPlayers,
                InterestManagementEnabled: false,
                Tick: 120,
                TargetTicksPerSecond: 60,
                ObservedTicksPerSecond: 60d,
                LastTickMilliseconds: 0.25d,
                WorstTickMilliseconds: 1.5d,
                CpuTimeAvailable: true,
                LastTickCpuMilliseconds: 0.20d,
                WorstTickCpuMilliseconds: 1.2d,
                SlowestPhase: "Update",
                SlowestPhaseMilliseconds: 0.15d,
                MissedTickDeadlines: 0,
                CommandsProcessed: 2,
                PendingCommands: 0,
                DeferredCommands: 0,
                RejectedCommands: 0,
                CommandBudgetExhaustions: 0,
                OldestPendingCommandAgeMilliseconds: 0d,
                ActiveConnections: 1,
                AcceptedConnections: 1,
                RejectedConnections: 0,
                CapturedAtUtc: DateTimeOffset.UtcNow);
    }
}
