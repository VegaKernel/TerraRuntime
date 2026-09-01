using TerraRuntime.Operations;

namespace TerraRuntime.TerminalUI;

/// <summary>
/// Keeps detached operations snapshots away from the Terminal.Gui thread. Built-in dashboard reads are lock-free
/// and administrative writes still delegate directly to the authoritative ingress exposed by the source operations.
/// Detail-only snapshots are refreshed on demand so a responsive UI does not become a permanent allocation tax.
/// </summary>
internal sealed class TerminalUiOperationsCache :
    IRuntimeDashboardOperations,
    IPlayerOperations,
    INpcOperations,
    IProjectileOperations,
    IWorldItemOperations,
    INetworkOperations,
    IWorldOperations,
    ILogOperations
{
    private const int DemandNpcs = 1 << 0;
    private const int DemandProjectiles = 1 << 1;
    private const int DemandWorldItems = 1 << 2;
    private const int DemandDetailLogs = 1 << 3;
    private const int MaximumCachedLogSources = 64;
    private const int OverviewFeedEntries = 64;
    private const int DetailLogEntries = 256;

    // The overview captures a richer bounded superset so Logs/Chat visibility and the log threshold can be changed
    // instantly on the UI thread without recapturing runtime state. Legacy request shapes remain recognized below
    // because DashboardWorkspaceWindow intentionally stays presentation-agnostic.
    private static readonly RuntimeLogQuery OverviewLogQuery =
        new(RuntimeLogLevel.Debug, OverviewFeedEntries);
    private static readonly RuntimeLogQuery LegacyOverviewLogQuery =
        new(RuntimeLogLevel.Information, 12);
    private static readonly RuntimeLogQuery ChatLogQuery =
        new(RuntimeLogLevel.Debug, OverviewFeedEntries, "Chat");
    private static readonly RuntimeLogQuery LegacyChatLogQuery =
        new(RuntimeLogLevel.Debug, 8, "Chat");
    private static readonly RuntimeLogQuery DetailLogQuery =
        new(RuntimeLogLevel.Debug, DetailLogEntries);

    private readonly IRuntimeDashboardOperations dashboardSource;
    private readonly IPlayerOperations playerSource;
    private readonly INpcOperations npcSource;
    private readonly IProjectileOperations? projectileSource;
    private readonly IWorldItemOperations? worldItemSource;
    private readonly INetworkOperations networkSource;
    private readonly IWorldOperations worldSource;
    private readonly ILogOperations logSource;
    private SnapshotState state;
    private int demandMask;
    private long version;

    public TerminalUiOperationsCache(
        IRuntimeDashboardOperations dashboardSource,
        IPlayerOperations playerSource,
        INpcOperations npcSource,
        INetworkOperations networkSource,
        IWorldOperations worldSource,
        ILogOperations logSource,
        IProjectileOperations? projectileSource = null,
        IWorldItemOperations? worldItemSource = null)
    {
        this.dashboardSource = dashboardSource ?? throw new ArgumentNullException(nameof(dashboardSource));
        this.playerSource = playerSource ?? throw new ArgumentNullException(nameof(playerSource));
        this.npcSource = npcSource ?? throw new ArgumentNullException(nameof(npcSource));
        this.projectileSource = projectileSource;
        this.worldItemSource = worldItemSource;
        this.networkSource = networkSource ?? throw new ArgumentNullException(nameof(networkSource));
        this.worldSource = worldSource ?? throw new ArgumentNullException(nameof(worldSource));
        this.logSource = logSource ?? throw new ArgumentNullException(nameof(logSource));

        state = CaptureInitialState();
    }

    internal long Version => Volatile.Read(ref version);

    /// <summary>
    /// Captures runtime-owned detached snapshots. This method is intentionally called from a worker task rather
    /// than the Terminal.Gui loop. Publication is one atomic reference swap, so UI reads never wait for capture.
    /// </summary>
    internal void Refresh()
    {
        SnapshotState previous = Volatile.Read(ref state);
        int demand = Interlocked.Exchange(ref demandMask, 0);

        RuntimeNpcsSnapshot npcs = (demand & DemandNpcs) != 0
            ? npcSource.CaptureSnapshot()
            : previous.Npcs;
        RuntimeProjectilesSnapshot? projectiles = projectileSource is not null && (demand & DemandProjectiles) != 0
            ? projectileSource.CaptureSnapshot()
            : previous.Projectiles;
        RuntimeWorldItemsSnapshot? worldItems = worldItemSource is not null && (demand & DemandWorldItems) != 0
            ? worldItemSource.CaptureSnapshot()
            : previous.WorldItems;
        RuntimeLogSnapshot detailLogs = (demand & DemandDetailLogs) != 0
            ? logSource.CaptureSnapshot(DetailLogQuery)
            : previous.DetailLogs;

        var next = new SnapshotState(
            dashboardSource.CaptureSnapshot(),
            playerSource.CaptureSnapshot(),
            npcs,
            projectiles,
            worldItems,
            networkSource.CaptureSnapshot(),
            worldSource.CaptureSnapshot(),
            logSource.CaptureSnapshot(OverviewLogQuery),
            logSource.CaptureSnapshot(ChatLogQuery),
            detailLogs,
            logSource.CaptureSources(MaximumCachedLogSources));

        Volatile.Write(ref state, next);
        Interlocked.Increment(ref version);
    }

    RuntimeDashboardSnapshot IRuntimeDashboardOperations.CaptureSnapshot() =>
        Volatile.Read(ref state).Dashboard;

    bool IRuntimeDashboardOperations.TrySetInterestManagementEnabled(bool enabled) =>
        dashboardSource.TrySetInterestManagementEnabled(enabled);

    RuntimePlayersSnapshot IPlayerOperations.CaptureSnapshot() =>
        Volatile.Read(ref state).Players;

    RuntimeNpcsSnapshot INpcOperations.CaptureSnapshot()
    {
        MarkDemand(DemandNpcs);
        return Volatile.Read(ref state).Npcs;
    }

    RuntimeProjectilesSnapshot IProjectileOperations.CaptureSnapshot()
    {
        if (projectileSource is null)
            throw new InvalidOperationException("Projectile operations are not available to this TUI cache.");

        MarkDemand(DemandProjectiles);
        return Volatile.Read(ref state).Projectiles
            ?? throw new InvalidOperationException("Projectile snapshot cache was not initialized.");
    }

    RuntimeWorldItemsSnapshot IWorldItemOperations.CaptureSnapshot()
    {
        if (worldItemSource is null)
            throw new InvalidOperationException("World-item operations are not available to this TUI cache.");

        MarkDemand(DemandWorldItems);
        return Volatile.Read(ref state).WorldItems
            ?? throw new InvalidOperationException("World-item snapshot cache was not initialized.");
    }

    RuntimeNetworkSnapshot INetworkOperations.CaptureSnapshot() =>
        Volatile.Read(ref state).Network;

    RuntimeWorldSnapshot IWorldOperations.CaptureSnapshot() =>
        Volatile.Read(ref state).World;

    bool IWorldOperations.TryRequestSave() => worldSource.TryRequestSave();

    RuntimeLogSnapshot ILogOperations.CaptureSnapshot(RuntimeLogQuery query)
    {
        SnapshotState snapshot = Volatile.Read(ref state);
        if (query == OverviewLogQuery || query == LegacyOverviewLogQuery)
            return snapshot.OverviewLogs;
        if (query == ChatLogQuery || query == LegacyChatLogQuery)
            return snapshot.ChatLogs;
        if (query == DetailLogQuery)
        {
            MarkDemand(DemandDetailLogs);
            return snapshot.DetailLogs;
        }

        // The built-in TUI uses the pinned queries above. Preserve interface completeness for trusted future callers
        // rather than silently returning a snapshot with unrelated filtering semantics.
        return logSource.CaptureSnapshot(query);
    }

    ReadOnlyMemory<string> ILogOperations.CaptureSources(int maxSources)
    {
        if (maxSources <= 0)
            return ReadOnlyMemory<string>.Empty;

        ReadOnlyMemory<string> sources = Volatile.Read(ref state).LogSources;
        return sources[..Math.Min(maxSources, sources.Length)];
    }

    private SnapshotState CaptureInitialState() => new(
        dashboardSource.CaptureSnapshot(),
        playerSource.CaptureSnapshot(),
        npcSource.CaptureSnapshot(),
        projectileSource?.CaptureSnapshot(),
        worldItemSource?.CaptureSnapshot(),
        networkSource.CaptureSnapshot(),
        worldSource.CaptureSnapshot(),
        logSource.CaptureSnapshot(OverviewLogQuery),
        logSource.CaptureSnapshot(ChatLogQuery),
        logSource.CaptureSnapshot(DetailLogQuery),
        logSource.CaptureSources(MaximumCachedLogSources));

    private void MarkDemand(int demand) => Interlocked.Or(ref demandMask, demand);

    private sealed record SnapshotState(
        RuntimeDashboardSnapshot Dashboard,
        RuntimePlayersSnapshot Players,
        RuntimeNpcsSnapshot Npcs,
        RuntimeProjectilesSnapshot? Projectiles,
        RuntimeWorldItemsSnapshot? WorldItems,
        RuntimeNetworkSnapshot Network,
        RuntimeWorldSnapshot World,
        RuntimeLogSnapshot OverviewLogs,
        RuntimeLogSnapshot ChatLogs,
        RuntimeLogSnapshot DetailLogs,
        ReadOnlyMemory<string> LogSources);
}