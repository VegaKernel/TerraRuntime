using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Application.TerminalUI;

/// <summary>
/// Keeps detached operations snapshots away from the Terminal.Gui thread. Built-in dashboard reads are lock-free
/// and administrative writes still delegate directly to the authoritative ingress exposed by the source operations.
/// Detail-only snapshots are refreshed on demand so a responsive UI does not become a permanent allocation tax.
/// World-scoped detail requests select a stable runtime ID; the worker resolves the current session after sandbox
/// regeneration and publishes only detached snapshots for that selected world.
/// </summary>
internal sealed class OperationsCache :
    IRuntimeDashboardOperations,
    IPlayerOperations,
    INpcOperations,
    IProjectileOperations,
    IWorldItemOperations,
    INetworkOperations,
    IWorldOperations,
    ILogOperations,
    IRuntimeWorldInspectionOperations
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
        new(OperationsLogLevel.Debug, OverviewFeedEntries);
    private static readonly RuntimeLogQuery LegacyOverviewLogQuery =
        new(OperationsLogLevel.Information, 12);
    private static readonly RuntimeLogQuery ChatLogQuery =
        new(OperationsLogLevel.Debug, OverviewFeedEntries, "Chat");
    private static readonly RuntimeLogQuery LegacyChatLogQuery =
        new(OperationsLogLevel.Debug, 8, "Chat");
    private static readonly RuntimeLogQuery DetailLogQuery =
        new(OperationsLogLevel.Debug, DetailLogEntries);

    private readonly IRuntimeDashboardOperations dashboardSource;
    private readonly IPlayerOperations playerSource;
    private readonly INpcOperations npcSource;
    private readonly IProjectileOperations? projectileSource;
    private readonly IWorldItemOperations? worldItemSource;
    private readonly INetworkOperations networkSource;
    private readonly IWorldOperations worldSource;
    private readonly ILogOperations logSource;
    private readonly SandboxOperations? sandboxSource;
    private readonly WorldInspectionCache? worldInspectionCache;
    private SnapshotState state;
    private int demandMask;
    private long version;

    public OperationsCache(
        IRuntimeDashboardOperations dashboardSource,
        IPlayerOperations playerSource,
        INpcOperations npcSource,
        INetworkOperations networkSource,
        IWorldOperations worldSource,
        ILogOperations logSource,
        IProjectileOperations? projectileSource = null,
        IWorldItemOperations? worldItemSource = null,
        SandboxOperations? sandboxSource = null,
        IRuntimeWorldInspectionOperations? worldInspectionSource = null)
    {
        this.dashboardSource = dashboardSource ?? throw new ArgumentNullException(nameof(dashboardSource));
        this.playerSource = playerSource ?? throw new ArgumentNullException(nameof(playerSource));
        this.npcSource = npcSource ?? throw new ArgumentNullException(nameof(npcSource));
        this.projectileSource = projectileSource;
        this.worldItemSource = worldItemSource;
        this.networkSource = networkSource ?? throw new ArgumentNullException(nameof(networkSource));
        this.worldSource = worldSource ?? throw new ArgumentNullException(nameof(worldSource));
        this.logSource = logSource ?? throw new ArgumentNullException(nameof(logSource));
        this.sandboxSource = sandboxSource;
        worldInspectionCache = worldInspectionSource is null
            ? null
            : new WorldInspectionCache(worldInspectionSource);

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

        worldInspectionCache?.Refresh();

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
            logSource.CaptureSources(MaximumCachedLogSources),
            sandboxSource?.CaptureTreeSnapshot() ?? default);

        Volatile.Write(ref state, next);
        Interlocked.Increment(ref version);
    }

    RuntimeDashboardSnapshot IRuntimeDashboardOperations.CaptureSnapshot() =>
        Volatile.Read(ref state).Dashboard;

    bool IRuntimeDashboardOperations.TrySetInterestManagementEnabled(bool enabled) =>
        dashboardSource.TrySetInterestManagementEnabled(enabled);

    ListenerChangeResult IRuntimeDashboardOperations.TryChangeListenerEndpoint(string bindAddress, int port) =>
        dashboardSource.TryChangeListenerEndpoint(bindAddress, port);

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

    ReadOnlyMemory<RuntimeWorldInspectionTarget> IRuntimeWorldInspectionOperations.CaptureTargets() =>
        worldInspectionCache?.CaptureTargets() ?? ReadOnlyMemory<RuntimeWorldInspectionTarget>.Empty;

    bool IRuntimeWorldInspectionOperations.TryCaptureRuntime(
        WorldRuntimeId runtimeId,
        out WorldRuntimeSnapshot snapshot)
    {
        if (worldInspectionCache is not null)
            return worldInspectionCache.TryCaptureRuntime(runtimeId, out snapshot);

        snapshot = default;
        return false;
    }

    bool IRuntimeWorldInspectionOperations.TryCapturePlayers(
        WorldRuntimeId runtimeId,
        out RuntimePlayersSnapshot snapshot)
    {
        if (worldInspectionCache is not null)
            return worldInspectionCache.TryCapturePlayers(runtimeId, out snapshot);

        snapshot = default;
        return false;
    }

    bool IRuntimeWorldInspectionOperations.TryCaptureNpcs(
        WorldRuntimeId runtimeId,
        out RuntimeNpcsSnapshot snapshot)
    {
        if (worldInspectionCache is not null)
            return worldInspectionCache.TryCaptureNpcs(runtimeId, out snapshot);

        snapshot = default;
        return false;
    }

    bool IRuntimeWorldInspectionOperations.TryCaptureProjectiles(
        WorldRuntimeId runtimeId,
        out RuntimeProjectilesSnapshot snapshot)
    {
        if (worldInspectionCache is not null)
            return worldInspectionCache.TryCaptureProjectiles(runtimeId, out snapshot);

        snapshot = default;
        return false;
    }

    bool IRuntimeWorldInspectionOperations.TryCaptureWorldItems(
        WorldRuntimeId runtimeId,
        out RuntimeWorldItemsSnapshot snapshot)
    {
        if (worldInspectionCache is not null)
            return worldInspectionCache.TryCaptureWorldItems(runtimeId, out snapshot);

        snapshot = default;
        return false;
    }

    internal SandboxTreeSnapshot CaptureSandboxTreeSnapshot() =>
        Volatile.Read(ref state).SandboxTree;

    private SnapshotState CaptureInitialState()
    {
        return new SnapshotState(
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
            logSource.CaptureSources(MaximumCachedLogSources),
            sandboxSource?.CaptureTreeSnapshot() ?? default);
    }

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
        ReadOnlyMemory<string> LogSources,
        SandboxTreeSnapshot SandboxTree);
}
