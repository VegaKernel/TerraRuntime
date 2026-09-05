using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Application.TerminalUI;

/// <summary>
/// Worker-refreshed cache for one operator-selected live world. The Terminal.Gui thread only records demand by
/// stable runtime ID and reads already detached snapshots. Sandbox session replacement therefore does not require
/// UI code to retain mutable runtime references or perform runtime lookup on the input thread.
/// </summary>
internal sealed class WorldInspectionCache : IRuntimeWorldInspectionOperations
{
    private const int DemandRuntime = 1 << 0;
    private const int DemandPlayers = 1 << 1;
    private const int DemandNpcs = 1 << 2;
    private const int DemandProjectiles = 1 << 3;
    private const int DemandWorldItems = 1 << 4;

    private readonly IRuntimeWorldInspectionOperations source;
    private readonly object demandGate = new();
    private SnapshotState state;
    private int demandMask;
    private WorldRuntimeId requestedWorld;

    public WorldInspectionCache(IRuntimeWorldInspectionOperations source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        state = CaptureInitialState();
        requestedWorld = state.WorldId;
    }

    public ReadOnlyMemory<RuntimeWorldInspectionTarget> CaptureTargets() =>
        Volatile.Read(ref state).Targets;

    public bool TryCaptureRuntime(WorldRuntimeId runtimeId, out WorldRuntimeSnapshot snapshot)
    {
        Request(runtimeId, DemandRuntime);
        SnapshotState current = Volatile.Read(ref state);
        if (current.WorldId == runtimeId && current.Runtime is WorldRuntimeSnapshot value)
        {
            snapshot = value;
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool TryCapturePlayers(WorldRuntimeId runtimeId, out RuntimePlayersSnapshot snapshot)
    {
        Request(runtimeId, DemandPlayers);
        SnapshotState current = Volatile.Read(ref state);
        if (current.WorldId == runtimeId && current.Players is RuntimePlayersSnapshot value)
        {
            snapshot = value;
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool TryCaptureNpcs(WorldRuntimeId runtimeId, out RuntimeNpcsSnapshot snapshot)
    {
        Request(runtimeId, DemandNpcs);
        SnapshotState current = Volatile.Read(ref state);
        if (current.WorldId == runtimeId && current.Npcs is RuntimeNpcsSnapshot value)
        {
            snapshot = value;
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool TryCaptureProjectiles(WorldRuntimeId runtimeId, out RuntimeProjectilesSnapshot snapshot)
    {
        Request(runtimeId, DemandProjectiles);
        SnapshotState current = Volatile.Read(ref state);
        if (current.WorldId == runtimeId && current.Projectiles is RuntimeProjectilesSnapshot value)
        {
            snapshot = value;
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool TryCaptureWorldItems(WorldRuntimeId runtimeId, out RuntimeWorldItemsSnapshot snapshot)
    {
        Request(runtimeId, DemandWorldItems);
        SnapshotState current = Volatile.Read(ref state);
        if (current.WorldId == runtimeId && current.WorldItems is RuntimeWorldItemsSnapshot value)
        {
            snapshot = value;
            return true;
        }

        snapshot = default;
        return false;
    }

    internal void Refresh()
    {
        SnapshotState previous = Volatile.Read(ref state);
        InspectionDemand demand = TakeDemand();
        ReadOnlyMemory<RuntimeWorldInspectionTarget> targets = source.CaptureTargets();
        WorldRuntimeId worldId = ResolveWorld(demand.RuntimeId, previous.WorldId, targets.Span);
        bool changed = worldId != previous.WorldId;

        WorldRuntimeSnapshot? runtime = changed ? null : previous.Runtime;
        RuntimePlayersSnapshot? players = changed ? null : previous.Players;
        RuntimeNpcsSnapshot? npcs = changed ? null : previous.Npcs;
        RuntimeProjectilesSnapshot? projectiles = changed ? null : previous.Projectiles;
        RuntimeWorldItemsSnapshot? worldItems = changed ? null : previous.WorldItems;

        if (worldId.IsAssigned)
        {
            if ((demand.Mask & DemandRuntime) != 0 && source.TryCaptureRuntime(worldId, out WorldRuntimeSnapshot runtimeSnapshot))
                runtime = runtimeSnapshot;
            if ((demand.Mask & DemandPlayers) != 0 && source.TryCapturePlayers(worldId, out RuntimePlayersSnapshot playersSnapshot))
                players = playersSnapshot;
            if ((demand.Mask & DemandNpcs) != 0 && source.TryCaptureNpcs(worldId, out RuntimeNpcsSnapshot npcSnapshot))
                npcs = npcSnapshot;
            if ((demand.Mask & DemandProjectiles) != 0 && source.TryCaptureProjectiles(worldId, out RuntimeProjectilesSnapshot projectileSnapshot))
                projectiles = projectileSnapshot;
            if ((demand.Mask & DemandWorldItems) != 0 && source.TryCaptureWorldItems(worldId, out RuntimeWorldItemsSnapshot itemSnapshot))
                worldItems = itemSnapshot;
        }

        Volatile.Write(
            ref state,
            new SnapshotState(targets, worldId, runtime, players, npcs, projectiles, worldItems));
    }

    private SnapshotState CaptureInitialState()
    {
        ReadOnlyMemory<RuntimeWorldInspectionTarget> targets = source.CaptureTargets();
        WorldRuntimeId worldId = ResolveWorld(default, default, targets.Span);
        return new SnapshotState(targets, worldId, null, null, null, null, null);
    }

    private void Request(WorldRuntimeId runtimeId, int demand)
    {
        if (!runtimeId.IsAssigned)
            return;

        lock (demandGate)
        {
            requestedWorld = runtimeId;
            demandMask |= demand;
        }
    }

    private InspectionDemand TakeDemand()
    {
        lock (demandGate)
        {
            int demand = demandMask;
            demandMask = 0;
            return new InspectionDemand(requestedWorld, demand);
        }
    }

    private static WorldRuntimeId ResolveWorld(
        WorldRuntimeId requested,
        WorldRuntimeId previous,
        ReadOnlySpan<RuntimeWorldInspectionTarget> targets)
    {
        if (ContainsRuntime(targets, requested))
            return requested;
        if (ContainsRuntime(targets, previous))
            return previous;

        foreach (RuntimeWorldInspectionTarget target in targets)
        {
            if (target.IsPrimary)
                return target.RuntimeId;
        }

        return targets.Length == 0 ? default : targets[0].RuntimeId;
    }

    private static bool ContainsRuntime(
        ReadOnlySpan<RuntimeWorldInspectionTarget> targets,
        WorldRuntimeId runtimeId)
    {
        if (!runtimeId.IsAssigned)
            return false;
        foreach (RuntimeWorldInspectionTarget target in targets)
        {
            if (target.RuntimeId == runtimeId)
                return true;
        }
        return false;
    }

    private readonly record struct InspectionDemand(WorldRuntimeId RuntimeId, int Mask);

    private sealed record SnapshotState(
        ReadOnlyMemory<RuntimeWorldInspectionTarget> Targets,
        WorldRuntimeId WorldId,
        WorldRuntimeSnapshot? Runtime,
        RuntimePlayersSnapshot? Players,
        RuntimeNpcsSnapshot? Npcs,
        RuntimeProjectilesSnapshot? Projectiles,
        RuntimeWorldItemsSnapshot? WorldItems);
}
