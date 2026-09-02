using System.Reflection;
using System.Text;
using TerraRuntime.Operations;
using TerraRuntime.TerminalUI;
using TerraRuntime.World;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace TerraRuntime.Tests;

public sealed class ManualWorldCheckpointOperationsTests
{
    private const int SmokeWidth = 160;
    private const int SmokeHeight = 28;

    [Fact]
    public async Task Save_service_try_request_is_non_throwing_and_rejects_after_completion()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-manual-save-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);
        var service = new RuntimeWorldCheckpointCoordinator(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            new RuntimeChestStore(source.Chests),
            synchronizationSectionsPerTick: 1);

        try
        {
            Assert.True(service.TryRequestSave());
            Assert.True(service.IsSaveRequested);

            await service.CompleteAsync(TestContext.Current.CancellationToken);

            Assert.False(service.TryRequestSave());
            Assert.False(service.CaptureStatus().AcceptingRequests);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void World_operations_forward_manual_save_request_only_through_supplied_ingress()
    {
        int requestCount = 0;
        var operations = new LocalRuntimeWorldOperations(
            CreateWorldSnapshot(),
            persistenceSaveRequest: () =>
            {
                requestCount++;
                return requestCount == 1;
            });

        Assert.True(operations.TryRequestSave());
        Assert.False(operations.TryRequestSave());
        Assert.Equal(2, requestCount);

        var readOnlyOperations = new LocalRuntimeWorldOperations(CreateWorldSnapshot());
        Assert.False(readOnlyOperations.TryRequestSave());
    }

    [Fact]
    public void Actions_menu_manual_save_uses_world_operations_and_renders_pending_status()
    {
        var operations = new ManualSaveOperations();
        var logs = new RuntimeLogBuffer(capacity: 4);

        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(SmokeWidth, SmokeHeight);
        using var workspace = new DashboardWorkspaceWindow(
            operations,
            operations,
            operations,
            operations,
            operations,
            logs,
            terminalDashboards: null);

        SessionToken token = app.Begin(workspace)!;
        try
        {
            app.Keyboard.RaiseKeyDownEvent(Key.A.WithAlt);
            app.LayoutAndDraw();
            AssertRendered(app.Driver!, "Save world checkpoint");

            app.Keyboard.RaiseKeyDownEvent(Key.S);
            Assert.Equal(1, operations.SaveRequests);
            Assert.StartsWith("WORLD", workspace.GetRowTextForSmoke(0), StringComparison.Ordinal);

            app.LayoutAndDraw();
            AssertRendered(app.Driver!, "WORLD");
            AssertRendered(app.Driver!, "Save        shadow ready");
            AssertRendered(app.Driver!, "request pending");
            AssertRendered(app.Driver!, "Admin: queued world save checkpoint");
        }
        finally
        {
            app.End(token);
        }
    }

    [Fact]
    public void Actions_menu_manual_save_reports_rejected_ingress()
    {
        var operations = new ManualSaveOperations(acceptSave: false);
        var logs = new RuntimeLogBuffer(capacity: 4);

        using IApplication app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(SmokeWidth, SmokeHeight);
        using var workspace = new DashboardWorkspaceWindow(
            operations,
            operations,
            operations,
            operations,
            operations,
            logs,
            terminalDashboards: null);

        SessionToken token = app.Begin(workspace)!;
        try
        {
            app.Keyboard.RaiseKeyDownEvent(Key.A.WithAlt);
            app.LayoutAndDraw();
            AssertRendered(app.Driver!, "Save world checkpoint");

            app.Keyboard.RaiseKeyDownEvent(Key.S);
            Assert.Equal(1, operations.SaveRequests);
            Assert.StartsWith("WORLD", workspace.GetRowTextForSmoke(0), StringComparison.Ordinal);

            app.LayoutAndDraw();
            AssertRendered(app.Driver!, "request idle");
            AssertRendered(app.Driver!, "Admin: rejected world save checkpoint");
        }
        finally
        {
            app.End(token);
        }
    }

    private static RuntimeWorldSnapshot CreateWorldSnapshot(bool saveRequested = false) =>
        new(
            Ready: true,
            Name: "Manual-Save-Test",
            WorldId: 7,
            UniqueId: new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            FormatVersion: 326,
            WorldGeneratorVersion: 1,
            WidthTiles: 100,
            HeightTiles: 100,
            TileCount: 10_000,
            ChestCount: 0,
            SignCount: 0,
            TownNpcCount: 0,
            PersistentNpcCount: 0,
            TileEntityCount: 0,
            PressurePlateCount: 0,
            TownRoomCount: 0,
            RuntimeCacheHit: true,
            InitialCacheResult: "Loaded",
            CacheParallelReads: 1,
            FileReadMilliseconds: 0,
            CacheLoadMilliseconds: 0,
            CanonicalWorldLoadMilliseconds: 0,
            CacheWriteMilliseconds: 0,
            BootstrapMilliseconds: 0,
            WorldReadyMilliseconds: 0,
            NetworkReadyMilliseconds: 0,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Persistence: new RuntimeWorldPersistenceSnapshot(
                AcceptingRequests: true,
                TileShadowReady: true,
                RemainingBootstrapSections: 0,
                PendingDirtyTileSections: 0,
                SaveRequested: saveRequested,
                WriteActive: false,
                PendingWrite: false,
                AcceptedSnapshots: 0,
                StartedWrites: 0,
                CompletedWrites: 0,
                CoalescedSnapshots: 0,
                FailedWrites: 0));

    private static void AssertRendered(IDriver driver, string expectedText)
    {
        Assert.NotNull(driver.Contents);
        var screen = new StringBuilder(SmokeWidth * SmokeHeight);
        for (int row = 0; row < SmokeHeight; row++)
        {
            var line = new StringBuilder(SmokeWidth);
            for (int column = 0; column < SmokeWidth; column++)
                line.Append(driver.Contents![row, column]!.Grapheme);

            string renderedRow = line.ToString();
            if (renderedRow.Contains(expectedText, StringComparison.Ordinal))
                return;

            screen.AppendLine(renderedRow.TrimEnd());
        }

        throw new InvalidOperationException(
            $"ANSI framebuffer did not contain expected text '{expectedText}'.{Environment.NewLine}{screen}");
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }

    private sealed class ManualSaveOperations :
        IRuntimeDashboardOperations,
        IPlayerOperations,
        INpcOperations,
        INetworkOperations,
        IWorldOperations
    {
        private readonly bool acceptSave;
        private bool saveRequested;

        public ManualSaveOperations(bool acceptSave = true)
        {
            this.acceptSave = acceptSave;
        }

        public int SaveRequests { get; private set; }

        RuntimeDashboardSnapshot IRuntimeDashboardOperations.CaptureSnapshot() => default;

        public bool TrySetInterestManagementEnabled(bool enabled) => true;

        RuntimePlayersSnapshot IPlayerOperations.CaptureSnapshot() => default;

        RuntimeNpcsSnapshot INpcOperations.CaptureSnapshot() => default;

        RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() => default;

        RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() => CreateWorldSnapshot(saveRequested);

        public bool TryRequestSave()
        {
            SaveRequests++;
            if (!acceptSave)
                return false;

            saveRequested = true;
            return true;
        }
    }
}
