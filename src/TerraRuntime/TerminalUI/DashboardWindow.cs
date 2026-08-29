using System.Globalization;
using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

internal sealed class DashboardWindow : Runnable
{
    private const int RowCount = 18;
    private const int MaximumLogSources = 32;
    private readonly IRuntimeDashboardOperations dashboardOperations;
    private readonly IPlayerOperations playerOperations;
    private readonly INpcOperations npcOperations;
    private readonly IProjectileOperations? projectileOperations;
    private readonly IWorldItemOperations? worldItemOperations;
    private readonly INetworkOperations networkOperations;
    private readonly IWorldOperations worldOperations;
    private readonly ILogOperations logOperations;
    private readonly Label[] rows = new Label[RowCount];
    private TerminalUiScreen screen;
    private RuntimeLogLevel minimumLogLevel = RuntimeLogLevel.Information;
    private string? logSourceFilter;
    private RuntimeLogSnapshot lastLogSnapshot;
    private string? lastAdminAction;
    private bool hasLogSnapshot;
    private bool logPaused;

    public DashboardWindow(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INpcOperations npcOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
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
                        new MenuItem("_NPCs", "Live authoritative NPC read model", ShowNpcs),
                        new MenuItem("P_rojectiles", "Grouped authoritative projectile read model", ShowProjectiles),
                        new MenuItem("_Items", "Grouped authoritative dropped-item read model", ShowItems),
                        new MenuItem("_Network", "Connection and replication counters", ShowNetwork),
                        new MenuItem("_World", "Validated world and cache state", ShowWorld),
                        new MenuItem("_Logs", "Bounded runtime event log", ShowLogs)
                    ]),
                new MenuBarItem(
                    "_Actions",
                    [
                        new MenuItem(
                            "_Enable interest management",
                            "Queue the runtime-owned visibility optimization through the authoritative command boundary",
                            () => SetInterestManagementEnabled(true)),
                        new MenuItem(
                            "_Disable interest management",
                            "Queue disabling runtime-owned visibility optimization through the authoritative command boundary",
                            () => SetInterestManagementEnabled(false))
                    ]),
                new MenuBarItem(
                    "_Logs",
                    [
                        new MenuItem("_All", "Show debug and above", () => SetLogLevel(RuntimeLogLevel.Debug)),
                        new MenuItem("_Information+", "Show information and above", () => SetLogLevel(RuntimeLogLevel.Information)),
                        new MenuItem("_Warnings+", "Show warnings and errors", () => SetLogLevel(RuntimeLogLevel.Warning)),
                        new MenuItem("_Errors", "Show only errors", () => SetLogLevel(RuntimeLogLevel.Error)),
                        new MenuItem("Next _source", "Cycle through sources currently retained in the bounded log", CycleLogSource),
                        new MenuItem("Clear source _filter", "Show all log sources", ClearLogSource),
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
            case TerminalUiScreen.Npcs:
                RefreshNpcs();
                break;
            case TerminalUiScreen.Projectiles:
                RefreshProjectiles();
                break;
            case TerminalUiScreen.Items:
                RefreshItems();
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

    internal void ShowNpcs() => SelectScreen(TerminalUiScreen.Npcs, "TerraRuntime - NPCs");

    internal void ShowProjectiles() => SelectScreen(TerminalUiScreen.Projectiles, "TerraRuntime - Projectiles");

    internal void ShowItems() => SelectScreen(TerminalUiScreen.Items, "TerraRuntime - Items");

    internal void ShowNetwork() => SelectScreen(TerminalUiScreen.Network, "TerraRuntime - Network");

    internal void ShowWorld() => SelectScreen(TerminalUiScreen.World, "TerraRuntime - World");

    internal void ShowLogs() => SelectScreen(TerminalUiScreen.Logs, "TerraRuntime - Logs");

    internal void SetInterestManagementEnabled(bool enabled)
    {
        bool queued = dashboardOperations.TrySetInterestManagementEnabled(enabled);
        lastAdminAction = queued
            ? $"Admin     : queued interest management {(enabled ? "enable" : "disable")} command"
            : $"Admin     : rejected interest management {(enabled ? "enable" : "disable")} command (queue full/stopping)";

        if (screen == TerminalUiScreen.Dashboard)
            RefreshDashboard();
    }

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
        rows[9].Text = $"Memory    : heap {FormatMebibytes(snapshot.ManagedHeapBytes)}   allocated {FormatMebibytes(snapshot.TotalAllocatedBytes)}   working set {FormatMebibytes(snapshot.WorkingSetBytes)}   GC 0/1/2 {snapshot.Gen0Collections:N0}/{snapshot.Gen1Collections:N0}/{snapshot.Gen2Collections:N0}";
        rows[10].Text = $"Process   : CPU {snapshot.ProcessCpuPercent:F1}%   GC pause {snapshot.GcPauseTimePercentage:F2}%";
        rows[11].Text = $"Snapshot  : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
        if (lastAdminAction is not null)
            rows[12].Text = lastAdminAction;
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
            string mount = player.MountType == 0 ? "none" : player.MountType.ToString(CultureInfo.InvariantCulture);
            rows[i + 1].Text =
                $"#{player.Slot,3} g{player.Generation,-4} c{player.ConnectionId,-5} {name,-20} team {player.Team} " +
                $"pos {player.PositionX / 16f:F1},{player.PositionY / 16f:F1}t vel {player.VelocityX:F1},{player.VelocityY:F1} " +
                $"item-slot {player.SelectedItem} mount {mount} HP {health} MP {mana}";
        }

        if (players.Length > visible)
            rows[rows.Length - 2].Text = $"... {players.Length - visible} more player(s) not shown in this compact view";

        rows[rows.Length - 1].Text = $"Snapshot: {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshNpcs()
    {
        ClearRows();
        RuntimeNpcsSnapshot snapshot = npcOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimeNpcSnapshot> npcs = snapshot.Npcs.Span;
        rows[0].Text =
            $"Live NPCs  : {npcs.Length}   commits spawn {snapshot.CommittedSpawns:N0}   " +
            $"update {snapshot.CommittedUpdates:N0}   despawn {snapshot.CommittedDespawns:N0}";

        int visible = Math.Min(npcs.Length, rows.Length - 3);
        for (int i = 0; i < visible; i++)
        {
            RuntimeNpcSnapshot npc = npcs[i];
            string collision = $"{(npc.CollideX ? 'X' : '-')}{(npc.CollideY ? 'Y' : '-')}";
            string flags =
                $"{collision}/{(npc.Wet ? "wet" : "dry")}/{(npc.NoGravity ? "ng" : "g")}/{(npc.NoTileCollide ? "ntc" : "tc")}";
            rows[i + 1].Text =
                $"#{npc.Slot,3} g{npc.Generation,-4} r{npc.Revision,-5} type {npc.Type}/{npc.NetId} " +
                $"pos {npc.PositionX / 16f:F1},{npc.PositionY / 16f:F1}t vel {npc.VelocityX:F1},{npc.VelocityY:F1} " +
                $"target {npc.Target} ai {npc.Ai0:F1}/{npc.Ai1:F1}/{npc.Ai2:F1}/{npc.Ai3:F1} " +
                $"dir {npc.DirectionX},{npc.DirectionY} {flags}";
        }

        if (npcs.Length > visible)
            rows[rows.Length - 2].Text = $"... {npcs.Length - visible} more NPC(s) not shown in this compact view";

        rows[rows.Length - 1].Text = $"Snapshot: {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshProjectiles()
    {
        ClearRows();
        if (projectileOperations is null)
        {
            rows[0].Text = "Projectile telemetry: <unavailable>";
            return;
        }

        RuntimeProjectilesSnapshot snapshot = projectileOperations.CaptureSnapshot();
        RuntimePlayersSnapshot playersSnapshot = playerOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        ReadOnlySpan<RuntimeProjectileGroupSnapshot> groups = snapshot.Groups.Span;
        rows[0].Text =
            $"Live projectiles: {snapshot.ActiveProjectiles} in {groups.Length} owner/type group(s)   " +
            $"commits spawn {snapshot.CommittedSpawns:N0} update {snapshot.CommittedUpdates:N0} despawn {snapshot.CommittedDespawns:N0}";

        int visible = Math.Min(groups.Length, rows.Length - 3);
        for (int i = 0; i < visible; i++)
        {
            RuntimeProjectileGroupSnapshot group = groups[i];
            string type = ProjectileDisplayFormatter.FormatType(group.Type);
            string owner = ProjectileDisplayFormatter.FormatOwner(group.Spawner, players);
            rows[i + 1].Text =
                $"x{group.Count,-4} {type,-28} {owner,-28} " +
                $"pos~ {group.AveragePositionX / 16f:F1},{group.AveragePositionY / 16f:F1}t " +
                $"vel~ {group.AverageVelocityX:F1},{group.AverageVelocityY:F1} " +
                $"dmg<={group.MaxDamage} orig<={group.MaxOriginalDamage} kb<={group.MaxKnockBack:F1}";
        }

        if (groups.Length > visible)
            rows[rows.Length - 2].Text = $"... {groups.Length - visible} more projectile group(s) not shown";

        rows[rows.Length - 1].Text = $"Snapshot: {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshItems()
    {
        ClearRows();
        if (worldItemOperations is null)
        {
            rows[0].Text = "World-item telemetry: <unavailable>";
            return;
        }

        RuntimeWorldItemsSnapshot snapshot = worldItemOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimeWorldItemGroupSnapshot> groups = snapshot.Groups.Span;
        rows[0].Text = $"Live items  : {snapshot.ActiveItems} in {groups.Length} item-type group(s)";

        int visible = Math.Min(groups.Length, rows.Length - 3);
        for (int i = 0; i < visible; i++)
        {
            RuntimeWorldItemGroupSnapshot group = groups[i];
            rows[i + 1].Text =
                $"x{group.DropCount,-4} type #{group.ItemNetId,-6} stack total {group.TotalStack,-8:N0} max {group.MaxStack,-5} " +
                $"reserved {group.ReservedDrops,-4} shimmer {group.ShimmeredDrops,-4} " +
                $"pos~ {group.AveragePositionX / 16f:F1},{group.AveragePositionY / 16f:F1}t";
        }

        if (groups.Length > visible)
            rows[rows.Length - 2].Text = $"... {groups.Length - visible} more item group(s) not shown";

        rows[rows.Length - 1].Text = $"Snapshot: {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshNetwork()
    {
        ClearRows();
        RuntimeNetworkSnapshot snapshot = networkOperations.CaptureSnapshot();
        rows[0].Text = $"Connections : active {snapshot.ActiveConnections}   registered {snapshot.RegisteredConnections}";
        rows[1].Text = $"Admission   : accepted {snapshot.AcceptedConnections:N0}   rejected {snapshot.RejectedConnections:N0}";
        rows[2].Text = $"Queues      : tracked {snapshot.TrackedOutboundQueues}   frames {snapshot.QueuedOutboundFrames:N0}   bytes {snapshot.QueuedOutboundBytes:N0}   rejected {snapshot.RejectedOutboundFrames:N0}   slow {snapshot.SlowClients}";
        rows[3].Text = $"Movement    : relayed {snapshot.RelayedMovementFrames:N0}   AOI resync {snapshot.MovementResyncFrames:N0}";
        rows[4].Text = $"Appearance  : relayed {snapshot.RelayedAppearanceFrames:N0}   baselines {snapshot.AppearanceBaselineFrames:N0}";
        rows[5].Text = $"Equipment   : relayed {snapshot.RelayedEquipmentFrames:N0}   baselines {snapshot.EquipmentBaselineFrames:N0}   dropped snapshots {snapshot.DroppedEquipmentSnapshotUpdates:N0}";
        rows[6].Text = $"Lifecycle   : active baselines {snapshot.PlayerActiveBaselineFrames:N0}   deactivations {snapshot.PlayerDeactivationFrames:N0}";
        rows[7].Text = $"NPC packet23: relayed {snapshot.NpcRelayedFrames:N0}   baselines {snapshot.NpcBaselineFrames:N0}   rejected {snapshot.NpcRejectedFrames:N0}   unsupported {snapshot.NpcUnsupportedCommits:N0}";
        rows[8].Text = $"Proj packet27: relayed {snapshot.ProjectileRelayedFrames:N0}   baselines {snapshot.ProjectileBaselineFrames:N0}   rejected {snapshot.ProjectileRejectedFrames:N0}   unsupported {snapshot.ProjectileUnsupportedCommits:N0}";
        rows[9].Text = $"Items 21/22 : relayed {snapshot.WorldItemRelayedFrames:N0}   rejected {snapshot.WorldItemRejectedFrames:N0}   unsupported {snapshot.WorldItemUnsupportedCommits:N0}";
        rows[10].Text = $"Inbound 1s  : tracked {snapshot.TrackedInboundRates}   frames {snapshot.InboundWindowFrames:N0}   bytes {snapshot.InboundWindowBytes:N0}   rejected {snapshot.RejectedInboundFrames:N0}";

        ReadOnlySpan<RuntimeConnectionRateDetail> rates = snapshot.TopInboundRates.Span;
        for (int i = 0; i < Math.Min(rates.Length, 2); i++)
        {
            RuntimeConnectionRateDetail rate = rates[i];
            rows[i + 11].Text =
                $"Inbound #{rate.ConnectionId,-5}: 1s {rate.WindowFrames:N0} frames / {FormatMebibytes(rate.WindowBytes)}   " +
                $"total {rate.TotalFrames:N0} / {FormatMebibytes(rate.TotalBytes)}   rejected {rate.RejectedFrames:N0}";
        }
        if (rates.Length == 0)
            rows[11].Text = "Inbound detail: <no active inbound traffic>";

        ReadOnlySpan<RuntimeConnectionQueueDetail> queues = snapshot.TopOutboundQueues.Span;
        for (int i = 0; i < Math.Min(queues.Length, 2); i++)
        {
            RuntimeConnectionQueueDetail queue = queues[i];
            rows[i + 13].Text =
                $"Queue #{queue.ConnectionId,-5}: frames {queue.QueuedFrames:N0}   bytes {FormatMebibytes(queue.QueuedBytes)}   " +
                $"rejected {queue.RejectedFrames:N0}   {(queue.SlowClient ? "SLOW" : "ok")}";
        }
        if (queues.Length == 0)
            rows[13].Text = "Queue detail : <no queued/rejected/slow clients>";

        rows[rows.Length - 1].Text = $"Snapshot    : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
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

        if (snapshot.RuntimeClockAvailable)
        {
            rows[10].Text =
                $"Clock      : {(snapshot.RuntimeDayTime ? "day" : "night")}   time {snapshot.RuntimeTime:N0}   " +
                $"rate {snapshot.RuntimeDayRate}   moon {snapshot.RuntimeMoonPhase}";
            string slimeRainState = snapshot.RuntimeSlimeRainTime > 0d
                ? "active"
                : snapshot.RuntimeSlimeRainTime < 0d
                    ? "cooldown"
                    : "inactive";
            rows[11].Text = $"Slime rain : {slimeRainState}   timer {snapshot.RuntimeSlimeRainTime:N0}";
        }
        else
        {
            rows[10].Text = "Clock      : <runtime clock telemetry unavailable>";
        }

        if (snapshot.SectionCacheAvailable)
        {
            rows[12].Text =
                $"Section cache: {snapshot.SectionCacheEntries:N0}/{snapshot.SectionCacheMaximumEntries:N0} entries   " +
                $"{FormatMebibytes(snapshot.SectionCacheBytes)}   dirty {snapshot.SectionCacheDirtyBacklog:N0}   in-flight {snapshot.SectionCacheInFlight:N0}";
            rows[13].Text =
                $"Lookups     : hit {snapshot.SectionCacheHits:N0}   miss {snapshot.SectionCacheMisses:N0}   " +
                $"stale {snapshot.SectionCacheStaleReads:N0}   waits {snapshot.SectionCacheWaits:N0}";
            rows[14].Text =
                $"Waits       : completed {snapshot.SectionCacheWaitCompletions:N0}   timeout {snapshot.SectionCacheWaitTimeouts:N0}   " +
                $"on-demand pending {snapshot.SectionCacheOnDemandPendingRequests:N0}";
            rows[15].Text =
                $"Rebuilds    : submitted {snapshot.SectionCacheSubmitted:N0}   published {snapshot.SectionCachePublished:N0}   " +
                $"stale {snapshot.SectionCacheStaleResults:N0}   rejected {snapshot.SectionCacheRejected:N0}";
            rows[16].Text =
                $"Workers     : active {snapshot.SectionCacheActiveWorkers:N0}   queued {snapshot.SectionCachePendingWork:N0}   " +
                $"encode fail {snapshot.SectionCacheEncodeFailures:N0}   publish fail {snapshot.SectionCachePublishRejections:N0}   " +
                $"demand {snapshot.SectionCacheOnDemandRequests:N0}/{snapshot.SectionCacheOnDemandUniqueRequests:N0}/{snapshot.SectionCacheOnDemandDeduplicatedRequests:N0}";
        }
        else
        {
            rows[12].Text = "Section cache: <runtime rebuild telemetry unavailable>";
        }

        rows[17].Text = $"Snapshot   : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private void RefreshLogs()
    {
        if (!logPaused || !hasLogSnapshot)
        {
            lastLogSnapshot = logOperations.CaptureSnapshot(
                minimumLogLevel,
                logSourceFilter,
                rows.Length - 2);
            hasLogSnapshot = true;
        }

        ClearRows();
        string source = logSourceFilter is null ? "all" : SanitizeText(logSourceFilter, 24);
        rows[0].Text =
            $"Level {minimumLogLevel}+   source {source}   follow {(logPaused ? "paused" : "on")}   " +
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

    private void CycleLogSource()
    {
        ReadOnlyMemory<string> sourceSnapshot = logOperations.CaptureSources(MaximumLogSources);
        ReadOnlySpan<string> sources = sourceSnapshot.Span;
        if (sources.Length == 0)
        {
            logSourceFilter = null;
        }
        else if (logSourceFilter is null)
        {
            logSourceFilter = sources[0];
        }
        else
        {
            int currentIndex = -1;
            for (int i = 0; i < sources.Length; i++)
            {
                if (string.Equals(sources[i], logSourceFilter, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }

            logSourceFilter = currentIndex < 0
                ? sources[0]
                : currentIndex + 1 < sources.Length
                    ? sources[currentIndex + 1]
                    : null;
        }

        hasLogSnapshot = false;
        if (screen == TerminalUiScreen.Logs)
            RefreshLogs();
    }

    private void ClearLogSource()
    {
        logSourceFilter = null;
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
        Npcs,
        Projectiles,
        Items,
        Network,
        World,
        Logs
    }
}
