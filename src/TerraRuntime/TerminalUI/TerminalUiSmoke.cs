using System.Globalization;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Operations;
using TerraRuntime.World;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

internal static class TerminalUiSmoke
{
    public static int Run()
    {
        var operations = new SmokeOperations();
        var dashboardRegistry = new SmokeDashboardSource();
        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var window = new RuntimeTerminalWindow(operations, dashboardRegistry, requestStop: static () => { });
        try
        {
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.Players);
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.Npcs);
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.Projectiles);
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.World);
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.Network);
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.Commands);
            window.Refresh();
            window.SelectTab(RuntimeTerminalTab.Dashboard);
            window.Refresh();
            Console.WriteLine("TerraRuntime terminal UI smoke passed.");
            return 0;
        }
        finally
        {
            window.Dispose();
            dashboardRegistry.Dispose();
        }
    }

    private sealed class SmokeDashboardSource : ITerraRuntimeTerminalDashboardSource, IDisposable
    {
        private readonly ITerraRuntimeTerminalDashboardProvider[] providers = [new SmokeDashboardProvider()];

        public ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> CaptureDashboards() => providers;

        public void Dispose()
        {
            foreach (ITerraRuntimeTerminalDashboardProvider provider in providers)
            {
                if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private sealed class SmokeDashboardProvider : ITerraRuntimeTerminalDashboardProvider
    {
        public string Id => "smoke.dashboard";
        public string Title => "Smoke Dashboard";

        public View CreateDashboard() =>
            new Label
            {
                Text = "Smoke dashboard content"
            };

        public void Refresh(View rootView)
        {
            ArgumentNullException.ThrowIfNull(rootView);
        }
    }

    private sealed class SmokeOperations :
        IPlayerOperations,
        INpcOperations,
        IProjectileOperations,
        IWorldOperations,
        INetworkOperations,
        ICommandOperations
    {
        private bool interestManagementEnabled;

        public RuntimeServerSummary CaptureServerSummary() =>
            new(
                WorldName: "Smoke World",
                WidthTiles: 8400,
                HeightTiles: 2400,
                Port: 7777,
                MaxPlayers: 8,
                InterestManagementEnabled: interestManagementEnabled,
                ConnectedPlayers: 1,
                ActiveNpcs: 2,
                ActiveProjectiles: 3,
                Tick: 1234,
                StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-15));

        RuntimePlayerSnapshot IPlayerOperations.CaptureSnapshot() =>
            new(
                ActivePlayers:
                [
                    new RuntimePlayerInfo(
                        Slot: 0,
                        Name: "SmokePlayer",
                        PositionX: 120f,
                        PositionY: 240f,
                        VelocityX: 1f,
                        VelocityY: 0f,
                        Life: 100,
                        LifeMax: 100,
                        Mana: 20,
                        ManaMax: 20,
                        SelectedItem: 0,
                        Team: 0,
                        Hostile: false,
                        Dead: false,
                        Difficulty: 0,
                        ConnectedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5))
                ].AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);

        bool IPlayerOperations.TryKick(int slot, string reason) => slot == 0;

        RuntimeNpcSnapshot INpcOperations.CaptureSnapshot() =>
            new(
                ActiveNpcs:
                [
                    new RuntimeNpcInfo(
                        Slot: 1,
                        Type: 1,
                        NetId: 1,
                        Life: 50,
                        LifeMax: 50,
                        PositionX: 100f,
                        PositionY: 200f,
                        VelocityX: 0f,
                        VelocityY: 0f,
                        Target: 0,
                        Active: true)
                ].AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);

        bool INpcOperations.TryRemove(int slot) => slot == 1;

        RuntimeProjectileSnapshot IProjectileOperations.CaptureSnapshot() =>
            new(
                ActiveProjectiles:
                [
                    new RuntimeProjectileInfo(
                        Slot: 1,
                        Type: 1,
                        Owner: 0,
                        Identity: 5,
                        PositionX: 300f,
                        PositionY: 400f,
                        VelocityX: 1f,
                        VelocityY: -1f,
                        TimeLeft: 60,
                        Active: true)
                ].AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);

        bool IProjectileOperations.TryRemove(int slot) => slot == 1;

        RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() =>
            new(
                Name: "Smoke World",
                WidthTiles: 8400,
                HeightTiles: 2400,
                SpawnX: 4200,
                SpawnY: 300,
                DungeonX: 500,
                DungeonY: 400,
                WorldSurface: 300,
                RockLayer: 600,
                GameMode: 0,
                IsCrimson: false,
                IsHardMode: false,
                DayTime: true,
                Time: 13500d,
                MoonPhase: 0,
                BloodMoon: false,
                Eclipse: false,
                Raining: false,
                RainTime: 0,
                MaxRain: 0f,
                WindSpeed: 0f,
                MaxWind: 0f,
                CloudCount: 0,
                CapturedAtUtc: DateTimeOffset.UtcNow);

        RuntimeWorldTileSnapshot IWorldOperations.CaptureTileSnapshot(int centerX, int centerY, int radius) =>
            new(
                CenterX: centerX,
                CenterY: centerY,
                Radius: radius,
                Width: 1,
                Height: 1,
                Tiles:
                [
                    new RuntimeWorldTileInfo(
                        X: centerX,
                        Y: centerY,
                        Type: 0,
                        Wall: 0,
                        IsActive: true,
                        LiquidAmount: 0,
                        LiquidKind: 0)
                ].AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);

        bool IWorldOperations.TrySetInterestManagement(bool enabled)
        {
            interestManagementEnabled = enabled;
            return true;
        }

        RuntimeWorldChestsSnapshot IWorldOperations.CaptureChestSnapshot()
        {
            RuntimeWorldChestInfo[] chests =
            [
                new RuntimeWorldChestInfo(
                    ChestId: 0,
                    X: 100,
                    Y: 200,
                    Name: "Smoke Chest",
                    ItemCount: 2,
                    TotalStack: 12,
                    OccupiedSlots: 2)
            ];
            return new RuntimeWorldChestsSnapshot(
                ActiveChests: 1,
                Chests: chests.AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeWorldSignsSnapshot IWorldOperations.CaptureSignSnapshot()
        {
            RuntimeWorldSignInfo[] signs =
            [
                new RuntimeWorldSignInfo(
                    SignId: 0,
                    X: 120,
                    Y: 210,
                    Text: "Smoke sign")
            ];
            return new RuntimeWorldSignsSnapshot(
                ActiveSigns: 1,
                Signs: signs.AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeWorldTileEntitiesSnapshot IWorldOperations.CaptureTileEntitySnapshot()
        {
            RuntimeWorldTileEntityInfo[] tileEntities =
            [
                new RuntimeWorldTileEntityInfo(
                    Id: 1,
                    X: 150,
                    Y: 250,
                    Type: 0)
            ];
            return new RuntimeWorldTileEntitiesSnapshot(
                ActiveTileEntities: 1,
                TileEntities: tileEntities.AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeWorldTownRoomsSnapshot IWorldOperations.CaptureTownRoomSnapshot()
        {
            RuntimeWorldTownRoomInfo[] rooms =
            [
                new RuntimeWorldTownRoomInfo(
                    NpcType: 17,
                    HomeX: 200,
                    HomeY: 300)
            ];
            return new RuntimeWorldTownRoomsSnapshot(
                ActiveRooms: 1,
                Rooms: rooms.AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeWorldItemsSnapshot IWorldOperations.CaptureWorldItemSnapshot()
        {
            RuntimeWorldItemGroupSnapshot[] groups =
            [
                new RuntimeWorldItemGroupSnapshot(
                    ItemNetId: 71,
                    DropCount: 4,
                    TotalStack: 400,
                    ReservedDrops: 1,
                    ShimmeredDrops: 1,
                    MaxStack: 100,
                    AveragePositionX: 1920f,
                    AveragePositionY: 2880f),
                new RuntimeWorldItemGroupSnapshot(
                    ItemNetId: 1,
                    DropCount: 2,
                    TotalStack: 2,
                    ReservedDrops: 0,
                    ShimmeredDrops: 0,
                    MaxStack: 1,
                    AveragePositionX: 640f,
                    AveragePositionY: 960f)
            ];
            return new RuntimeWorldItemsSnapshot(
                ActiveItems: 6,
                Groups: groups.AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() =>
            new(
                ActiveConnections: 1,
                RegisteredConnections: 1,
                AcceptedConnections: 6,
                RejectedConnections: 5,
                TrackedOutboundQueues: 1,
                QueuedOutboundFrames: 2,
                QueuedOutboundBytes: 128,
                PeakQueuedOutboundFrames: 5,
                PeakQueuedOutboundBytes: 512,
                RejectedOutboundFrames: 1,
                SlowClients: 1,
                TopOutboundQueues: new RuntimeConnectionQueueDetail[]
                {
                    new(
                        ConnectionId: 1,
                        MaxFrames: 256,
                        MaxQueuedBytes: 1_048_576,
                        QueuedFrames: 2,
                        QueuedBytes: 128,
                        PeakQueuedFrames: 5,
                        PeakQueuedBytes: 512,
                        RejectedFrames: 1,
                        SlowClient: true)
                }.AsMemory(),
                TrackedInboundRates: 1,
                InboundWindowFrames: 12,
                InboundWindowBytes: 2048,
                InboundTotalFrames: 120,
                InboundTotalBytes: 16384,
                RejectedInboundFrames: 1,
                TopInboundRates: new RuntimeConnectionRateDetail[]
                {
                    new(1, 12, 2048, 120, 16384, 1)
                }.AsMemory(),
                RelayedAppearanceFrames: 1,
                AppearanceBaselineFrames: 1,
                RelayedEquipmentFrames: 1,
                EquipmentBaselineFrames: 1,
                DroppedEquipmentSnapshotUpdates: 0,
                PlayerActiveBaselineFrames: 1,
                PlayerDeactivationFrames: 0,
                RelayedMovementFrames: 10,
                MovementResyncFrames: 1,
                CapturedAtUtc: DateTimeOffset.UtcNow,
                NpcRelayedFrames: 8,
                NpcBaselineFrames: 2,
                NpcRejectedFrames: 1,
                NpcUnsupportedCommits: 1,
                ProjectileRelayedFrames: 15,
                ProjectileBaselineFrames: 4,
                ProjectileRejectedFrames: 2,
                ProjectileUnsupportedCommits: 1,
                PlayerStateResyncFrames: 1,
                PlayerStateRejectedFrames: 1,
                PlayerStateUnsupportedCommits: 1,
                WorldTimeRelayedFrames: 1,
                WorldTimeBaselineFrames: 1,
                WorldTimeRejectedFrames: 0,
                WorldTimeUnsupportedCommits: 0,
                ChestRelayedFrames: 3,
                ChestBaselineFrames: 1,
                ChestRejectedFrames: 1,
                ChestUnsupportedCommits: 0,
                RejectionTelemetry: new TerrariaFrameRejectionTelemetrySnapshot(
                    MalformedProtocol: 1,
                    RateLimited: 2,
                    InvalidState: 3,
                    GameplayRejected: 4,
                    Backpressure: 5));

        public CommandCatalogSnapshot CaptureCommandCatalog() =>
            new(
                Commands:
                [
                    new CommandDescriptor(
                        Name: "save",
                        Description: "Save the world",
                        Usage: "save",
                        RequiredPermission: "terraruntime.world.save")
                ].AsMemory(),
                CapturedAtUtc: DateTimeOffset.UtcNow);

        public CommandExecutionResult ExecuteCommand(string commandLine) =>
            new(
                Status: CommandExecutionStatus.Executed,
                Output: $"Smoke command: {commandLine}");
    }
}
