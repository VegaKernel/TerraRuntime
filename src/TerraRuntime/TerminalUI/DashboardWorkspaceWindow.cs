using System.Globalization;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

/// <summary>
/// Operator-facing terminal workspace. The built-in System Dashboard is always owned by TerraRuntime.
/// Trusted host modules may contribute independent dashboard roots, never controls inside the system view.
/// </summary>
internal sealed class DashboardWorkspaceWindow : Runnable
{
    private const int RowCount = 18;
    private const int TpsHistoryLength = 40;
    private const int MaximumExternalHotkeys = 10;

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
    private readonly View workspace;
    private readonly View systemRoot;
    private readonly Label[] rows = new Label[RowCount];
    private readonly ExternalDashboard[] externalDashboards;
    private readonly double[] tpsHistory = new double[TpsHistoryLength];
    private int tpsHistoryCount;
    private int tpsHistoryNext;
    private WorkspaceScreen screen;
    private int activeExternalDashboard = -1;
    private string? lastAdminAction;
    private string? externalDashboardFailure;

    public DashboardWorkspaceWindow(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INpcOperations npcOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
        ITerraRuntimeTerminalDashboardSource? terminalDashboards,
        IProjectileOperations? projectileOperations = null,
        IWorldItemOperations? worldItemOperations = null)
    {
        this.dashboardOperations = dashboardOperations ?? throw new ArgumentNullException(nameof(dashboardOperations));
        this.playerOperations = playerOperations ?? throw new ArgumentNullException(nameof(playerOperations));
        this.npcOperations = npcOperations ?? throw new ArgumentNullException(nameof(npcOperations));
        this.projectileOperations = projectileOperations;
        this.worldItemOperations = worldItemOperations;
        this.networkOperations = networkOperations ?? throw new ArgumentNullException(nameof(networkOperations));
        this.worldOperations = worldOperations ?? throw new ArgumentNullException(nameof(worldOperations));
        this.logOperations = logOperations ?? throw new ArgumentNullException(nameof(logOperations));

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

        systemRoot = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        Pos y = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new Label
            {
                X = 1,
                Y = y,
                Width = Dim.Fill(1)
            };
            systemRoot.Add(rows[i]);
            y = Pos.Bottom(rows[i]);
        }

        workspace.Add(systemRoot);
        Add(menu, workspace, status);

        KeyDown += (_, key) =>
        {
            if (key == Key.F2)
            {
                ShowSystemDashboard();
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
        if (activeExternalDashboard >= 0)
        {
            RefreshExternalDashboard(activeExternalDashboard);
            return;
        }

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

    internal string GetRowTextForSmoke(int index)
    {
        if ((uint)index >= (uint)rows.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return rows[index].Text?.ToString() ?? string.Empty;
    }

    internal void ShowExternalDashboardForSmoke(int index) => ShowExternalDashboard(index);

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
                            "Requires the future multi-world supervisor and authoritative transfer operation",
                            ShowWorldTransferUnavailable)
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
        if (externalDashboards.Length > 0)
            status.Add(new Shortcut(Key.F3, "Host dashboard", () => ShowExternalDashboard(0)));
        status.Add(new Shortcut(
            Application.GetDefaultKey(Command.Quit),
            "Close UI",
            () => App?.RequestStop()));
        return status;
    }

    private void SelectSystemScreen(WorkspaceScreen next, string title)
    {
        activeExternalDashboard = -1;
        SetExternalVisibility(-1);
        systemRoot.Visible = true;
        screen = next;
        Title = title;
        externalDashboardFailure = null;
        RefreshSnapshot();
        InvalidateSystemRoot(layout: true);
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
        systemRoot.Visible = true;
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
        if (layout)
        {
            systemRoot.SetNeedsLayout();
            workspace.SetNeedsLayout();
        }

        systemRoot.SetNeedsDraw();
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
        ClearRows();

        RuntimeDashboardSnapshot runtime = dashboardOperations.CaptureSnapshot();
        RuntimeNetworkSnapshot network = networkOperations.CaptureSnapshot();
        RuntimeWorldSnapshot world = worldOperations.CaptureSnapshot();
        RuntimePlayersSnapshot playerSnapshot = playerOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimePlayerSnapshot> players = playerSnapshot.Players.Span;

        AppendTps(runtime.ObservedTicksPerSecond);

        rows[0].Text =
            $"{runtime.Lifecycle} | world {SanitizeText(runtime.WorldName, 26)} | players {players.Length}/{runtime.MaxPlayers} | " +
            $"port {runtime.Port} | interest {(runtime.InterestManagementEnabled ? "ON" : "OFF")}";
        rows[1].Text =
            $"TPS {runtime.ObservedTicksPerSecond,5:F1}/{runtime.TargetTicksPerSecond,-3} {RenderTpsGraph(runtime.TargetTicksPerSecond)}";
        rows[2].Text =
            $"Tick #{runtime.Tick:N0}  last {runtime.LastTickMilliseconds:F2} ms  worst {runtime.WorstTickMilliseconds:F2} ms  " +
            $"missed {runtime.MissedTickDeadlines:N0}  CPU {runtime.ProcessCpuPercent:F1}%  heap {FormatMebibytes(runtime.ManagedHeapBytes)}";
        rows[3].Text =
            $"Network  active {network.ActiveConnections}  inbound {network.InboundWindowFrames:N0} f/s {FormatKibibytes(network.InboundWindowBytes)}/s  " +
            $"queued {network.QueuedOutboundFrames:N0} frames/{FormatKibibytes(network.QueuedOutboundBytes)}  slow {network.SlowClients}";
        rows[4].Text =
            $"Relay    move {network.RelayedMovementFrames:N0}  npc {network.NpcRelayedFrames:N0}  projectile {network.ProjectileRelayedFrames:N0}  " +
            $"items {network.WorldItemRelayedFrames:N0}  rejected-in {network.RejectedInboundFrames:N0}";

        rows[5].Text = "WORLDS";
        rows[6].Text =
            $"* {SanitizeText(world.Name, 28),-28} {(world.Ready ? "ready" : "loading"),-7} {world.WidthTiles}x{world.HeightTiles}  " +
            $"players {players.Length}/{runtime.MaxPlayers}  cache {(world.RuntimeCacheHit ? "hit" : "miss")}";

        rows[7].Text = "PLAYERS";
        int playerRows = Math.Min(players.Length, 3);
        for (int i = 0; i < playerRows; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            rows[8 + i].Text =
                $"#{player.Slot,-3} {SanitizeName(player.Name),-20} team {player.Team}  " +
                $"HP {(player.HasHealth ? $"{player.Life}/{player.MaxLife}" : "n/a"),-9}  " +
                $"pos {player.PositionX / 16f:F1},{player.PositionY / 16f:F1}t  conn {player.ConnectionId}";
        }
        if (playerRows == 0)
            rows[8].Text = "<no players>";
        else if (players.Length > playerRows)
            rows[10].Text += $"   ... +{players.Length - playerRows}";

        rows[11].Text = "CHAT";
        RuntimeLogSnapshot chat = logOperations.CaptureSnapshot(RuntimeLogLevel.Debug, "Chat", 2);
        FillLogRows(chat.Entries.Span, startRow: 12, rowCount: 2, emptyText: "<no chat yet>");

        rows[14].Text = "LOG";
        RuntimeLogSnapshot logs = logOperations.CaptureSnapshot(RuntimeLogLevel.Information, 2);
        FillLogRows(logs.Entries.Span, startRow: 15, rowCount: 2, emptyText: "<no runtime log entries>");

        rows[17].Text = externalDashboardFailure
            ?? lastAdminAction
            ?? $"F2 System | F3-F12 host dashboards | menu: Dashboards/Details | snapshot {runtime.CapturedAtUtc:HH:mm:ss.fff} UTC";
    }

    private void RefreshPlayers()
    {
        ClearRows();
        RuntimePlayersSnapshot snapshot = playerOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimePlayerSnapshot> players = snapshot.Players.Span;
        rows[0].Text = $"PLAYERS  {players.Length} playing";

        int visible = Math.Min(players.Length, rows.Length - 2);
        for (int i = 0; i < visible; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            string health = player.HasHealth ? $"{player.Life}/{player.MaxLife}" : "n/a";
            string mana = player.HasMana ? $"{player.Mana}/{player.MaxMana}" : "n/a";
            string mount = player.MountType == 0 ? "none" : player.MountType.ToString(CultureInfo.InvariantCulture);
            rows[i + 1].Text =
                $"#{player.Slot,3} g{player.Generation,-4} c{player.ConnectionId,-5} {SanitizeName(player.Name),-20} " +
                $"team {player.Team} pos {player.PositionX / 16f:F1},{player.PositionY / 16f:F1}t " +
                $"vel {player.VelocityX:F1},{player.VelocityY:F1} item-slot {player.SelectedItem} mount {mount} HP {health} MP {mana}";
        }

        rows[rows.Length - 1].Text =
            players.Length > visible
                ? $"... {players.Length - visible} more | F2 returns to System Dashboard"
                : "F2 returns to System Dashboard";
    }

    private void RefreshNpcs()
    {
        ClearRows();
        RuntimeNpcsSnapshot snapshot = npcOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimeNpcSnapshot> npcs = snapshot.Npcs.Span;
        rows[0].Text =
            $"NPCS  {npcs.Length} live | commits spawn {snapshot.CommittedSpawns:N0} update {snapshot.CommittedUpdates:N0} despawn {snapshot.CommittedDespawns:N0}";

        int visible = Math.Min(npcs.Length, rows.Length - 2);
        for (int i = 0; i < visible; i++)
        {
            RuntimeNpcSnapshot npc = npcs[i];
            string collision = $"{(npc.CollideX ? 'X' : '-')}{(npc.CollideY ? 'Y' : '-')}";
            string flags =
                $"{collision}/{(npc.Wet ? "wet" : "dry")}/{(npc.NoGravity ? "ng" : "g")}/{(npc.NoTileCollide ? "ntc" : "tc")}";
            rows[i + 1].Text =
                $"#{npc.Slot,3} g{npc.Generation,-4} r{npc.Revision,-5} type {npc.Type}/{npc.NetId} " +
                $"pos {npc.PositionX / 16f:F1},{npc.PositionY / 16f:F1}t vel {npc.VelocityX:F1},{npc.VelocityY:F1} " +
                $"target {npc.Target} ai {npc.Ai0:F1}/{npc.Ai1:F1}/{npc.Ai2:F1}/{npc.Ai3:F1} dir {npc.DirectionX},{npc.DirectionY} {flags}";
        }

        rows[rows.Length - 1].Text =
            npcs.Length > visible
                ? $"... {npcs.Length - visible} more | F2 returns to System Dashboard"
                : "F2 returns to System Dashboard";
    }

    private void RefreshProjectiles()
    {
        ClearRows();
        if (projectileOperations is null)
        {
            rows[0].Text = "PROJECTILES  <telemetry unavailable>";
            rows[17].Text = "F2 returns to System Dashboard";
            return;
        }

        RuntimeProjectilesSnapshot snapshot = projectileOperations.CaptureSnapshot();
        RuntimePlayersSnapshot playersSnapshot = playerOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        ReadOnlySpan<RuntimeProjectileGroupSnapshot> groups = snapshot.Groups.Span;
        rows[0].Text =
            $"PROJECTILES  {snapshot.ActiveProjectiles} live in {groups.Length} spawner/type groups | " +
            $"commits {snapshot.CommittedSpawns:N0}/{snapshot.CommittedUpdates:N0}/{snapshot.CommittedDespawns:N0}";

        int visible = Math.Min(groups.Length, rows.Length - 2);
        for (int i = 0; i < visible; i++)
        {
            RuntimeProjectileGroupSnapshot group = groups[i];
            string type = ProjectileDisplayFormatter.FormatType(group.Type);
            string owner = ProjectileDisplayFormatter.FormatOwner(group.Spawner, players);
            rows[i + 1].Text =
                $"x{group.Count,-4} {type,-28} {owner,-28} " +
                $"pos~ {group.AveragePositionX / 16f:F1},{group.AveragePositionY / 16f:F1}t " +
                $"vel~ {group.AverageVelocityX:F1},{group.AverageVelocityY:F1} dmg<={group.MaxDamage} orig<={group.MaxOriginalDamage} kb<={group.MaxKnockBack:F1}";
        }

        rows[rows.Length - 1].Text =
            groups.Length > visible
                ? $"... {groups.Length - visible} more groups | F2 returns to System Dashboard"
                : "F2 returns to System Dashboard";
    }

    private void RefreshItems()
    {
        ClearRows();
        if (worldItemOperations is null)
        {
            rows[0].Text = "ITEMS  <telemetry unavailable>";
            rows[17].Text = "F2 returns to System Dashboard";
            return;
        }

        RuntimeWorldItemsSnapshot snapshot = worldItemOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimeWorldItemGroupSnapshot> groups = snapshot.Groups.Span;
        rows[0].Text = $"ITEMS  {snapshot.ActiveItems} live in {groups.Length} item-type groups";

        int visible = Math.Min(groups.Length, rows.Length - 2);
        for (int i = 0; i < visible; i++)
        {
            RuntimeWorldItemGroupSnapshot group = groups[i];
            rows[i + 1].Text =
                $"x{group.DropCount,-4} type #{group.ItemNetId,-6} stack total {group.TotalStack,-8:N0} max {group.MaxStack,-5} " +
                $"reserved {group.ReservedDrops,-4} shimmer {group.ShimmeredDrops,-4} " +
                $"pos~ {group.AveragePositionX / 16f:F1},{group.AveragePositionY / 16f:F1}t";
        }

        rows[rows.Length - 1].Text =
            groups.Length > visible
                ? $"... {groups.Length - visible} more groups | F2 returns to System Dashboard"
                : "F2 returns to System Dashboard";
    }

    private void RefreshNetwork()
    {
        ClearRows();
        RuntimeNetworkSnapshot snapshot = networkOperations.CaptureSnapshot();
        rows[0].Text = $"NETWORK  active {snapshot.ActiveConnections}  registered {snapshot.RegisteredConnections}";
        rows[1].Text =
            $"Admission   accepted {snapshot.AcceptedConnections:N0}  rejected {snapshot.RejectedConnections:N0}  " +
            $"capacity {snapshot.AdmissionCapacityRejectedConnections:N0}  rate {snapshot.AdmissionRateRejectedConnections:N0}";
        rows[2].Text = $"Inbound 1s  {snapshot.InboundWindowFrames:N0} frames  {FormatKibibytes(snapshot.InboundWindowBytes)}  rejected {snapshot.RejectedInboundFrames:N0}";
        rows[3].Text =
            $"Outbound    queues {snapshot.TrackedOutboundQueues}  frames {snapshot.QueuedOutboundFrames:N0}  {FormatKibibytes(snapshot.QueuedOutboundBytes)}  " +
            $"peak {snapshot.PeakQueuedOutboundFrames:N0}/{FormatKibibytes(snapshot.PeakQueuedOutboundBytes)}  slow {snapshot.SlowClients}";
        rows[4].Text = $"Movement    relay {snapshot.RelayedMovementFrames:N0}  AOI resync {snapshot.MovementResyncFrames:N0}";
        rows[5].Text = $"Appearance  relay {snapshot.RelayedAppearanceFrames:N0}  baseline {snapshot.AppearanceBaselineFrames:N0}";
        rows[6].Text = $"Equipment   relay {snapshot.RelayedEquipmentFrames:N0}  baseline {snapshot.EquipmentBaselineFrames:N0}  dropped {snapshot.DroppedEquipmentSnapshotUpdates:N0}";
        rows[7].Text = $"NPC         relay {snapshot.NpcRelayedFrames:N0}  baseline {snapshot.NpcBaselineFrames:N0}  rejected {snapshot.NpcRejectedFrames:N0}";
        rows[8].Text = $"Projectile  relay {snapshot.ProjectileRelayedFrames:N0}  baseline {snapshot.ProjectileBaselineFrames:N0}  rejected {snapshot.ProjectileRejectedFrames:N0}";
        rows[9].Text = $"Items       relay {snapshot.WorldItemRelayedFrames:N0}  rejected {snapshot.WorldItemRejectedFrames:N0}";
        rows[10].Text =
            $"Stops       protocol {snapshot.StopProtocolFailures:N0}  rate {snapshot.StopRateLimited:N0}  " +
            $"handshake {snapshot.StopInvalidHandshake:N0}  unsupported {snapshot.StopUnsupportedProtocol:N0}  slow {snapshot.StopSlowClient:N0}  " +
            $"frame-rejected {snapshot.StopFrameRejected:N0}";

        ReadOnlySpan<RuntimeConnectionRateDetail> rates = snapshot.TopInboundRates.Span;
        for (int i = 0; i < Math.Min(rates.Length, 2); i++)
        {
            RuntimeConnectionRateDetail rate = rates[i];
            rows[11 + i].Text =
                $"IN  #{rate.ConnectionId,-5} {rate.WindowFrames,6:N0} f/s  {FormatKibibytes(rate.WindowBytes),10}/s  total {rate.TotalFrames:N0}";
        }
        if (rates.Length == 0)
            rows[11].Text = "IN  <no active inbound traffic>";

        rows[13].Text =
            $"Frame reject malformed {snapshot.RejectedMalformedProtocol:N0}  rate {snapshot.RejectedRateLimited:N0}  " +
            $"state {snapshot.RejectedInvalidState:N0}  gameplay {snapshot.RejectedGameplay:N0}  backpressure {snapshot.RejectedBackpressure:N0}";

        ReadOnlySpan<RuntimeConnectionQueueDetail> queues = snapshot.TopOutboundQueues.Span;
        for (int i = 0; i < Math.Min(queues.Length, 2); i++)
        {
            RuntimeConnectionQueueDetail queue = queues[i];
            rows[14 + i].Text =
                $"OUT #{queue.ConnectionId,-5} {queue.QueuedFrames:N0}/{queue.MaxFrames:N0} frames  " +
                $"{FormatKibibytes(queue.QueuedBytes)}/{FormatKibibytes(queue.MaxQueuedBytes)}  " +
                $"peak {queue.PeakQueuedFrames:N0}/{FormatKibibytes(queue.PeakQueuedBytes)}  rejected {queue.RejectedFrames:N0}  {(queue.SlowClient ? "SLOW" : "ok")}";
        }
        if (queues.Length == 0)
            rows[14].Text = "OUT <no queued/rejected/slow clients>";

        rows[16].Text =
            $"Timeouts    handshake {snapshot.StopHandshakeTimeout:N0}  join {snapshot.StopJoinTimeout:N0}  idle {snapshot.StopIdleTimeout:N0}  " +
            $"application-stop {snapshot.StopApplicationStopped:N0}";
        rows[17].Text = "F2 returns to System Dashboard";
    }

    private void RefreshWorld()
    {
        ClearRows();
        RuntimeWorldSnapshot snapshot = worldOperations.CaptureSnapshot();
        rows[0].Text = $"WORLD  {(snapshot.Ready ? "ready" : "not ready")}  {SanitizeText(snapshot.Name, 36)}  id {snapshot.WorldId}";
        rows[1].Text = $"Identity    {snapshot.UniqueId:D}";
        rows[2].Text = $"Format      {snapshot.FormatVersion}  worldgen {snapshot.WorldGeneratorVersion}";
        rows[3].Text = $"Dimensions  {snapshot.WidthTiles}x{snapshot.HeightTiles}  tiles {snapshot.TileCount:N0}";
        rows[4].Text = $"Objects     chests {snapshot.ChestCount:N0}  signs {snapshot.SignCount:N0}  tile entities {snapshot.TileEntityCount:N0}";
        rows[5].Text = $"NPC state   town {snapshot.TownNpcCount:N0}  persistent {snapshot.PersistentNpcCount:N0}  rooms {snapshot.TownRoomCount:N0}";
        rows[6].Text = $"Cache       {(snapshot.RuntimeCacheHit ? "hit" : "miss")}  initial {snapshot.InitialCacheResult}  readers {snapshot.CacheParallelReads}";
        rows[7].Text = $"Load        file {snapshot.FileReadMilliseconds:F2} ms  cache {snapshot.CacheLoadMilliseconds:F2} ms  canonical {snapshot.CanonicalWorldLoadMilliseconds:F2} ms";
        rows[8].Text = $"Ready       world {snapshot.WorldReadyMilliseconds:F2} ms  network {snapshot.NetworkReadyMilliseconds:F2} ms";
        if (snapshot.RuntimeClockAvailable)
        {
            rows[10].Text = $"Clock       {(snapshot.RuntimeDayTime ? "day" : "night")}  time {snapshot.RuntimeTime:N0}  rate {snapshot.RuntimeDayRate}  moon {snapshot.RuntimeMoonPhase}";
            rows[11].Text = $"Slime rain  {snapshot.RuntimeSlimeRainTime:N0}";
        }
        if (snapshot.SectionCacheAvailable)
        {
            rows[13].Text = $"Sections    {snapshot.SectionCacheEntries:N0}/{snapshot.SectionCacheMaximumEntries:N0}  {FormatMebibytes(snapshot.SectionCacheBytes)}  dirty {snapshot.SectionCacheDirtyBacklog:N0}";
            rows[14].Text = $"Lookups     hit {snapshot.SectionCacheHits:N0}  miss {snapshot.SectionCacheMisses:N0}  stale {snapshot.SectionCacheStaleReads:N0}  waits {snapshot.SectionCacheWaits:N0}";
            rows[15].Text = $"Rebuild     queued {snapshot.SectionCachePendingWork:N0}  active {snapshot.SectionCacheActiveWorkers:N0}  published {snapshot.SectionCachePublished:N0}";
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
            rows[16].Text =
                $"Save        shadow {shadow} dirty {persistence.PendingDirtyTileSections:N0} request {request} write {write} " +
                $"done {persistence.CompletedWrites:N0}/{persistence.StartedWrites:N0} accepted {persistence.AcceptedSnapshots:N0} " +
                $"coalesced {persistence.CoalescedSnapshots:N0} failed {persistence.FailedWrites:N0}";
        }
        rows[17].Text = lastAdminAction ?? "F2 returns to System Dashboard";
    }

    private void RefreshLogs()
    {
        ClearRows();
        RuntimeLogSnapshot snapshot = logOperations.CaptureSnapshot(RuntimeLogLevel.Debug, rows.Length - 2);
        rows[0].Text = $"LOG  published {snapshot.PublishedEntries:N0}  overwritten {snapshot.OverwrittenEntries:N0}";
        ReadOnlySpan<RuntimeLogEntry> entries = snapshot.Entries.Span;
        int visible = Math.Min(entries.Length, rows.Length - 2);
        if (visible == 0)
        {
            rows[1].Text = "<no runtime log entries>";
        }
        else
        {
            int sourceStart = Math.Max(0, entries.Length - visible);
            for (int i = 0; i < visible; i++)
                rows[i + 1].Text = FormatLogEntry(entries[sourceStart + i]);
        }
        rows[17].Text = "F2 returns to System Dashboard";
    }

    private void AppendTps(double value)
    {
        tpsHistory[tpsHistoryNext] = double.IsFinite(value) && value >= 0d ? value : 0d;
        tpsHistoryNext = (tpsHistoryNext + 1) % tpsHistory.Length;
        if (tpsHistoryCount < tpsHistory.Length)
            tpsHistoryCount++;
    }

    private string RenderTpsGraph(int targetTicksPerSecond)
    {
        if (tpsHistoryCount == 0)
            return string.Empty;

        const string levels = "._-~=*#@";
        double target = Math.Max(1d, targetTicksPerSecond);
        char[] graph = new char[tpsHistoryCount];
        int oldest = (tpsHistoryNext - tpsHistoryCount + tpsHistory.Length) % tpsHistory.Length;
        for (int i = 0; i < graph.Length; i++)
        {
            double sample = tpsHistory[(oldest + i) % tpsHistory.Length];
            double ratio = Math.Clamp(sample / target, 0d, 1d);
            int level = (int)Math.Round(ratio * (levels.Length - 1));
            graph[i] = levels[level];
        }

        return new string(graph);
    }

    private void FillLogRows(
        ReadOnlySpan<RuntimeLogEntry> entries,
        int startRow,
        int rowCount,
        string emptyText)
    {
        if (entries.Length == 0)
        {
            rows[startRow].Text = emptyText;
            return;
        }

        int visible = Math.Min(entries.Length, rowCount);
        int sourceStart = entries.Length - visible;
        for (int i = 0; i < visible; i++)
            rows[startRow + i].Text = FormatLogEntry(entries[sourceStart + i]);
    }

    private void ClearRows()
    {
        foreach (Label row in rows)
            row.Text = string.Empty;
    }

    private static ExternalDashboard[] CaptureExternalDashboards(
        ITerraRuntimeTerminalDashboardSource? source)
    {
        if (source is null)
            return [];

        ReadOnlySpan<ITerraRuntimeTerminalDashboardProvider> providers = source.CaptureDashboards().Span;
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

    private static string FormatLevel(RuntimeLogLevel level) =>
        level switch
        {
            RuntimeLogLevel.Debug => "DBG",
            RuntimeLogLevel.Information => "INFO",
            RuntimeLogLevel.Warning => "WARN",
            RuntimeLogLevel.Error => "ERR",
            _ => "?"
        };

    private static string FormatMebibytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

    private static string FormatKibibytes(long bytes) =>
        (bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " KiB";

    private void ShowWorldTransferUnavailable() =>
        MessageBox.Query(
            App!,
            "World transfer",
            "This process currently owns one runtime world. Player drag/drop will be enabled only after a multi-world supervisor exposes an authoritative transfer operation.",
            "OK");

    private void ShowAbout() =>
        MessageBox.Query(
            App!,
            "TerraRuntime",
            "F2 opens the TerraRuntime System Dashboard. F3-F12 open independent dashboards registered by trusted host modules. Details exposes runtime-owned read models only.",
            "OK");

    private sealed class ExternalDashboard(ITerraRuntimeTerminalDashboardProvider provider)
    {
        public ITerraRuntimeTerminalDashboardProvider Provider { get; } = provider;
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
