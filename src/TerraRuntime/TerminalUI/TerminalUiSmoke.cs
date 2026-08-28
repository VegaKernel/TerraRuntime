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
            var operations = new SmokeOperations();
            using var window = new DashboardWindow(operations, operations, operations);
            window.RefreshSnapshot();
            window.ShowPlayers();
            window.ShowNetwork();
            window.ShowDashboard();
            app.Run(window);
            Console.WriteLine("Terminal UI smoke passed: Terminal.Gui initialized and rendered dashboard, players and network views.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Terminal UI smoke failed: {exception}");
            return 29;
        }
    }

    private sealed class SmokeOperations : IRuntimeDashboardOperations, IPlayerOperations, INetworkOperations
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

        RuntimePlayersSnapshot IPlayerOperations.CaptureSnapshot()
        {
            RuntimePlayerSnapshot[] players =
            [
                new RuntimePlayerSnapshot(
                    ConnectionId: 1,
                    Slot: 0,
                    Generation: 1,
                    Name: "SmokePlayer",
                    Team: 0,
                    PositionX: 1600f,
                    PositionY: 3200f,
                    HasHealth: true,
                    Life: 100,
                    MaxLife: 100,
                    HasMana: true,
                    Mana: 20,
                    MaxMana: 20)
            ];
            return new RuntimePlayersSnapshot(players.AsMemory(), DateTimeOffset.UtcNow);
        }

        RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() =>
            new(
                ActiveConnections: 1,
                RegisteredConnections: 1,
                AcceptedConnections: 1,
                RejectedConnections: 0,
                RelayedAppearanceFrames: 1,
                AppearanceBaselineFrames: 1,
                RelayedEquipmentFrames: 1,
                EquipmentBaselineFrames: 1,
                DroppedEquipmentSnapshotUpdates: 0,
                PlayerActiveBaselineFrames: 1,
                PlayerDeactivationFrames: 0,
                RelayedMovementFrames: 10,
                MovementResyncFrames: 1,
                CapturedAtUtc: DateTimeOffset.UtcNow);
    }
}
