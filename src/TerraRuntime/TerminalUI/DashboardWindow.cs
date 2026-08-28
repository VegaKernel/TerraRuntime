using System.Globalization;
using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

internal sealed class DashboardWindow : Runnable
{
    private const int RowCount = 12;
    private readonly IRuntimeDashboardOperations dashboardOperations;
    private readonly IPlayerOperations playerOperations;
    private readonly INetworkOperations networkOperations;
    private readonly IWorldOperations worldOperations;
    private readonly ILogOperations logOperations;
    private readonly Label[] rows = new Label[RowCount];
    private TerminalUiScreen screen;
    private RuntimeLogLevel minimumLogLevel = RuntimeLogLevel.Information;
    private RuntimeLogSnapshot lastLogSnapshot;
    private bool hasLogSnapshot;
    private bool logPaused;

    public DashboardWindow(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations)
    {
        this.dashboardOperations = dashboardOperations ?? throw new ArgumentNullException(nameof(dashboardOperations));
        this.playerOperations = playerOperations ?? throw new ArgumentNullException(nameof(playerOperations));
        this.networkOperations = networkOperations ?? throw new ArgumentNullException(nameof(networkOperations));
        this.worldOperations = worldOperations ?? throw new ArgumentNullException(nameof(worldOperations));
        this.logOperations = logOperations ?? throw new ArgumentNullException(nameof(logOperations));
        Title = "TerraRuntime - Dashboard";

        MenuBar menu = new()
        {
            Menus =
            [
                new MenuBarItem(
                    "_File",
                    [new MenuItem("_Close UI", "Keep the server running and close only the terminal UI", () => App?.RequestStop())]),
                new MenuBarItem(
                    "_View",
                    [
                        new MenuItem("_Dashboard", "Runtime overview", ShowDashboard),
                        new MenuItem("_Players", "Live authoritative player read model", ShowPlayers),
                        new MenuItem("_Network", "Connection and replication counters", ShowNetwork),
                        new MenuItem("_World", "Validated world and cache state", ShowWorld),
                        new MenuItem("_Logs", "Bounded runtime event log", ShowLogs)
                    ]),
                new MenuBarItem(
                    "_Logs",
                    [
                        new MenuItem("_All", "Show debug and above", () => SetLogLevel(RuntimeLogLevel.Debug)),
                        new MenuItem("_Information+", "Show information and above", () => SetLogLevel(RuntimeLogLevel.Information)),
                        new MenuItem("_Warnings+", "Show warnings and errors", () => SetLogLevel(RuntimeLogLevel.Warning)),
                        new MenuItem("_Errors", "Show only errors", () => SetLogLevel(RuntimeLogLevel.Error)),
                        new MenuItem("_Pause / resume", "Freeze or resume the log snapshot", ToggleLogPause)
                    ]),
                new MenuBarItem(
                    "_Help",
                    [new MenuItem("_About", "", ShowAbout)])
            ]
        };

        StatusBar status = new();
        status.Add(new Shortcut(
            Application.GetDefaultKey(Command.Quit),
            "Close UI",
            () => App?.RequestStop()));

        View content = new()
        {
            Y = Pos.Bottom(menu),
            Width = Dim.Fill(),
            Height = Dim.Fill(status)
        };

        Pos y = 1;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new Label
            {
                X = 1,
                Y = y,
                Width = Dim.Fill(1)
            };
            content.Add(rows[i]);
            y = Pos.Bottom(rows[i]);
        }

        Add(menu, content, status);
    }

    public void RefreshSnapshot()
    {
        switch (screen)
        {
            case TerminalUiScreen.Players:
                RefreshPlayers();
                break;
            case TerminalUiScreen.Network:
                RefreshNetwork();
                break;
            case TerminalUiScreen.World:
                RefreshWorld();
                break;
            case TerminalUiScreen.Logs:
                RefreshLogs();
                break;
            default:
                RefreshDashboard();
                break;
        }
    }

    internal void ShowDashboard() => SelectScreen(TerminalUiScreen.Dashboard, "TerraRuntime - Dashboard");

    internal void ShowPlayers() => SelectScreen(TerminalUiScreen.Players, "TerraRuntime - Players");

    internal void ShowNetwork() => SelectScreen(TerminalUiScreen.Network, "TerraRuntime - Network");

    internal void ShowWorld() => SelectScreen(TerminalUiScreen.World, "TerraRuntime - World");

    internal void ShowLogs() => SelectScreen(TerminalUiScreen.Logs, "TerraRuntime - Logs");

    private void SelectScreen(TerminalUiScreen next, string title)
    {
        screen = next;
        Title = title;
        RefreshSnapshot();
    }

    private void RefreshDashboard()
    {
        ClearRows();
        RuntimeDashboardSnapshot snapshot = dashboardOperations.CaptureSnapshot();
        string cpuLast = snapshot.CpuTimeAvailable
            ? FormatMilliseconds(snapshot.LastTickCpuMilliseconds)
            : "n/a";
        string cpuWorst = snapshot.CpuTimeAvailable
            ? FormatMilliseconds(snapshot.WorstTickCpuMilliseconds)
            : "n/a";

        rows[0].Text = $"Lifecycle : {snapshot.Lifecycle}";
        rows[1].Text = $"World     : {snapshot.WorldName}  {snapshot.WorldWidthTiles}x{snapshot.WorldHeightTiles}";
        rows[2].Text = $"Tick      : {snapshot.Tick:N0}   TPS {snapshot.ObservedTicksPerSecond:F1}/{snapshot.TargetTicksPerSecond}";
        rows[3].Text = $"Tick wall : last {FormatMilliseconds(snapshot.LastTickMilliseconds)}   worst {FormatMilliseconds(snapshot.WorstTickMilliseconds)}";
        rows[4].Text = $"Tick CPU  : last {cpuLast}   worst {cpuWorst}";
        rows[5].Text = $"Slow phase: {snapshot.SlowestPhase}  {FormatMilliseconds(snapshot.SlowestPhaseMilliseconds)}   missed deadlines {snapshot.MissedTickDeadlines:N0}";
        rows[6].Text = $"Commands  : processed {snapshot.CommandsProcessed:N0}   pending {snapshot.PendingCommands:N0}   deferred {snapshot.DeferredCommands:N0}   rejected {snapshot.RejectedCommands:N0}";
        rows[7].Text = $"Network   : active {snapshot.ActiveConnections}/{snapshot.MaxPlayers}   accepted {snapshot.AcceptedConnections:N0}   rejected {snapshot.RejectedConnections:N0}   port {snapshot.Port}";
        rows[8].Text = $"Runtime   : interest management {(snapshot.InterestManagementEnabled ? "enabled" : "disabled")}   budget exhaustions {snapshot.CommandBudgetExhaustions:N0}   oldest command {FormatMilliseconds(snapshot.OldestPendingCommandAgeMilliseconds)}";
        rows[9].Text = $"Memory    : heap {FormatMebibytes(snapshot.ManagedHeapBytes)}   allocated {FormatMebibytes(snapshot.TotalAllocatedBytes)}   GC 0/1/2 {snapshot.Gen0Collections:N0}/{snapshot.Gen1Collections:N0}/{snapshot.Gen2Collections:N0}";
        rows[10].Text = $"Snapshot  : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshPlayers()
    {
        ClearRows();
        RuntimePlayersSnapshot snapshot = playerOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimePlayerSnapshot> players = snapshot.Players.Span;
        rows[0].Text = $"Playing players: {players.Length}";

        int visible = Math.Min(players.Length, rows.Length - 3);
        for (int i = 0; i < visible; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            string name = SanitizeName(player.Name);
            string health = player.HasHealth ? $"{player.Life}/{player.MaxLife}" : "n/a";
            string mana = player.HasMana ? $"{player.Mana}/{player.MaxMana}" : "n/a";
            rows[i + 1].Text =
                $"#{player.Slot,3} gen {player.Generation,-4} conn {player.ConnectionId,-5} {name,-20} " +
                $"team {player.Team} pos {player.PositionX / 16f:F1},{player.PositionY / 16f:F1}t HP {health} MP {mana}";
        }

        if (players.Length > visible)
            rows[rows.Length - 2].Text = $"... {players.Length - visible} more player(s) not shown in this compact view";

        rows[rows.Length - 1].Text = $"Snapshot: {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshNetwork()
    {
        ClearRows();
        RuntimeNetworkSnapshot snapshot = networkOperations.CaptureSnapshot();
        rows[0].Text = $"Connections : active {snapshot.ActiveConnections}   registered {snapshot.RegisteredConnections}";
        rows[1].Text = $"Admission   : accepted {snapshot.AcceptedConnections:N0}   rejected {snapshot.RejectedConnections:N0}";
        rows[2].Text = $"Queues      : tracked {snapshot.TrackedOutboundQueues}   frames {snapshot.QueuedOutboundFrames:N0}   bytes {snapshot.QueuedOutboundBytes:N0}";
        rows[3].Text = $"Movement    : relayed {snapshot.RelayedMovementFrames:N0}   AOI resync {snapshot.MovementResyncFrames:N0}";
        rows[4].Text = $"Appearance  : relayed {snapshot.RelayedAppearanceFrames:N0}   baselines {snapshot.AppearanceBaselineFrames:N0}";
        rows[5].Text = $"Equipment   : relayed {snapshot.RelayedEquipmentFrames:N0}   baselines {snapshot.EquipmentBaselineFrames:N0}   dropped snapshots {snapshot.DroppedEquipmentSnapshotUpdates:N0}";
        rows[6].Text = $"Lifecycle   : active baselines {snapshot.PlayerActiveBaselineFrames:N0}   deactivations {snapshot.PlayerDeactivationFrames:N0}";
        rows[8].Text = $"Backpressure: rejected frames {snapshot.RejectedOutboundFrames:N0}   slow clients {snapshot.SlowClients}";
        rows[10].Text = $"Snapshot    : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshWorld()
    {
        ClearRows();
        RuntimeWorldSnapshot snapshot = worldOperations.CaptureSnapshot();
        rows[0].Text = $"State      : {(snapshot.Ready ? "ready" : "not ready")}   world '{snapshot.Name}'   id {snapshot.WorldId}";
        rows[1].Text = $"Identity   : {snapshot.UniqueId:D}";
        rows[2].Text = $"Format     : {snapshot.FormatVersion}   worldgen {snapshot.WorldGeneratorVersion}";
        rows[3].Text = $"Dimensions : {snapshot.WidthTiles}x{snapshot.HeightTiles}   tiles {snapshot.TileCount:N0}";
        rows[4].Text = $"Objects    : chests {snapshot.ChestCount:N0}   signs {snapshot.SignCount:N0}   tile entities {snapshot.TileEntityCount:N0}   plates {snapshot.PressurePlateCount:N0}";
        rows[5].Text = $"NPC state  : town {snapshot.TownNpcCount:N0}   persistent {snapshot.PersistentNpcCount:N0}   rooms {snapshot.TownRoomCount:N0}";
        rows[6].Text = $"Cache      : {(snapshot.RuntimeCacheHit ? "hit" : "miss")}   initial {snapshot.InitialCacheResult}   readers {snapshot.CacheParallelReads}";
        rows[7].Text = $"Load       : file {FormatMilliseconds(snapshot.FileReadMilliseconds)}   cache {FormatMilliseconds(snapshot.CacheLoadMilliseconds)}   canonical {FormatMilliseconds(snapshot.CanonicalWorldLoadMilliseconds)}";
        rows[8].Text = $"Prepare    : cache write {FormatMilliseconds(snapshot.CacheWriteMilliseconds)}   bootstrap {FormatMilliseconds(snapshot.BootstrapMilliseconds)}";
        rows[9].Text = $"Ready      : world {FormatMilliseconds(snapshot.WorldReadyMilliseconds)}   network {FormatMilliseconds(snapshot.NetworkReadyMilliseconds)}";
        rows[10].Text = $"Snapshot   : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshLogs()
    {
        if (!logPaused || !hasLogSnapshot)
        {
            lastLogSnapshot = logOperations.CaptureSnapshot(minimumLogLevel, rows.Length - 2);
            hasLogSnapshot = true;
        }

        ClearRows();
        rows[0].Text =
            $"Filter {minimumLogLevel}+   follow {(logPaused ? "paused" : "on")}   " +
            $"published {lastLogSnapshot.PublishedEntries:N0}   overwritten {lastLogSnapshot.OverwrittenEntries:N0}";

        ReadOnlySpan<RuntimeLogEntry> entries = lastLogSnapshot.Entries.Span;
        if (entries.Length == 0)
        {
            rows[1].Text = "<no matching runtime log entries>";
        }
        else
        {
            int visible = Math.Min(entries.Length, rows.Length - 2);
            for (int i = 0; i < visible; i++)
            {
                RuntimeLogEntry entry = entries[i];
                rows[i + 1].Text =
                    $"{entry.TimestampUtc:HH:mm:ss.fff} {FormatLevel(entry.Level),-4} " +
                    $"{SanitizeText(entry.Source, 16),-16} {SanitizeText(entry.Message, 96)}";
            }
        }

        rows[rows.Length - 1].Text = $"Snapshot: {lastLogSnapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void SetLogLevel(RuntimeLogLevel level)
    {
        minimumLogLevel = level;
        hasLogSnapshot = false;
        if (screen == TerminalUiScreen.Logs)
            RefreshLogs();
    }

    private void ToggleLogPause()
    {
        logPaused = !logPaused;
        if (!logPaused)
            hasLogSnapshot = false;
        if (screen == TerminalUiScreen.Logs)
            RefreshLogs();
    }

    private void ClearRows()
    {
        foreach (Label row in rows)
            row.Text = string.Empty;
    }

    private static string SanitizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "<unnamed>" : SanitizeText(name, 20);

    private static string SanitizeText(string value, int maximumLength)
    {
        int length = Math.Min(value.Length, maximumLength);
        char[] buffer = new char[length];
        for (int i = 0; i < length; i++)
            buffer[i] = char.IsControl(value[i]) ? ' ' : value[i];

        return new string(buffer);
    }

    private static string FormatLevel(RuntimeLogLevel level) =>
        level switch
        {
            RuntimeLogLevel.Debug => "DBG",
            RuntimeLogLevel.Information => "INFO",
            RuntimeLogLevel.Warning => "WARN",
            RuntimeLogLevel.Error => "ERR",
            _ => "?"
        };

    private static string FormatMilliseconds(double milliseconds) =>
        milliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms";

    private static string FormatMebibytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

    private void ShowAbout() =>
        MessageBox.Query(
            App!,
            "TerraRuntime",
            "Local operations UI. Views consume bounded read models and never own mutable game state.",
            "OK");

    private enum TerminalUiScreen
    {
        Dashboard,
        Players,
        Network,
        World,
        Logs
    }
}
