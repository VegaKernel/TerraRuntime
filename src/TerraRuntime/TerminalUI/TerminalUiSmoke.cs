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
            var logs = new RuntimeLogBuffer(capacity: 16);
            logs.Publish(RuntimeLogLevel.Information, "Server", "Terminal UI smoke startup");
            logs.Publish(RuntimeLogLevel.Warning, "Network", "Synthetic bounded log warning");
            using var window = new DashboardWindow(operations, operations, operations, operations, logs);
            window.RefreshSnapshot();
            window.ShowPlayers();
            window.ShowNetwork();
            window.ShowWorld();
            window.ShowLogs();
            window.ShowDashboard();
            app.Run(window);
            Console.WriteLine("Terminal UI smoke passed: Terminal.Gui initialized and rendered dashboard, players, network, world and logs views.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Terminal UI smoke failed: {exception}");
            return 29;
        }
    }

    private sealed class SmokeOperations : IRuntimeDashboardOperations, IPlayerOperations, INetworkOperations, IWorldOperations
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
                ManagedHeapBytes: 32L * 1024 * 1024,
                TotalAllocatedBytes: 96L * 1024 * 1024,
                Gen0Collections: 3,
                Gen1Collections: 1,
                Gen2Collections: 0,
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
                TrackedOutboundQueues: 1,
                QueuedOutboundFrames: 2,
                QueuedOutboundBytes: 128,
                RejectedOutboundFrames: 0,
                SlowClients: 0,
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

        RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() =>
            new(
                Ready: true,
                Name: "NativeAOT-Smoke",
                WorldId: 42,
                UniqueId: new Guid("00112233-4455-6677-8899-aabbccddeeff"),
                FormatVersion: 326,
                WorldGeneratorVersion: 1,
                WidthTiles: 4200,
                HeightTiles: 1200,
                TileCount: 5_040_000,
                ChestCount: 12,
                SignCount: 4,
                TownNpcCount: 3,
                PersistentNpcCount: 1,
                TileEntityCount: 5,
                PressurePlateCount: 2,
                TownRoomCount: 3,
                RuntimeCacheHit: true,
                InitialCacheResult: "Loaded",
                CacheParallelReads: 4,
                FileReadMilliseconds: 1.2,
                CacheLoadMilliseconds: 3.4,
                CanonicalWorldLoadMilliseconds: 0,
                CacheWriteMilliseconds: 0,
                BootstrapMilliseconds: 2.1,
                WorldReadyMilliseconds: 4.8,
                NetworkReadyMilliseconds: 6.2,
                CapturedAtUtc: DateTimeOffset.UtcNow);
    }
}
