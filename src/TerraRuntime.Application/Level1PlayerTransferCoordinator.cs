using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Application;

internal readonly record struct SandboxTreePlayerSnapshot(
    string Selector,
    RuntimePlayerSnapshot Player,
    bool IsPlaying)
{
    public SandboxTreePlayerSnapshot(string selector, byte slot, string name, bool isPlaying)
        : this(
            selector,
            new RuntimePlayerSnapshot(
                ConnectionId: 0, Slot: slot, Generation: 1, Name: name, Team: 0,
                PositionX: 0f, PositionY: 0f, VelocityX: 0f, VelocityY: 0f, SelectedItem: 0, MountType: 0,
                DifficultyFlags: 0, HasHealth: false, Life: 0, MaxLife: 0, HasMana: false, Mana: 0, MaxMana: 0),
            isPlaying)
    {
    }

    public byte Slot => Player.Slot;
    public string Name => Player.Name;
}

internal readonly record struct SandboxTreeWorldSnapshot(
    SandboxName? Sandbox,
    string DisplayName,
    bool IsPrimary,
    WorldRuntimeSnapshot? Runtime,
    SandboxJobSnapshot? PendingJob,
    ReadOnlyMemory<SandboxTreePlayerSnapshot> Players);

internal readonly record struct SandboxTreeSnapshot(
    ReadOnlyMemory<SandboxTreeWorldSnapshot> Worlds,
    ReadOnlyMemory<SandboxJobSnapshot> Jobs,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Resolves operator player/runtime selectors and executes the process-local WorldRuntime transfer transaction.
/// Socket ownership remains in the server process; this policy layer never emits ad-hoc Terraria packets itself.
/// </summary>
internal sealed class Level1PlayerTransferCoordinator
{
    private readonly RuntimeConnectionDirectory connections;
    private readonly WorldRegistry runtimes;
    private readonly SandboxHost sandboxes;

    public Level1PlayerTransferCoordinator(
        RuntimeConnectionDirectory connections,
        WorldRegistry runtimes,
        SandboxHost sandboxes)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        this.sandboxes = sandboxes ?? throw new ArgumentNullException(nameof(sandboxes));
    }

    public bool TryMove(
        PlayerHandle player,
        SandboxName? sandbox,
        bool forceRespawn,
        out string? error)
    {
        if (!connections.TryResolve(player, out RuntimeConnectionRoute? route) || route is null)
        {
            error = $"player #{player.Slot.Value} generation {player.Generation.Value} is no longer connected";
            return false;
        }

        return TryMove(route, sandbox, forceRespawn, out error);
    }

    public bool TryMove(
        string playerSelector,
        SandboxName? sandbox,
        bool forceRespawn,
        out string? error)
    {
        if (!connections.TryResolve(playerSelector, out RuntimeConnectionRoute? route, out error) || route is null)
            return false;

        return TryMove(route, sandbox, forceRespawn, out error);
    }

    private bool TryMove(
        RuntimeConnectionRoute route,
        SandboxName? sandbox,
        bool forceRespawn,
        out string? error)
    {
        WorldRuntime destination;
        if (sandbox is SandboxName name)
        {
            if (!sandboxes.TryGetLiveRuntime(name, out WorldRuntime? runtime, out error) || runtime is null)
                return false;
            destination = runtime;
        }
        else
        {
            if (!runtimes.TryGetPrimary(out WorldRuntime? primary) || primary is null ||
                primary.Lifecycle != WorldRuntimeLifecycle.Running)
            {
                error = "primary runtime is not running";
                return false;
            }
            destination = primary;
        }

        return route.TryTransfer(destination, forceRespawn, out error);
    }

    public bool TryKick(string playerSelector, out string? error)
    {
        if (!connections.TryResolve(playerSelector, out RuntimeConnectionRoute? route, out error) || route is null)
            return false;
        return route.TryRequestDisconnect(out error);
    }

    public SandboxTreeSnapshot CaptureTreeSnapshot()
    {
        WorldRuntimeSnapshot? primary = runtimes.TryGetPrimary(out WorldRuntime? primaryRuntime) && primaryRuntime is not null
            ? primaryRuntime.CaptureSnapshot()
            : null;
        SandboxSnapshot[] sandboxSnapshots = sandboxes.CaptureSandboxes();
        SandboxJobSnapshot[] jobs = sandboxes.CaptureJobs();
        RuntimeConnectionRouteSnapshot[] routes = connections.Capture();

        var worlds = new List<SandboxTreeWorldSnapshot>(1 + sandboxSnapshots.Length);
        if (primary is WorldRuntimeSnapshot primarySnapshot)
        {
            worlds.Add(new SandboxTreeWorldSnapshot(
                Sandbox: null,
                primarySnapshot.WorldName,
                IsPrimary: true,
                primarySnapshot,
                PendingJob: null,
                CapturePlayers(primaryRuntime!, routes)));
        }

        foreach (SandboxSnapshot sandbox in sandboxSnapshots)
        {
            SandboxJobSnapshot? pending = null;
            if (sandbox.PendingJob is SandboxJobId pendingId)
            {
                int pendingIndex = Array.FindIndex(jobs, job => job.Id == pendingId);
                if (pendingIndex >= 0)
                    pending = jobs[pendingIndex];
            }
            worlds.Add(new SandboxTreeWorldSnapshot(
                sandbox.Name,
                sandbox.Name.Value,
                IsPrimary: false,
                sandbox.Runtime,
                pending,
                CapturePlayersForSandbox(sandbox.Runtime, routes)));
        }

        foreach (SandboxJobSnapshot pendingCreate in jobs)
        {
            if (pendingCreate.Kind != SandboxJobKind.Create ||
                pendingCreate.Status is SandboxJobStatus.Completed or SandboxJobStatus.Failed or SandboxJobStatus.Canceled ||
                worlds.Any(world => world.Sandbox == pendingCreate.Sandbox))
            {
                continue;
            }

            worlds.Add(new SandboxTreeWorldSnapshot(
                pendingCreate.Sandbox,
                pendingCreate.Sandbox.Value,
                IsPrimary: false,
                Runtime: null,
                pendingCreate,
                ReadOnlyMemory<SandboxTreePlayerSnapshot>.Empty));
        }

        int sandboxStart = primary is null ? 0 : 1;
        if (worlds.Count - sandboxStart > 1)
        {
            worlds.Sort(sandboxStart, worlds.Count - sandboxStart, Comparer<SandboxTreeWorldSnapshot>.Create(
                static (left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase)));
        }

        return new SandboxTreeSnapshot(worlds.ToArray(), jobs, DateTimeOffset.UtcNow);
    }

    private ReadOnlyMemory<SandboxTreePlayerSnapshot> CapturePlayersForSandbox(
        WorldRuntimeSnapshot runtimeSnapshot,
        ReadOnlySpan<RuntimeConnectionRouteSnapshot> routes)
    {
        if (!runtimes.TryGet(runtimeSnapshot.Identity.RuntimeId, out WorldRuntime? runtime) || runtime is null)
            return ReadOnlyMemory<SandboxTreePlayerSnapshot>.Empty;
        return CapturePlayers(runtime, routes);
    }

    private static ReadOnlyMemory<SandboxTreePlayerSnapshot> CapturePlayers(
        WorldRuntime runtime,
        ReadOnlySpan<RuntimeConnectionRouteSnapshot> routes)
    {
        RuntimePlayersSnapshot playerSnapshot = runtime.PlayerOperations.CaptureSnapshot();
        ReadOnlySpan<RuntimePlayerSnapshot> runtimePlayers = playerSnapshot.Players.Span;
        var players = new List<SandboxTreePlayerSnapshot>();
        foreach (RuntimeConnectionRouteSnapshot route in routes)
        {
            if (route.Runtime != runtime.Identity || route.Player is not PlayerHandle player)
                continue;

            RuntimePlayerSnapshot snapshot = default;
            bool found = false;
            for (int i = 0; i < runtimePlayers.Length; i++)
            {
                RuntimePlayerSnapshot candidate = runtimePlayers[i];
                if (candidate.ConnectionId == route.ConnectionId && candidate.Slot == player.Slot.Value && candidate.Generation == player.Generation.Value)
                {
                    snapshot = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                string fallbackName = string.IsNullOrWhiteSpace(route.PlayerName) ? $"player-{player.Slot.Value}" : route.PlayerName;
                snapshot = new RuntimePlayerSnapshot(
                    route.ConnectionId, player.Slot.Value, player.Generation.Value, fallbackName, Team: 0,
                    PositionX: 0f, PositionY: 0f, VelocityX: 0f, VelocityY: 0f, SelectedItem: 0, MountType: 0,
                    DifficultyFlags: 0, HasHealth: false, Life: 0, MaxLife: 0, HasMana: false, Mana: 0, MaxMana: 0);
            }

            players.Add(new SandboxTreePlayerSnapshot(
                $"#{player.Slot.Value}",
                snapshot,
                route.JoinState == TerraRuntime.Core.Players.PlayerJoinState.Playing));
        }
        return players.ToArray();
    }
}
