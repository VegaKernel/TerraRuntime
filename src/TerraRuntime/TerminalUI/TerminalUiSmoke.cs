using System.Text;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

internal static class TerminalUiSmoke
{
    private const int SmokeWidth = 160;
    private const int SmokeHeight = 28;

    public static int Run()
    {
        try
        {
            var operations = new SmokeOperations();
            var logs = new RuntimeLogBuffer(capacity: 16);
            logs.Publish(RuntimeLogLevel.Information, "Server", "Terminal UI smoke startup");
            logs.Publish(RuntimeLogLevel.Warning, "Network", "Synthetic bounded log warning");
            RuntimeChatTelemetry.Publish(0, "Synthetic dashboard chat message");

            using (IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI))
            {
                app.Driver!.SetScreenSize(SmokeWidth, SmokeHeight);
                using var workspace = new DashboardWorkspaceWindow(
                    operations,
                    operations,
                    operations,
                    operations,
                    operations,
                    logs,
                    new SmokeDashboardSource(),
                    operations,
                    operations);

                SessionToken token = app.Begin(workspace)!;
                try
                {
                    workspace.RefreshSnapshot();
                    AssertWorkspaceRow(workspace, "Running");
                    app.LayoutAndDraw();
                    AssertRendered(app.Driver!, "Running");

                    // Exercise the exact production transition that regressed: an external root is visible,
                    // then every built-in Details screen must become visible through the real MenuBar path.
                    workspace.ShowExternalDashboardForSmoke(0);
                    app.LayoutAndDraw();
                    AssertRendered(app.Driver!, "EXTERNAL DASHBOARD SMOKE");

                    SelectDetailsScreen(app, workspace, Key.P, "Players", "PLAYERS");
                    SelectDetailsScreen(app, workspace, Key.N, "NPCs", "NPCS");
                    SelectDetailsScreen(app, workspace, Key.R, "Projectiles", "PROJECTILES");
                    SelectDetailsScreen(app, workspace, Key.I, "Items", "ITEMS");
                    SelectDetailsScreen(app, workspace, Key.E, "Network", "NETWORK");
                    SelectDetailsScreen(app, workspace, Key.W, "World", "WORLD");
                    AssertRendered(app.Driver!, "Sections");
                    AssertRendered(app.Driver!, "Lookups");
                    AssertRendered(app.Driver!, "Rebuild");
                    SelectDetailsScreen(app, workspace, Key.L, "Logs", "LOG");
                    SelectDetailsScreen(app, workspace, Key.O, "Overview", "Running");

                    workspace.SetInterestManagementEnabled(true);
                    app.LayoutAndDraw();
                    AssertRendered(app.Driver!, "Admin: queued interest management enable command");

                    workspace.SetInterestManagementEnabled(false);
                    app.LayoutAndDraw();
                    AssertRendered(app.Driver!, "Admin: queued interest management disable command");
                }
                finally
                {
                    app.End(token);
                }
            }

            Console.WriteLine(
                "Terminal UI smoke passed: ANSI framebuffer rendered the System Dashboard, external-dashboard transition, " +
                "all Details menu hotkeys, section-cache telemetry, Players/NPCs/Projectiles/Items/Network/World/Logs detail views and authoritative admin actions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Terminal UI smoke failed: {exception}");
            return 29;
        }
    }

    private static void SelectDetailsScreen(
        IApplication app,
        DashboardWorkspaceWindow workspace,
        Key hotKey,
        string menuText,
        string expectedRowPrefix)
    {
        app.Keyboard.RaiseKeyDownEvent(Key.E.WithAlt);
        app.LayoutAndDraw();
        AssertRendered(app.Driver!, menuText);

        app.Keyboard.RaiseKeyDownEvent(hotKey);
        AssertWorkspaceRow(workspace, expectedRowPrefix);
        app.LayoutAndDraw();
        AssertRendered(app.Driver!, expectedRowPrefix);
    }

    private static void AssertWorkspaceRow(DashboardWorkspaceWindow workspace, string expectedPrefix)
    {
        string row = workspace.GetRowTextForSmoke(0);
        if (!row.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Workspace detail screen did not populate row 0. Expected prefix '{expectedPrefix}', actual '{row}'.");
        }
    }

    private static void AssertRendered(IDriver driver, string expectedText)
    {
        if (driver.Contents is null)
            throw new InvalidOperationException("ANSI driver did not expose framebuffer contents.");

        var screen = new StringBuilder(SmokeWidth * SmokeHeight);
        for (int row = 0; row < SmokeHeight; row++)
        {
            var line = new StringBuilder(SmokeWidth);
            for (int column = 0; column < SmokeWidth; column++)
                line.Append(driver.Contents[row, column]!.Grapheme);

            string renderedRow = line.ToString();
            if (renderedRow.Contains(expectedText, StringComparison.Ordinal))
                return;

            screen.AppendLine(renderedRow.TrimEnd());
        }

        throw new InvalidOperationException(
            $"ANSI framebuffer did not contain expected text '{expectedText}'.{Environment.NewLine}{screen}");
    }

    private sealed class SmokeDashboardSource : ITerraRuntimeTerminalDashboardSource
    {
        private readonly ITerraRuntimeTerminalDashboardProvider[] dashboards = [new SmokeDashboardProvider()];

        public ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> CaptureDashboards() => dashboards;
    }

    private sealed class SmokeDashboardProvider : ITerraRuntimeTerminalDashboardProvider
    {
        public string Id => "smoke.external";

        public string Title => "Smoke External";

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
                Y = 0,
                Text = "EXTERNAL DASHBOARD SMOKE"
            });
            return root;
        }

        public void Refresh(View rootView) => rootView.SetNeedsDraw();
    }

    private sealed class SmokeOperations :
        IRuntimeDashboardOperations,
        IPlayerOperations,
        INpcOperations,
        IProjectileOperations,
        IWorldItemOperations,
        INetworkOperations,
        IWorldOperations
    {
        private bool interestManagementEnabled;

        public RuntimeDashboardSnapshot CaptureSnapshot() =>
            new(
                Lifecycle: RuntimeLifecycleState.Running,
                WorldName: "NativeAOT-Smoke",
                WorldWidthTiles: 4200,
                WorldHeightTiles: 1200,
                Port: ServerHostOptions.DefaultPort,
                MaxPlayers: ServerHostOptions.DefaultMaxPlayers,
                InterestManagementEnabled: interestManagementEnabled,
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

        public bool TrySetInterestManagementEnabled(bool enabled)
        {
            interestManagementEnabled = enabled;
            return true;
        }

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
                    VelocityX: 1.25f,
                    VelocityY: -0.5f,
                    SelectedItem: 4,
                    MountType: 2,
                    HasHealth: true,
                    Life: 100,
                    MaxLife: 100,
                    HasMana: true,
                    Mana: 20,
                    MaxMana: 20)
            ];
            return new RuntimePlayersSnapshot(players.AsMemory(), DateTimeOffset.UtcNow);
        }

        RuntimeNpcsSnapshot INpcOperations.CaptureSnapshot()
        {
            RuntimeNpcSnapshot[] npcs =
            [
                new RuntimeNpcSnapshot(
                    Slot: 1,
                    Generation: 2,
                    Revision: 7,
                    Type: 1,
                    NetId: 1,
                    PositionX: 800f,
                    PositionY: 1600f,
                    VelocityX: 1.5f,
                    VelocityY: -0.25f,
                    Target: 0,
                    Ai0: 1f,
                    Ai1: 2f,
                    Ai2: 3f,
                    Ai3: 4f,
                    DirectionX: 1,
                    DirectionY: 0,
                    CollideX: false,
                    CollideY: true,
                    Wet: false,
                    NoGravity: false,
                    NoTileCollide: false)
            ];
            return new RuntimeNpcsSnapshot(
                npcs.AsMemory(),
                CommittedSpawns: 1,
                CommittedUpdates: 6,
                CommittedDespawns: 0,
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeProjectilesSnapshot IProjectileOperations.CaptureSnapshot()
        {
            RuntimeProjectileGroupSnapshot[] groups =
            [
                new RuntimeProjectileGroupSnapshot(
                    Spawner: 0,
                    Type: 1,
                    Count: 12,
                    AveragePositionX: 1600f,
                    AveragePositionY: 3200f,
                    AverageVelocityX: 3.5f,
                    AverageVelocityY: -0.25f,
                    MaxDamage: 42,
                    MaxOriginalDamage: 42,
                    MaxKnockBack: 3f),
                new RuntimeProjectileGroupSnapshot(
                    Spawner: 7,
                    Type: 14,
                    Count: 3,
                    AveragePositionX: 2400f,
                    AveragePositionY: 1600f,
                    AverageVelocityX: -2f,
                    AverageVelocityY: 0f,
                    MaxDamage: 20,
                    MaxOriginalDamage: 20,
                    MaxKnockBack: 1f)
            ];
            return new RuntimeProjectilesSnapshot(
                ActiveProjectiles: 15,
                Groups: groups.AsMemory(),
                CommittedSpawns: 20,
                CommittedUpdates: 64,
                CommittedDespawns: 5,
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        RuntimeWorldItemsSnapshot IWorldItemOperations.CaptureSnapshot()
        {
            RuntimeWorldItemGroupSnapshot[] groups =
            [
                new RuntimeWorldItemGroupSnapshot(
                    ItemNetId: 71,
                    DropCount: 4,
                    TotalStack: 183,
                    ReservedDrops: 1,
                    ShimmeredDrops: 1,
                    MaxStack: 99,
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
                AcceptedConnections: 1,
                RejectedConnections: 0,
                TrackedOutboundQueues: 1,
                QueuedOutboundFrames: 2,
                QueuedOutboundBytes: 128,
                RejectedOutboundFrames: 1,
                SlowClients: 1,
                TopOutboundQueues: new RuntimeConnectionQueueDetail[]
                {
                    new(1, 2, 128, 1, true)
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
                WorldItemRelayedFrames: 9,
                WorldItemRejectedFrames: 1,
                WorldItemUnsupportedCommits: 1);

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
                CapturedAtUtc: DateTimeOffset.UtcNow,
                RuntimeClockAvailable: true,
                RuntimeTime: 12_345d,
                RuntimeDayTime: true,
                RuntimeMoonPhase: 2,
                RuntimeSlimeRainTime: 300d,
                RuntimeDayRate: 1,
                SectionCacheAvailable: true,
                SectionCacheDirtyBacklog: 3,
                SectionCacheEntries: 12,
                SectionCacheMaximumEntries: 64,
                SectionCacheBytes: 2L * 1024 * 1024,
                SectionCachePublished: 9,
                SectionCacheActiveWorkers: 2,
                SectionCachePendingWork: 4,
                SectionCacheHits: 100,
                SectionCacheMisses: 8,
                SectionCacheStaleReads: 2,
                SectionCacheWaits: 3);
    }
}
