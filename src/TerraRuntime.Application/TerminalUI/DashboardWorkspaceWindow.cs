using System.Collections.ObjectModel;
using System.Globalization;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.Application.Operations;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

#pragma warning disable CS0618 // Terminal.Gui TextView remains the built-in selectable read-only surface in 2.4.17.

namespace TerraRuntime.Application.TerminalUI;

/// <summary>
/// Operator-facing terminal workspace. Runtime-owned detail screens consume bounded snapshots and render their
/// complete bounded contents into one scrollable/selectable surface instead of truncating them to viewport-sized
/// label arrays. Filtering is UI-local and never changes runtime capture semantics or authoritative state.
/// </summary>
internal sealed class DashboardWorkspaceWindow : Runnable
{
    private const int SmokeRowCount = 18;
    private const int MaximumExternalHotkeys = 10;
    private const int MaximumLogEntries = 256;

    private static readonly Key[] ExternalDashboardKeys =
    [
        Key.F3,
        Key.F4,
        Key.F5,
        Key.F6,
        Key.F7,
        Key.F8,
        Key.F9,
        Key.F10,
        Key.F11,
        Key.F12
    ];

    private readonly IRuntimeDashboardOperations dashboardOperations;
    private readonly IPlayerOperations playerOperations;
    private readonly INpcOperations npcOperations;
    private readonly IProjectileOperations? projectileOperations;
    private readonly IWorldItemOperations? worldItemOperations;
    private readonly INetworkOperations networkOperations;
    private readonly IWorldOperations worldOperations;
    private readonly ILogOperations logOperations;
    private readonly IRuntimeWorldInspectionOperations? worldInspectionOperations;
    private readonly View workspace;
    private readonly View systemRoot;
    private readonly RuntimeOverviewDashboard overviewDashboard;
    private readonly Label detailHeader;
    private readonly Label worldSelectorLabel;
    private readonly DropDownList worldSelector;
    private readonly Label filterLabel;
    private readonly TextField filterInput;
    private readonly TextView detailText;
    private readonly Label detailFooter;
    private readonly ExternalDashboard[] externalDashboards;
    private readonly string[] screenFilters = Enumerable.Repeat(string.Empty, 8).ToArray();
    private readonly string[] smokeRows = new string[SmokeRowCount];
    private WorkspaceScreen screen;
    private int activeExternalDashboard = -1;
    private string? lastAdminAction;
    private string? externalDashboardFailure;
    private string appliedDetailText = string.Empty;
    private string pendingDetailText = string.Empty;
    private string[] currentDetailLines = [];
    private string[] visibleDetailLines = [];
    private RuntimeSettingsWindow? runtimeSettingsWindow;
    private readonly IPlayerAdministrativeOperations? playerAdministration;
    private readonly RuntimeConnectionSessionDirectory? connectionSessions;
    private PlayerDetailsWindow? playerDetailsWindow;
    private RuntimeWorldInspectionTarget[] worldInspectionTargets = [];
    private string[] worldInspectionLabels = [];
    private WorldRuntimeId selectedInspectionWorld;
    private bool updatingWorldSelector;

    public DashboardWorkspaceWindow(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INpcOperations npcOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
        IDashboardSource? terminalDashboards,
        IProjectileOperations? projectileOperations = null,
        IWorldItemOperations? worldItemOperations = null,
        SandboxOperations? sandboxOperations = null,
        Func<SandboxTreeSnapshot>? sandboxTreeSource = null,
        IRuntimeWorldInspectionOperations? worldInspectionOperations = null,
        IPlayerAdministrativeOperations? playerAdministration = null,
        RuntimeConnectionSessionDirectory? connectionSessions = null)
    {
        this.dashboardOperations = dashboardOperations ?? throw new ArgumentNullException(nameof(dashboardOperations));
        this.playerOperations = playerOperations ?? throw new ArgumentNullException(nameof(playerOperations));
        this.npcOperations = npcOperations ?? throw new ArgumentNullException(nameof(npcOperations));
        this.projectileOperations = projectileOperations;
        this.worldItemOperations = worldItemOperations;
        this.networkOperations = networkOperations ?? throw new ArgumentNullException(nameof(networkOperations));
        this.worldOperations = worldOperations ?? throw new ArgumentNullException(nameof(worldOperations));
        this.logOperations = logOperations ?? throw new ArgumentNullException(nameof(logOperations));
        this.worldInspectionOperations = worldInspectionOperations;
        this.playerAdministration = playerAdministration;
        this.connectionSessions = connectionSessions;

        Title = "TerraRuntime - System Dashboard";
        externalDashboards = CaptureExternalDashboards(terminalDashboards);

        MenuBar menu = CreateMenu();
        StatusBar status = CreateStatusBar();

        workspace = new View
        {
            Y = Pos.Bottom(menu),
            Width = Dim.Fill(),
            Height = Dim.Fill(status)
        };

        overviewDashboard = new RuntimeOverviewDashboard(sandboxOperations, sandboxTreeSource)
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        overviewDashboard.SettingsRequested += ShowRuntimeSettings;
        overviewDashboard.PlayerOpenRequested += ShowPlayerDetails;

        systemRoot = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false
        };

        detailHeader = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            SchemeName = "Accent"
        };
        worldSelectorLabel = new Label
        {
            X = 1,
            Y = 1,
            Text = "World:",
            SchemeName = "Base",
            Visible = false
        };
        worldSelector = new DropDownList
        {
            X = 9,
            Y = 1,
            Width = Dim.Fill(1),
            ReadOnly = true,
            SchemeName = "Base",
            Visible = false
        };
        filterLabel = new Label
        {
            X = 1,
            Y = 1,
            Text = "Filter:",
            SchemeName = "Base"
        };
        filterInput = new TextField
        {
            X = 9,
            Y = 1,
            Width = Dim.Fill(1),
            Text = string.Empty,
            SchemeName = "Base"
        };
        detailText = new TextView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            WordWrap = false,
            TabKeyAddsTab = false,
            EnterKeyAddsLine = false,
            ViewportSettings = ViewportSettingsFlags.HasScrollBars,
            SchemeName = "Base"
        };
        detailFooter = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            SchemeName = "Base"
        };

        worldSelector.ValueChanged += (_, args) =>
        {
            if (updatingWorldSelector || string.IsNullOrWhiteSpace(args.NewValue))
                return;

            int selectedIndex = Array.FindIndex(
                worldInspectionLabels,
                label => string.Equals(label, args.NewValue, StringComparison.Ordinal));
            if ((uint)selectedIndex >= (uint)worldInspectionTargets.Length)
                return;

            selectedInspectionWorld = worldInspectionTargets[selectedIndex].RuntimeId;
            RefreshSnapshot();
        };

        filterInput.Accepting += (_, args) =>
        {
            ApplyFilterInput();
            args.Handled = true;
        };

        systemRoot.Add(
            detailHeader,
            worldSelectorLabel,
            worldSelector,
            filterLabel,
            filterInput,
            detailText,
            detailFooter);
        workspace.Add(overviewDashboard, systemRoot);
        Add(menu, workspace, status);

        KeyDown += (_, key) =>
        {
            if (key == Key.F2)
            {
                ShowSystemDashboard();
                key.Handled = true;
                return;
            }

            if (key == Key.F.WithCtrl)
            {
                FocusDetailFilter();
                key.Handled = true;
                return;
            }

            if (key == Key.L.WithCtrl)
            {
                ClearDetailFilter();
                key.Handled = true;
                return;
            }

            for (int i = 0; i < Math.Min(externalDashboards.Length, MaximumExternalHotkeys); i++)
            {
                if (key != ExternalDashboardKeys[i])
                    continue;

                ShowExternalDashboard(i);
                key.Handled = true;
                return;
            }
        };
    }

    public void RefreshSnapshot()
    {
        playerDetailsWindow?.RefreshLiveState();

        if (activeExternalDashboard >= 0)
        {
            RefreshExternalDashboard(activeExternalDashboard);
            return;
        }

        if (IsWorldScopedScreen(screen))
            RefreshWorldSelector();

        switch (screen)
        {
            case WorkspaceScreen.Players:
                RefreshPlayers();
                break;
            case WorkspaceScreen.Npcs:
                RefreshNpcs();
                break;
            case WorkspaceScreen.Projectiles:
                RefreshProjectiles();
                break;
            case WorkspaceScreen.Items:
                RefreshItems();
                break;
            case WorkspaceScreen.Network:
                RefreshNetwork();
                break;
            case WorkspaceScreen.World:
                RefreshWorld();
                break;
            case WorkspaceScreen.Logs:
                RefreshLogs();
                break;
            default:
                RefreshSystemDashboard();
                break;
        }
    }

    internal void ShowSystemDashboard() => SelectSystemScreen(WorkspaceScreen.Dashboard, "TerraRuntime - System Dashboard");

    internal void ShowPlayers() => SelectSystemScreen(WorkspaceScreen.Players, "TerraRuntime - Players");

    internal void ShowNpcs() => SelectSystemScreen(WorkspaceScreen.Npcs, "TerraRuntime - NPCs");

    internal void ShowProjectiles() => SelectSystemScreen(WorkspaceScreen.Projectiles, "TerraRuntime - Projectiles");

    internal void ShowItems() => SelectSystemScreen(WorkspaceScreen.Items, "TerraRuntime - Items");

    internal void ShowNetwork() => SelectSystemScreen(WorkspaceScreen.Network, "TerraRuntime - Network");

    internal void ShowWorld() => SelectSystemScreen(WorkspaceScreen.World, "TerraRuntime - World");

    internal void ShowLogs() => SelectSystemScreen(WorkspaceScreen.Logs, "TerraRuntime - Logs");

    internal void ShowRuntimeSettingsForSmoke() => ShowRuntimeSettings();

    internal bool RuntimeSettingsVisibleForSmoke => runtimeSettingsWindow is not null;

    internal string GetRowTextForSmoke(int index)
    {
        if ((uint)index >= SmokeRowCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        return smokeRows[index];
    }

    internal bool DetailTextSupportsSelectionForSmoke => detailText.ReadOnly && detailText.CanFocus;

    internal void ShowExternalDashboardForSmoke(int index) => ShowExternalDashboard(index);

    internal void SetFilterForSmoke(string filter)
    {
        if (screen == WorkspaceScreen.Dashboard)
            return;

        filterInput.Text = filter ?? string.Empty;
        ApplyFilterInput(focusDetail: false);
    }

    internal string GetDetailTextForSmoke() => appliedDetailText;

    internal int GetVisibleDetailRowCountForSmoke() => visibleDetailLines.Length;

    internal string GetDetailFooterForSmoke() => detailFooter.Text?.ToString() ?? string.Empty;

    internal bool WorldSelectorVisibleForSmoke => worldSelector.Visible;

    internal int WorldSelectorTargetCountForSmoke
    {
        get
        {
            RefreshWorldSelector();
            return worldInspectionTargets.Length;
        }
    }

    internal WorldRuntimeId SelectedInspectionWorldForSmoke => selectedInspectionWorld;

    internal bool SelectWorldForSmoke(WorldRuntimeId runtimeId)
    {
        RefreshWorldSelector();
        int index = Array.FindIndex(worldInspectionTargets, target => target.RuntimeId == runtimeId);
        if (index < 0)
            return false;

        worldSelector.Value = worldInspectionLabels[index];
        return selectedInspectionWorld == runtimeId;
    }

    internal void SetInterestManagementEnabled(bool enabled)
    {
        bool queued = dashboardOperations.TrySetInterestManagementEnabled(enabled);
        lastAdminAction = queued
            ? $"Admin: queued interest management {(enabled ? "enable" : "disable")} command"
            : $"Admin: rejected interest management {(enabled ? "enable" : "disable")} command";

        if (activeExternalDashboard < 0 && screen == WorkspaceScreen.Dashboard)
        {
            RefreshSystemDashboard();
            InvalidateSystemRoot(layout: false);
        }
    }

    internal void RequestWorldSaveCheckpoint()
    {
        bool queued = worldOperations.TryRequestSave();
        lastAdminAction = queued
            ? "Admin: queued world save checkpoint"
            : "Admin: rejected world save checkpoint";
        SelectSystemScreen(WorkspaceScreen.World, "TerraRuntime - World");
    }

    private void ShowPlayerDetails(RuntimePlayerSnapshot player)
    {
        if (playerAdministration is null || connectionSessions is null)
            return;

        if (playerDetailsWindow is not null)
        {
            ClosePlayerDetails();
        }

        var window = new PlayerDetailsWindow(player, playerOperations, playerAdministration, connectionSessions);
        playerDetailsWindow = window;
        window.CloseRequested += ClosePlayerDetails;
        workspace.Add(window);
        window.SetFocus();
        workspace.SetNeedsLayout();
        workspace.SetNeedsDraw();
    }

    private void ClosePlayerDetails()
    {
        PlayerDetailsWindow? window = playerDetailsWindow;
        if (window is null)
            return;
        playerDetailsWindow = null;
        window.CloseRequested -= ClosePlayerDetails;
        workspace.Remove(window);
        window.Dispose();
        overviewDashboard.SetFocus();
        workspace.SetNeedsDraw();
    }

    private void ShowRuntimeSettings()
    {
        if (runtimeSettingsWindow is not null)
        {
            runtimeSettingsWindow.SetFocus();
            return;
        }

        var window = new RuntimeSettingsWindow(dashboardOperations);
        runtimeSettingsWindow = window;
        window.CloseRequested += CloseRuntimeSettings;
        workspace.Add(window);
        window.SetFocus();
        workspace.SetNeedsLayout();
        workspace.SetNeedsDraw();
    }

    private void CloseRuntimeSettings()
    {
        RuntimeSettingsWindow? window = runtimeSettingsWindow;
        if (window is null)
            return;

        runtimeSettingsWindow = null;
        window.CloseRequested -= CloseRuntimeSettings;
        workspace.Remove(window);
        window.Dispose();
        overviewDashboard.SetFocus();
        RefreshSnapshot();
        workspace.SetNeedsDraw();
    }

    private MenuBar CreateMenu()
    {
        var dashboardItems = new List<MenuItem>
        {
            new("_System Dashboard", "F2 - TerraRuntime operational overview", ShowSystemDashboard)
        };

        for (int i = 0; i < externalDashboards.Length; i++)
        {
            int capturedIndex = i;
            ExternalDashboard dashboard = externalDashboards[i];
            string hotkey = i < MaximumExternalHotkeys ? $"F{i + 3} - " : string.Empty;
            dashboardItems.Add(new MenuItem(
                EscapeMenuTitle(dashboard.Provider.Title),
                $"{hotkey}trusted host dashboard '{dashboard.Provider.Id}'",
                () => ShowExternalDashboard(capturedIndex)));
        }

        return new MenuBar
        {
            Menus =
            [
                new MenuBarItem(
                    "_File",
                    [new MenuItem("_Close UI", "Return to the plain server console", () => App?.RequestStop())]),
                new MenuBarItem("_Dashboards", dashboardItems.ToArray()),
                new MenuBarItem(
                    "D_etails",
                    [
                        new MenuItem("_Overview", "Built-in operational dashboard", ShowSystemDashboard),
                        new MenuItem("_Players", "Authoritative player read model", ShowPlayers),
                        new MenuItem("_NPCs", "Authoritative NPC read model", ShowNpcs),
                        new MenuItem("P_rojectiles", "Grouped authoritative projectile read model", ShowProjectiles),
                        new MenuItem("_Items", "Grouped authoritative dropped-item read model", ShowItems),
                        new MenuItem("N_etwork", "Connection and replication counters", ShowNetwork),
                        new MenuItem("_World", "World and cache state", ShowWorld),
                        new MenuItem("_Logs", "Recent runtime log events", ShowLogs)
                    ]),
                new MenuBarItem(
                    "_Settings",
                    [new MenuItem("_Runtime settings", "Live listener and runtime controls", ShowRuntimeSettings)]),
                new MenuBarItem(
                    "_View",
                    [
                        new MenuItem("_Filter current details", "Ctrl+F - focus the local detail filter", FocusDetailFilter),
                        new MenuItem("_Clear detail filter", "Ctrl+L - show the complete bounded snapshot", ClearDetailFilter)
                    ]),
                new MenuBarItem(
                    "_Actions",
                    [
                        new MenuItem(
                            "_Enable interest management",
                            "Queue runtime-owned visibility optimization",
                            () => SetInterestManagementEnabled(true)),
                        new MenuItem(
                            "_Disable interest management",
                            "Queue disabling runtime-owned visibility optimization",
                            () => SetInterestManagementEnabled(false)),
                        new MenuItem(
                            "_Save world checkpoint",
                            "Queue a canonical save through the persistence ingress",
                            RequestWorldSaveCheckpoint),
                        new MenuItem(
                            "_Move player between worlds",
                            "Drag a player in the Worlds / Players tree onto the destination world",
                            FocusWorldTree)
                    ]),
                new MenuBarItem(
                    "_Help",
                    [new MenuItem("_About", "", ShowAbout)])
            ]
        };
    }

    private StatusBar CreateStatusBar()
    {
        StatusBar status = new();
        status.Add(new Shortcut(Key.F2, "System", ShowSystemDashboard));
        status.Add(new Shortcut(Key.F.WithCtrl, "Filter", FocusDetailFilter));
        status.Add(new Shortcut(Key.L.WithCtrl, "Clear filter", ClearDetailFilter));
        if (externalDashboards.Length > 0)
            status.Add(new Shortcut(Key.F3, "Host dashboard", () => ShowExternalDashboard(0)));
        status.Add(new Shortcut(
            Terminal.Gui.App.Application.GetDefaultKey(Command.Quit),
            "Close UI",
            () => App?.RequestStop()));
        return status;
    }

    private void SelectSystemScreen(WorkspaceScreen next, string title)
    {
        activeExternalDashboard = -1;
        SetExternalVisibility(-1);
        screen = next;
        bool overview = next == WorkspaceScreen.Dashboard;
        overviewDashboard.Visible = overview;
        systemRoot.Visible = !overview;
        Title = title;
        externalDashboardFailure = null;

        if (!overview)
        {
            detailText.IsSelecting = false;
            filterInput.Text = screenFilters[(int)next];
            ConfigureDetailControls(next);
        }

        RefreshSnapshot();
        InvalidateSystemRoot(layout: true);
    }

    private void ConfigureDetailControls(WorkspaceScreen targetScreen)
    {
        bool worldScoped = worldInspectionOperations is not null && IsWorldScopedScreen(targetScreen);
        worldSelectorLabel.Visible = worldScoped;
        worldSelector.Visible = worldScoped;

        int filterRow = worldScoped ? 2 : 1;
        filterLabel.Y = filterRow;
        filterInput.Y = filterRow;
        detailText.Y = worldScoped ? 3 : 2;
        detailText.Height = Dim.Fill(1);
    }

    private void RefreshWorldSelector()
    {
        if (worldInspectionOperations is null)
            return;

        RuntimeWorldInspectionTarget[] targets = worldInspectionOperations.CaptureTargets().ToArray();
        if (targets.Length == 0)
        {
            worldInspectionTargets = [];
            worldInspectionLabels = [];
            selectedInspectionWorld = default;
            updatingWorldSelector = true;
            try
            {
                worldSelector.Source = new ListWrapper<string>(new ObservableCollection<string>());
                worldSelector.Value = string.Empty;
                worldSelector.Enabled = false;
            }
            finally
            {
                updatingWorldSelector = false;
            }
            return;
        }

        int selectedIndex = Array.FindIndex(targets, target => target.RuntimeId == selectedInspectionWorld);
        if (selectedIndex < 0)
        {
            selectedIndex = Array.FindIndex(targets, static target => target.IsPrimary);
            if (selectedIndex < 0)
                selectedIndex = 0;
            selectedInspectionWorld = targets[selectedIndex].RuntimeId;
        }

        string[] labels = targets.Select(FormatWorldInspectionTarget).ToArray();
        bool sourceChanged = !SameWorldTargets(worldInspectionTargets, targets) ||
                             !worldInspectionLabels.AsSpan().SequenceEqual(labels);
        worldInspectionTargets = targets;
        worldInspectionLabels = labels;

        updatingWorldSelector = true;
        try
        {
            if (sourceChanged)
            {
                worldSelector.Source = new ListWrapper<string>(
                    new ObservableCollection<string>(worldInspectionLabels));
            }
            worldSelector.Enabled = true;
            worldSelector.Value = worldInspectionLabels[selectedIndex];
        }
        finally
        {
            updatingWorldSelector = false;
        }
    }

    private static bool SameWorldTargets(
        ReadOnlySpan<RuntimeWorldInspectionTarget> left,
        ReadOnlySpan<RuntimeWorldInspectionTarget> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i].RuntimeId != right[i].RuntimeId ||
                left[i].SessionId != right[i].SessionId ||
                left[i].IsPrimary != right[i].IsPrimary ||
                !string.Equals(left[i].DisplayName, right[i].DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string FormatWorldInspectionTarget(RuntimeWorldInspectionTarget target) =>
        $"{target.DisplayName} [{(target.IsPrimary ? "primary" : "sandbox")}]";

    private static bool IsWorldScopedScreen(WorkspaceScreen value) =>
        value is WorkspaceScreen.Players
            or WorkspaceScreen.Npcs
            or WorkspaceScreen.Projectiles
            or WorkspaceScreen.Items
            or WorkspaceScreen.World;

    private bool TryGetSelectedWorldTarget(out RuntimeWorldInspectionTarget target)
    {
        int index = Array.FindIndex(worldInspectionTargets, item => item.RuntimeId == selectedInspectionWorld);
        if ((uint)index < (uint)worldInspectionTargets.Length)
        {
            target = worldInspectionTargets[index];
            return true;
        }

        target = default;
        return false;
    }

    private void FocusDetailFilter()
    {
        if (activeExternalDashboard >= 0 || screen == WorkspaceScreen.Dashboard)
            return;

        filterInput.SetFocus();
    }

    private void ClearDetailFilter()
    {
        if (activeExternalDashboard >= 0 || screen == WorkspaceScreen.Dashboard)
            return;

        if (screenFilters[(int)screen].Length == 0 && filterInput.Text.Trim().Length == 0)
            return;

        filterInput.Text = string.Empty;
        screenFilters[(int)screen] = string.Empty;
        RefreshSnapshot();
        detailText.SetFocus();
    }

    private void ApplyFilterInput(bool focusDetail = true)
    {
        if (activeExternalDashboard >= 0 || screen == WorkspaceScreen.Dashboard)
            return;

        screenFilters[(int)screen] = filterInput.Text.Trim();
        RefreshSnapshot();
        if (focusDetail)
            detailText.SetFocus();
    }

    private void ShowExternalDashboard(int index)
    {
        if ((uint)index >= (uint)externalDashboards.Length)
            return;

        ExternalDashboard dashboard = externalDashboards[index];
        try
        {
            if (dashboard.Root is null)
            {
                View root = dashboard.Provider.CreateDashboard()
                    ?? throw new InvalidOperationException("Dashboard provider returned null root view.");
                root.X = 0;
                root.Y = 0;
                root.Width = Dim.Fill();
                root.Height = Dim.Fill();
                root.Visible = false;
                workspace.Add(root);
                dashboard.Root = root;
            }

            activeExternalDashboard = index;
            overviewDashboard.Visible = false;
            systemRoot.Visible = false;
            SetExternalVisibility(index);
            Title = $"TerraRuntime - {dashboard.Provider.Title}";
            externalDashboardFailure = null;
            dashboard.Provider.Refresh(dashboard.Root);
            InvalidateExternalRoot(dashboard.Root);
        }
        catch (Exception exception)
        {
            RestoreSystemDashboardAfterExternalFailure(
                $"Dashboard '{SanitizeText(dashboard.Provider.Title, 32)}' failed: {SanitizeText(exception.Message, 96)}");
        }
    }

    private void RefreshExternalDashboard(int index)
    {
        if ((uint)index >= (uint)externalDashboards.Length)
            return;

        ExternalDashboard dashboard = externalDashboards[index];
        if (dashboard.Root is null)
        {
            ShowExternalDashboard(index);
            return;
        }

        try
        {
            dashboard.Provider.Refresh(dashboard.Root);
            InvalidateExternalRoot(dashboard.Root);
        }
        catch (Exception exception)
        {
            RestoreSystemDashboardAfterExternalFailure(
                $"Dashboard '{SanitizeText(dashboard.Provider.Title, 32)}' refresh failed: {SanitizeText(exception.Message, 96)}");
        }
    }

    private void RestoreSystemDashboardAfterExternalFailure(string message)
    {
        activeExternalDashboard = -1;
        SetExternalVisibility(-1);
        systemRoot.Visible = false;
        overviewDashboard.Visible = true;
        screen = WorkspaceScreen.Dashboard;
        externalDashboardFailure = message;
        Title = "TerraRuntime - System Dashboard";
        RefreshSystemDashboard();
        InvalidateSystemRoot(layout: true);
    }

    private void SetExternalVisibility(int visibleIndex)
    {
        for (int i = 0; i < externalDashboards.Length; i++)
        {
            if (externalDashboards[i].Root is View root)
                root.Visible = i == visibleIndex;
        }
    }

    private void InvalidateSystemRoot(bool layout)
    {
        View root = screen == WorkspaceScreen.Dashboard ? overviewDashboard : systemRoot;
        if (layout)
        {
            root.SetNeedsLayout();
            workspace.SetNeedsLayout();
        }

        root.SetNeedsDraw();
        workspace.SetNeedsDraw();
    }

    private void InvalidateExternalRoot(View root)
    {
        root.SetNeedsLayout();
        root.SetNeedsDraw();
        workspace.SetNeedsLayout();
        workspace.SetNeedsDraw();
    }

    private void RefreshSystemDashboard()
    {
        Array.Fill(smokeRows, string.Empty);

        RuntimeDashboardSnapshot runtime = dashboardOperations.CaptureSnapshot();
        RuntimeNetworkSnapshot network = networkOperations.CaptureSnapshot();
        RuntimeWorldSnapshot world = worldOperations.CaptureSnapshot();
        RuntimePlayersSnapshot players = playerOperations.CaptureSnapshot();
        RuntimeLogSnapshot chat = logOperations.CaptureSnapshot(OperationsLogLevel.Debug, "Chat", 8);
        RuntimeLogSnapshot logs = logOperations.CaptureSnapshot(OperationsLogLevel.Information, 12);

        smokeRows[0] =
            $"{runtime.Lifecycle} | world {SanitizeText(runtime.WorldName, 24)} | players {players.Players.Length}/{runtime.MaxPlayers} | " +
            $"listen {SanitizeText(runtime.BindAddress, 20)}:{runtime.Port} {runtime.ListenerState} g{runtime.ListenerGeneration} | " +
            $"interest {(runtime.InterestManagementEnabled ? "ON" : "OFF")}";

        string? status = externalDashboardFailure ?? lastAdminAction;
        overviewDashboard.Refresh(runtime, network, world, players, logs, chat, status);
    }

    private void RefreshPlayers()
    {
        RuntimePlayersSnapshot snapshot;
        string worldSuffix = string.Empty;
        if (worldInspectionOperations is not null)
        {
            if (!TryGetSelectedWorldTarget(out RuntimeWorldInspectionTarget target) ||
                !worldInspectionOperations.TryCapturePlayers(target.RuntimeId, out snapshot))
            {
                SetDetailContent("PLAYERS  <loading selected world>", []);
                return;
            }
            worldSuffix = $" @ {SanitizeText(target.DisplayName, 28)}";
        }
        else
        {
            snapshot = playerOperations.CaptureSnapshot();
        }

        ReadOnlySpan<RuntimePlayerSnapshot> players = snapshot.Players.Span;
        var lines = new string[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            string health = player.HasHealth ? $"{player.Life}/{player.MaxLife}" : "n/a";
            string mana = player.HasMana ? $"{player.Mana}/{player.MaxMana}" : "n/a";
            string mount = player.MountType == 0 ? "none" : player.MountType.ToString(CultureInfo.InvariantCulture);
            lines[i] =
                $"#{player.Slot,3} g{player.Generation,-4} c{player.ConnectionId,-5} {SanitizeName(player.Name),-20} " +
                $"team {player.Team} pos {player.PositionX / 16f:F1},{player.PositionY / 16f:F1}t " +
                $"vel {player.VelocityX:F1},{player.VelocityY:F1} item-slot {player.SelectedItem} mount {mount} HP {health} MP {mana}";
        }

        SetDetailContent($"PLAYERS{worldSuffix}  {players.Length} playing", lines);
    }

    private void RefreshNpcs()
    {
        RuntimeNpcsSnapshot snapshot;
        string worldSuffix = string.Empty;
        if (worldInspectionOperations is not null)
        {
            if (!TryGetSelectedWorldTarget(out RuntimeWorldInspectionTarget target) ||
                !worldInspectionOperations.TryCaptureNpcs(target.RuntimeId, out snapshot))
            {
                SetDetailContent("NPCS  <loading or telemetry unavailable for selected world>", []);
                return;
            }
            worldSuffix = $" @ {SanitizeText(target.DisplayName, 28)}";
        }
        else
        {
            snapshot = npcOperations.CaptureSnapshot();
        }

        ReadOnlySpan<RuntimeNpcSnapshot> npcs = snapshot.Npcs.Span;
        var lines = new string[npcs.Length];
        for (int i = 0; i < npcs.Length; i++)
        {
            RuntimeNpcSnapshot npc = npcs[i];
            string collision = $"{(npc.CollideX ? 'X' : '-')}{(npc.CollideY ? 'Y' : '-')}";
            string flags =
                $"{collision}/{(npc.Wet ? "wet" : "dry")}/{(npc.NoGravity ? "ng" : "g")}/{(npc.NoTileCollide ? "ntc" : "tc")}";
            lines[i] =
                $"#{npc.Slot,3} g{npc.Generation,-4} r{npc.Revision,-5} type {npc.Type}/{npc.NetId} " +
                $"pos {npc.PositionX / 16f:F1},{npc.PositionY / 16f:F1}t vel {npc.VelocityX:F1},{npc.VelocityY:F1} " +
                $"target {npc.Target} ai {npc.Ai0:F1}/{npc.Ai1:F1}/{npc.Ai2:F1}/{npc.Ai3:F1} dir {npc.DirectionX},{npc.DirectionY} {flags}";
        }

        SetDetailContent(
            $"NPCS{worldSuffix}  {npcs.Length} live | commits spawn {snapshot.CommittedSpawns:N0} update {snapshot.CommittedUpdates:N0} despawn {snapshot.CommittedDespawns:N0}",
            lines);
    }

    private void RefreshProjectiles()
    {
        RuntimeProjectilesSnapshot snapshot;
        RuntimePlayersSnapshot playersSnapshot;
        string worldSuffix = string.Empty;
        if (worldInspectionOperations is not null)
        {
            if (!TryGetSelectedWorldTarget(out RuntimeWorldInspectionTarget target) ||
                !worldInspectionOperations.TryCaptureProjectiles(target.RuntimeId, out snapshot) ||
                !worldInspectionOperations.TryCapturePlayers(target.RuntimeId, out playersSnapshot))
            {
                SetDetailContent("PROJECTILES  <loading or telemetry unavailable for selected world>", []);
                return;
            }
            worldSuffix = $" @ {SanitizeText(target.DisplayName, 28)}";
        }
        else
        {
            if (projectileOperations is null)
            {
                SetDetailContent("PROJECTILES  <telemetry unavailable>", []);
                return;
            }

            snapshot = projectileOperations.CaptureSnapshot();
            playersSnapshot = playerOperations.CaptureSnapshot();
        }

        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        ReadOnlySpan<RuntimeProjectileGroupSnapshot> groups = snapshot.Groups.Span;
        var lines = new string[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            RuntimeProjectileGroupSnapshot group = groups[i];
            string type = ProjectileDisplayFormatter.FormatType(group.Type);
            string owner = ProjectileDisplayFormatter.FormatOwner(group.Spawner, players);
            lines[i] =
                $"x{group.Count,-4} {type,-28} {owner,-28} " +
                $"pos~ {group.AveragePositionX / 16f:F1},{group.AveragePositionY / 16f:F1}t " +
                $"vel~ {group.AverageVelocityX:F1},{group.AverageVelocityY:F1} dmg<={group.MaxDamage} orig<={group.MaxOriginalDamage} kb<={group.MaxKnockBack:F1}";
        }

        SetDetailContent(
            $"PROJECTILES{worldSuffix}  {snapshot.ActiveProjectiles} live in {groups.Length} spawner/type groups | commits {snapshot.CommittedSpawns:N0}/{snapshot.CommittedUpdates:N0}/{snapshot.CommittedDespawns:N0}",
            lines);
    }

    private void RefreshItems()
    {
        RuntimeWorldItemsSnapshot snapshot;
        string worldSuffix = string.Empty;
        if (worldInspectionOperations is not null)
        {
            if (!TryGetSelectedWorldTarget(out RuntimeWorldInspectionTarget target) ||
                !worldInspectionOperations.TryCaptureWorldItems(target.RuntimeId, out snapshot))
            {
                SetDetailContent("ITEMS  <loading or telemetry unavailable for selected world>", []);
                return;
            }
            worldSuffix = $" @ {SanitizeText(target.DisplayName, 28)}";
        }
        else
        {
            if (worldItemOperations is null)
            {
                SetDetailContent("ITEMS  <telemetry unavailable>", []);
                return;
            }
            snapshot = worldItemOperations.CaptureSnapshot();
        }

        ReadOnlySpan<RuntimeWorldItemGroupSnapshot> groups = snapshot.Groups.Span;
        var lines = new string[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            RuntimeWorldItemGroupSnapshot group = groups[i];
            lines[i] =
                $"x{group.DropCount,-4} type #{group.ItemNetId,-6} stack total {group.TotalStack,-8:N0} max {group.MaxStack,-5} " +
                $"reserved {group.ReservedDrops,-4} shimmer {group.ShimmeredDrops,-4} " +
                $"pos~ {group.AveragePositionX / 16f:F1},{group.AveragePositionY / 16f:F1}t";
        }

        SetDetailContent($"ITEMS{worldSuffix}  {snapshot.ActiveItems} live in {groups.Length} item-type groups", lines);
    }

    private void RefreshNetwork()
    {
        RuntimeNetworkSnapshot snapshot = networkOperations.CaptureSnapshot();
        var lines = new List<string>(20)
        {
            $"Admission   accepted {snapshot.AcceptedConnections:N0}  rejected {snapshot.RejectedConnections:N0}  capacity {snapshot.AdmissionCapacityRejectedConnections:N0}  rate {snapshot.AdmissionRateRejectedConnections:N0}",
            $"Inbound 1s  {snapshot.InboundWindowFrames:N0} frames  {FormatKibibytes(snapshot.InboundWindowBytes)}  rejected {snapshot.RejectedInboundFrames:N0}  total {snapshot.InboundTotalFrames:N0}/{FormatKibibytes(snapshot.InboundTotalBytes)}",
            $"Outbound    queues {snapshot.TrackedOutboundQueues}  frames {snapshot.QueuedOutboundFrames:N0}  {FormatKibibytes(snapshot.QueuedOutboundBytes)}  peak {snapshot.PeakQueuedOutboundFrames:N0}/{FormatKibibytes(snapshot.PeakQueuedOutboundBytes)}  rejected {snapshot.RejectedOutboundFrames:N0}  slow {snapshot.SlowClients}",
            $"Movement    relay {snapshot.RelayedMovementFrames:N0}  AOI resync {snapshot.MovementResyncFrames:N0}  player active {snapshot.PlayerActiveBaselineFrames:N0}  deactivated {snapshot.PlayerDeactivationFrames:N0}",
            $"Appearance  relay {snapshot.RelayedAppearanceFrames:N0}  baseline {snapshot.AppearanceBaselineFrames:N0}",
            $"Equipment   relay {snapshot.RelayedEquipmentFrames:N0}  baseline {snapshot.EquipmentBaselineFrames:N0}  dropped {snapshot.DroppedEquipmentSnapshotUpdates:N0}",
            $"NPC         relay {snapshot.NpcRelayedFrames:N0}  baseline {snapshot.NpcBaselineFrames:N0}  rejected {snapshot.NpcRejectedFrames:N0}  unsupported {snapshot.NpcUnsupportedCommits:N0}",
            $"Projectile  relay {snapshot.ProjectileRelayedFrames:N0}  baseline {snapshot.ProjectileBaselineFrames:N0}  rejected {snapshot.ProjectileRejectedFrames:N0}  unsupported {snapshot.ProjectileUnsupportedCommits:N0}",
            $"Items       relay {snapshot.WorldItemRelayedFrames:N0}  rejected {snapshot.WorldItemRejectedFrames:N0}  unsupported {snapshot.WorldItemUnsupportedCommits:N0}",
            $"Stops       protocol {snapshot.StopProtocolFailures:N0}  rate {snapshot.StopRateLimited:N0}  handshake {snapshot.StopInvalidHandshake:N0}  unsupported {snapshot.StopUnsupportedProtocol:N0}  slow {snapshot.StopSlowClient:N0}  frame-rejected {snapshot.StopFrameRejected:N0}"
        };

        ReadOnlySpan<RuntimeConnectionRateDetail> rates = snapshot.TopInboundRates.Span;
        if (rates.Length == 0)
        {
            lines.Add("IN  <no active inbound traffic>");
        }
        else
        {
            for (int i = 0; i < rates.Length; i++)
            {
                RuntimeConnectionRateDetail rate = rates[i];
                lines.Add($"IN  #{rate.ConnectionId,-5} {rate.WindowFrames,6:N0} f/s  {FormatKibibytes(rate.WindowBytes),10}/s  total {rate.TotalFrames:N0}");
            }
        }

        ReadOnlySpan<TerrariaMessageTrafficDetail> topMessages = snapshot.TopMessageTraffic.Span;
        if (topMessages.Length == 0)
        {
            lines.Add("MSG <no message traffic in rolling window>");
        }
        else
        {
            double windowSeconds = Math.Max(1d, snapshot.MessageTrafficWindow.TotalSeconds);
            for (int i = 0; i < topMessages.Length; i++)
            {
                TerrariaMessageTrafficDetail traffic = topMessages[i];
                string direction = traffic.Direction == TerrariaMessageDirection.Outbound ? "OUT" : "IN ";
                string messageName = traffic.IsKnownMessageId
                    ? ((TerrariaMessageId)traffic.MessageId).ToString()
                    : "Unknown";
                lines.Add(
                    $"MSG {direction} #{traffic.MessageId,-3} {messageName,-24} " +
                    $"{traffic.WindowFrames / windowSeconds,7:F1} f/s  {FormatKibibytesPerSecond(traffic.WindowBytes / windowSeconds),10}  total {traffic.TotalFrames:N0}");
            }
        }

        lines.Add(
            $"Frame reject malformed {snapshot.RejectedMalformedProtocol:N0}  rate {snapshot.RejectedRateLimited:N0}  state {snapshot.RejectedInvalidState:N0}  gameplay {snapshot.RejectedGameplay:N0}  backpressure {snapshot.RejectedBackpressure:N0}");

        ReadOnlySpan<RuntimeConnectionQueueDetail> queues = snapshot.TopOutboundQueues.Span;
        if (queues.Length == 0)
        {
            lines.Add("OUT <no queued/rejected/slow clients>");
        }
        else
        {
            for (int i = 0; i < queues.Length; i++)
            {
                RuntimeConnectionQueueDetail queue = queues[i];
                lines.Add(
                    $"OUT #{queue.ConnectionId,-5} {queue.QueuedFrames:N0}/{queue.MaxFrames:N0} frames  " +
                    $"{FormatKibibytes(queue.QueuedBytes)}/{FormatKibibytes(queue.MaxQueuedBytes)}  " +
                    $"peak {queue.PeakQueuedFrames:N0}/{FormatKibibytes(queue.PeakQueuedBytes)}  rejected {queue.RejectedFrames:N0}  {(queue.SlowClient ? "SLOW" : "ok")}");
            }
        }

        lines.Add(
            $"Timeouts    handshake {snapshot.StopHandshakeTimeout:N0}  join {snapshot.StopJoinTimeout:N0}  idle {snapshot.StopIdleTimeout:N0}  application-stop {snapshot.StopApplicationStopped:N0}");

        SetDetailContent(
            $"NETWORK  active {snapshot.ActiveConnections}  registered {snapshot.RegisteredConnections}",
            lines);
    }

    private void RefreshWorld()
    {
        RuntimeWorldInspectionTarget? selectedTarget = null;
        WorldRuntimeSnapshot? runtimeSnapshot = null;
        if (worldInspectionOperations is not null)
        {
            if (!TryGetSelectedWorldTarget(out RuntimeWorldInspectionTarget target) ||
                !worldInspectionOperations.TryCaptureRuntime(target.RuntimeId, out WorldRuntimeSnapshot selectedRuntime))
            {
                SetDetailContent("WORLD  <loading selected world>", []);
                return;
            }

            selectedTarget = target;
            runtimeSnapshot = selectedRuntime;
            if (!target.IsPrimary)
            {
                SetDetailContent(
                    $"WORLD @ {SanitizeText(target.DisplayName, 36)}  {selectedRuntime.Lifecycle}",
                    BuildRuntimeWorldLines(selectedRuntime),
                    lastAdminAction);
                return;
            }
        }

        RuntimeWorldSnapshot snapshot = worldOperations.CaptureSnapshot();
        var lines = new List<string>(24);
        if (runtimeSnapshot is WorldRuntimeSnapshot runtime)
            lines.AddRange(BuildRuntimeWorldLines(runtime));

        lines.AddRange(
        [
            $"Identity    {snapshot.UniqueId:D}",
            $"Format      {snapshot.FormatVersion}  worldgen {snapshot.WorldGeneratorVersion}",
            $"Dimensions  {snapshot.WidthTiles}x{snapshot.HeightTiles}  tiles {snapshot.TileCount:N0}",
            $"Objects     chests {snapshot.ChestCount:N0}  signs {snapshot.SignCount:N0}  tile entities {snapshot.TileEntityCount:N0}",
            $"NPC state   town {snapshot.TownNpcCount:N0}  persistent {snapshot.PersistentNpcCount:N0}  rooms {snapshot.TownRoomCount:N0}",
            $"Cache       {(snapshot.RuntimeCacheHit ? "hit" : "miss")}  reason {snapshot.InitialCacheResult}/{snapshot.InitialCacheDetailCode}  readers {snapshot.CacheParallelReads}",
            $"Load        file {snapshot.FileReadMilliseconds:F2} ms  cache {snapshot.CacheLoadMilliseconds:F2} ms  canonical {snapshot.CanonicalWorldLoadMilliseconds:F2} ms  build {snapshot.CacheWriteMilliseconds:F2} ms",
            $"Ready       world {snapshot.WorldReadyMilliseconds:F2} ms  network {snapshot.NetworkReadyMilliseconds:F2} ms"
        ]);

        if (snapshot.SectionCacheAvailable)
        {
            lines.Add(
                $"Pipeline    in-flight {snapshot.SectionCacheInFlight:N0}  submitted {snapshot.SectionCacheSubmitted:N0}  rejected {snapshot.SectionCacheRejected:N0}  " +
                $"stale {snapshot.SectionCacheStaleResults:N0}  encode-fail {snapshot.SectionCacheEncodeFailures:N0}  publish-reject {snapshot.SectionCachePublishRejections:N0}  encode {snapshot.SectionCacheTotalEncodeMilliseconds:F1} ms");
        }

        if (snapshot.RuntimeClockAvailable)
        {
            lines.Add($"Clock       {(snapshot.RuntimeDayTime ? "day" : "night")}  time {snapshot.RuntimeTime:N0}  rate {snapshot.RuntimeDayRate}  moon {snapshot.RuntimeMoonPhase}");
            lines.Add($"Slime rain  {snapshot.RuntimeSlimeRainTime:N0}");
        }

        if (snapshot.SectionCacheAvailable)
        {
            lines.Add(
                $"On-demand   request {snapshot.SectionCacheOnDemandRequests:N0}  unique {snapshot.SectionCacheOnDemandUniqueRequests:N0}  " +
                $"dedup {snapshot.SectionCacheOnDemandDeduplicatedRequests:N0}  pending {snapshot.SectionCacheOnDemandPendingRequests:N0}/{snapshot.SectionCacheOnDemandCapacity:N0}  " +
                $"rejected {snapshot.SectionCacheOnDemandRejectedRequests:N0}  waits done/timeout {snapshot.SectionCacheWaitCompletions:N0}/{snapshot.SectionCacheWaitTimeouts:N0}");
            lines.Add($"Sections    {snapshot.SectionCacheEntries:N0}/{snapshot.SectionCacheMaximumEntries:N0}  {FormatMebibytes(snapshot.SectionCacheBytes)}  dirty {snapshot.SectionCacheDirtyBacklog:N0}");
            lines.Add($"Lookups     hit {snapshot.SectionCacheHits:N0}  miss {snapshot.SectionCacheMisses:N0}  stale {snapshot.SectionCacheStaleReads:N0}  waits {snapshot.SectionCacheWaits:N0}");
            lines.Add($"Rebuild     queued {snapshot.SectionCachePendingWork:N0}  active {snapshot.SectionCacheActiveWorkers:N0}  published {snapshot.SectionCachePublished:N0}");
        }

        if (snapshot.Persistence is RuntimeWorldPersistenceSnapshot persistence)
        {
            string shadow = persistence.TileShadowReady
                ? "ready"
                : $"sync({persistence.RemainingBootstrapSections:N0})";
            string request = persistence.SaveRequested ? "pending" : "idle";
            string write = persistence.WriteActive
                ? "active"
                : persistence.PendingWrite ? "pending" : "idle";
            lines.Add(
                $"Save        shadow {shadow} dirty {persistence.PendingDirtyTileSections:N0} request {request} write {write} " +
                $"done {persistence.CompletedWrites:N0}/{persistence.StartedWrites:N0} accepted {persistence.AcceptedSnapshots:N0} " +
                $"coalesced {persistence.CoalescedSnapshots:N0} failed {persistence.FailedWrites:N0}  " +
                $"last-ms snap/ser/write {persistence.LastSnapshotCaptureMilliseconds:F2}/{persistence.LastSerializationMilliseconds:F2}/{persistence.LastWriteMilliseconds:F2}");
        }

        string selectedSuffix = selectedTarget is RuntimeWorldInspectionTarget targetValue
            ? $" @ {SanitizeText(targetValue.DisplayName, 36)}"
            : string.Empty;
        SetDetailContent(
            $"WORLD{selectedSuffix}  {(snapshot.Ready ? "ready" : "not ready")}  {SanitizeText(snapshot.Name, 36)}  id {snapshot.WorldId}",
            lines,
            lastAdminAction);
    }

    private static IReadOnlyList<string> BuildRuntimeWorldLines(WorldRuntimeSnapshot runtime) =>
    [
        $"Runtime     {runtime.Identity.RuntimeId}  session {runtime.Identity.SessionId}",
        $"Lifecycle   {runtime.Lifecycle}  persistence {runtime.PersistenceMode}  source {runtime.Source.GetType().Name}",
        $"Tick        {runtime.Tick:N0}  TPS {runtime.ObservedTicksPerSecond:F1}/{runtime.TargetTicksPerSecond}",
        $"Entities    connections {runtime.Connections:N0}  NPCs {runtime.Npcs:N0}  projectiles {runtime.Projectiles:N0}  items {runtime.WorldItems:N0}",
        runtime.Fault is null
            ? "Fault       none"
            : $"Fault       {SanitizeText(runtime.Fault.Message, 88)}"
    ];

    private void RefreshLogs()
    {
        RuntimeLogSnapshot snapshot = logOperations.CaptureSnapshot(OperationsLogLevel.Debug, MaximumLogEntries);
        ReadOnlySpan<RuntimeLogEntry> entries = snapshot.Entries.Span;
        var lines = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            lines[i] = FormatLogEntry(entries[i]);

        SetDetailContent(
            $"LOG  published {snapshot.PublishedEntries:N0}  overwritten {snapshot.OverwrittenEntries:N0}",
            lines);
    }

    private void SetDetailContent(string title, IReadOnlyList<string> lines, string? administrativeFooter = null)
    {
        currentDetailLines = lines.Count == 0 ? [] : lines.ToArray();
        string query = screenFilters[(int)screen];
        visibleDetailLines = query.Length == 0
            ? currentDetailLines
            : currentDetailLines
                .Where(line => line.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        detailHeader.Text = query.Length == 0
            ? title
            : $"{title}  |  filter '{SanitizeText(query, 48)}' => {visibleDetailLines.Length}/{currentDetailLines.Length}";

        string text = visibleDetailLines.Length == 0
            ? (query.Length == 0 ? "<no entries>" : "<no matching entries>")
            : string.Join(Environment.NewLine, visibleDetailLines);
        SetSelectableDetailText(text);

        string filterStatus = query.Length == 0
            ? $"{currentDetailLines.Length} rows"
            : $"{visibleDetailLines.Length}/{currentDetailLines.Length} rows";
        string shortcuts = "Ctrl+F filter · Ctrl+L clear · F2 system";
        detailFooter.Text = string.IsNullOrWhiteSpace(administrativeFooter)
            ? $"{filterStatus} · {shortcuts}"
            : $"{SanitizeText(administrativeFooter, 72)} · {filterStatus} · {shortcuts}";

        Array.Fill(smokeRows, string.Empty);
        smokeRows[0] = title;
        int copy = Math.Min(visibleDetailLines.Length, SmokeRowCount - 2);
        for (int i = 0; i < copy; i++)
            smokeRows[i + 1] = visibleDetailLines[i];
        smokeRows[SmokeRowCount - 1] = detailFooter.Text?.ToString() ?? string.Empty;

        detailHeader.SetNeedsDraw();
        detailFooter.SetNeedsDraw();
        systemRoot.SetNeedsDraw();
    }

    private void SetSelectableDetailText(string text)
    {
        if (string.Equals(appliedDetailText, text, StringComparison.Ordinal))
        {
            pendingDetailText = string.Empty;
            return;
        }

        pendingDetailText = text;
        if (detailText.IsSelecting && detailText.SelectedLength > 0)
            return;

        detailText.Text = pendingDetailText;
        appliedDetailText = pendingDetailText;
        pendingDetailText = string.Empty;
        detailText.SetNeedsDraw();
    }

    private static ExternalDashboard[] CaptureExternalDashboards(
        IDashboardSource? source)
    {
        if (source is null)
            return [];

        ReadOnlySpan<IDashboardProvider> providers = source.CaptureDashboards().Span;
        if (providers.Length == 0)
            return [];

        var dashboards = new ExternalDashboard[providers.Length];
        for (int i = 0; i < providers.Length; i++)
            dashboards[i] = new ExternalDashboard(providers[i]);
        return dashboards;
    }

    private static string EscapeMenuTitle(string value)
    {
        string sanitized = SanitizeText(value, 48);
        return sanitized.Replace("_", "__", StringComparison.Ordinal);
    }

    private static string SanitizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "<unnamed>" : SanitizeText(name, 20);

    private static string SanitizeText(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        int length = Math.Min(value.Length, maximumLength);
        char[] buffer = new char[length];
        for (int i = 0; i < length; i++)
            buffer[i] = char.IsControl(value[i]) ? ' ' : value[i];
        return new string(buffer);
    }

    private static string FormatLogEntry(RuntimeLogEntry entry) =>
        $"{entry.TimestampUtc:HH:mm:ss.fff} {FormatLevel(entry.Level),-4} {SanitizeText(entry.Source, 14),-14} {SanitizeText(entry.Message, 92)}";

    private static string FormatLevel(OperationsLogLevel level) =>
        level switch
        {
            OperationsLogLevel.Debug => "DBG",
            OperationsLogLevel.Information => "INFO",
            OperationsLogLevel.Warning => "WARN",
            OperationsLogLevel.Error => "ERR",
            _ => "?"
        };

    private static string FormatMebibytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

    private static string FormatKibibytesPerSecond(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 0d)
            return "0 KiB/s";
        return string.Create(CultureInfo.InvariantCulture, $"{bytesPerSecond / 1024d:F1} KiB/s");
    }

    private static string FormatKibibytes(long bytes) =>
        (bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " KiB";

    private void FocusWorldTree()
    {
        ShowSystemDashboard();
        overviewDashboard.FocusWorldTree();
    }

    private void ShowAbout() =>
        MessageBox.Query(
            App!,
            "TerraRuntime",
            "F2 opens the tiled TerraRuntime System Dashboard. Details use complete bounded scrollable snapshots; Ctrl+F filters the current detail view and Ctrl+L clears it. Double-click an overview tile to maximize/restore it. F3-F12 open independent dashboards registered by trusted host modules.",
            "OK");

    private sealed class ExternalDashboard(IDashboardProvider provider)
    {
        public IDashboardProvider Provider { get; } = provider;
        public View? Root { get; set; }
    }

    private enum WorkspaceScreen
    {
        Dashboard,
        Players,
        Npcs,
        Projectiles,
        Items,
        Network,
        World,
        Logs
    }
}

#pragma warning restore CS0618
